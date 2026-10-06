using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PayOrch.Api.Auth;
using PayOrch.Api.Bootstrap;
using PayOrch.Api.Endpoints;
using PayOrch.Api.Hubs;
using PayOrch.Application.Common;
using PayOrch.Application.Fraud;
using PayOrch.Application.Masking;
using PayOrch.Application.Orders;
using PayOrch.Application.Routing;
using PayOrch.Application.Psp;
using PayOrch.Application.Sellers;
using PayOrch.Application.Users;
using PayOrch.Infrastructure.Callbacks;
using PayOrch.Infrastructure.Persistence;
using PayOrch.Infrastructure.Psp;
using PayOrch.Infrastructure.Psp.Razorpay;
using Serilog;
using Npgsql;
using Npgsql.NameTranslation;
using PayOrch.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---- Persistence -----------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("A Postgres or DefaultConnection connection string is required.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

dataSourceBuilder.MapEnum<UserRole>(
    "user_role",
    new NpgsqlNullNameTranslator());

var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(dataSource));

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();

// ---- Auth: JWT Bearer for control-panel users (Super/Admin/Sub-Admin) --
// Tenants/Sellers do NOT use this — they authenticate via X-Api-Key authentication
// on the merchant-facing endpoints, a separate, simpler trust model.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR sends the JWT as a query-string token on the WebSocket
        // handshake (browsers can't set Authorization headers on the
        // initial upgrade request) — this reads it from there for the hub
        // path specifically, and only there.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal;
                var idValue = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var versionValue = principal?.FindFirst("token_version")?.Value;
                if (!Guid.TryParse(idValue, out var userId) || !int.TryParse(versionValue, out var tokenVersion))
                {
                    ctx.Fail("Invalid session claims.");
                    return;
                }

                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ctx.HttpContext.RequestAborted);
                if (user is null || user.Status != UserStatus.Active || user.IsBlocked || user.TokenVersion != tokenVersion)
                    ctx.Fail("Session is no longer active.");
            }
        };
    });

builder.Services.AddAuthorization();

// ---- PSP: Razorpay (primary, Phase 1's only adapter) ------------------
builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
builder.Services.AddHttpClient<RazorpayPspAdapter>();
builder.Services.AddScoped<IPspAdapter, RazorpayPspAdapter>();
builder.Services.AddScoped<IPspAdapterResolver>(sp =>
    new PspAdapterResolver(sp.GetServices<IPspAdapter>(), primaryCode: "razorpay"));

// ---- Masking ------------------------------------------------------------
builder.Services.AddScoped<IIdentityMasker>(sp =>
{
    var db = sp.GetRequiredService<AppDbContext>();
    return new IdentityMasker(async (tenantId, ct) =>
    {
        var tenant = await db.Tenants.FindAsync(new object?[] { tenantId }, ct);
        return tenant?.MerchantCode ?? "Merchant ??";
    });
});

// ---- Application services ------------------------------------------------
var webhookNotifyBaseUrl = builder.Configuration["Api:PublicBaseUrl"] ?? "https://localhost:44333";
builder.Services.AddScoped(sp => new CreateOrderService(
    sp.GetRequiredService<IUnitOfWork>(),
    sp.GetRequiredService<IPspAdapterResolver>(),
    webhookNotifyBaseUrl,
    sp.GetRequiredService<PaymentRoutingService>()));
builder.Services.AddScoped<WebhookIngestService>();
builder.Services.AddScoped<WebhookProcessingService>();
builder.Services.AddScoped<PaymentRoutingService>();

// ---- Hierarchy / sellers / fraud use cases -----------------------------
builder.Services.AddScoped<CreateSubordinateUserService>();
builder.Services.AddScoped<AdjustCreditService>();
builder.Services.AddScoped<OnboardSellerService>();
builder.Services.AddScoped<ListSellersService>();
builder.Services.AddScoped<ExportGuardService>();
builder.Services.AddScoped<RedFlagService>();

// ---- Callback relay dispatch worker + outbound HTTP client -----------
builder.Services.AddHttpClient("callback-relay");
builder.Services.AddHostedService<CallbackRelayDispatcher>();

// ---- Webhook processing worker (decoupled from the webhook HTTP path) --
builder.Services.AddHostedService<WebhookProcessingWorker>();

// ---- Live dashboard updates ---------------------------------------------
builder.Services.AddSignalR();
builder.Services.AddScoped<IOrderLifecycleNotifier, SignalROrderLifecycleNotifier>();

// ---- CORS: allow the React dashboard's origin(s) ------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()); // needed for SignalR's negotiate handshake
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseCors("Dashboard");
app.UseAuthentication();
app.UseAuthorization();

app.MapOrderEndpoints();
app.MapMerchantV1Endpoints();
app.MapWebhookEndpoints();
app.MapAdminEndpoints();
app.MapPlatformEndpoints();
app.MapAuthEndpoints();
app.MapHub<OrderLifecycleHub>("/hubs/orders");

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        var databaseOk = await db.Database.CanConnectAsync(ct);
        if (!databaseOk) return Results.StatusCode(503);
        return Results.Ok(new { status = "ok", database = "ok" });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
        return Results.Ok(new { status = "ready" });
    }
    catch { return Results.StatusCode(503); }
});

// Always bring the database to the application schema before workers start.
// This makes a fresh/recreated database self-healing instead of requiring a
// manual EF command or a separate Super Admin bootstrap script.
await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();
