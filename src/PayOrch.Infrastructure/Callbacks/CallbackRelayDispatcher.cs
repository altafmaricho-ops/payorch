using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayOrch.Domain.Entities;
using PayOrch.Infrastructure.Persistence;

namespace PayOrch.Infrastructure.Callbacks;

public class CallbackRelayDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CallbackRelayDispatcher> _logger;
    private static readonly int[] BackoffSeconds = { 5, 15, 60, 300, 900, 1800, 3600, 7200 };
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(2);

    public CallbackRelayDispatcher(IServiceScopeFactory scopeFactory, ILogger<CallbackRelayDispatcher> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchDueRelaysAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Callback relay dispatch loop failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DispatchDueRelaysAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var leaseCutoff = DateTimeOffset.UtcNow - LeaseTimeout;

        List<Guid> claimedIds;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            claimedIds = await db.Database.SqlQuery<Guid>($@"
                WITH candidates AS (
                    SELECT id FROM callback_relays
                    WHERE (status = 'Pending' AND next_attempt_at <= now())
                       OR (status = 'Processing' AND last_attempt_at < {leaseCutoff})
                    ORDER BY next_attempt_at
                    LIMIT 50
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE callback_relays r
                SET status = 'Processing', last_attempt_at = now()
                FROM candidates c
                WHERE r.id = c.id
                RETURNING r.id").ToListAsync(ct);
            await tx.CommitAsync(ct);
        }

        var due = await db.CallbackRelays.Where(r => claimedIds.Contains(r.Id))
            .OrderBy(r => r.LastAttemptAt).ToListAsync(ct);
        if (due.Count == 0) return;

        var tenantIds = due.Select(x => x.TenantId).Distinct().ToList();
        var tenantSecrets = await db.Tenants.Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => string.IsNullOrWhiteSpace(t.CallbackSecret) ? t.ApiSecretHash : t.CallbackSecret, ct);
        var client = httpFactory.CreateClient("callback-relay");

        foreach (var relay in due)
        {
            if (string.IsNullOrWhiteSpace(relay.TargetUrl))
            {
                relay.Status = RelayStatus.Failed;
                relay.AttemptCount++;
                continue;
            }
            try
            {
                var secret = tenantSecrets.GetValueOrDefault(relay.TenantId, string.Empty);
                var signature = ComputeHmacSha256Hex(relay.PayloadJson, secret);
                using var content = new StringContent(relay.PayloadJson, Encoding.UTF8, "application/json");
                content.Headers.Add("X-PayOrch-Signature", signature);
                using var resp = await client.PostAsync(relay.TargetUrl, content, ct);
                relay.AttemptCount++;
                relay.LastAttemptAt = DateTimeOffset.UtcNow;
                relay.LastResponseCode = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode) relay.Status = RelayStatus.Delivered;
                else ScheduleRetryOrExhaust(relay);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relay attempt failed for order {OrderId}", relay.OrderId);
                relay.AttemptCount++;
                relay.LastAttemptAt = DateTimeOffset.UtcNow;
                ScheduleRetryOrExhaust(relay);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static void ScheduleRetryOrExhaust(CallbackRelay relay)
    {
        if (relay.AttemptCount >= BackoffSeconds.Length) { relay.Status = RelayStatus.Exhausted; return; }
        relay.Status = RelayStatus.Pending;
        relay.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(BackoffSeconds[relay.AttemptCount]);
    }

    private static string ComputeHmacSha256Hex(string message, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }
}
