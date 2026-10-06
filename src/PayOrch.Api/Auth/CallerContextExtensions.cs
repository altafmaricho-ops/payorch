using System.Security.Claims;
using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Auth;

/// <summary>
/// Derives CallerContext from the validated JWT Bearer claims that ASP.NET
/// Core's authentication middleware attaches to HttpContext.User (see
/// Program.cs's AddJwtBearer setup and AuthEndpoints' /login). Every
/// [admin] endpoint using this has already passed signature + expiry
/// validation by the time this code runs — there is nothing left to trust
/// blindly here, unlike the header-based placeholder this replaced.
/// </summary>
public static class CallerContextExtensions
{
    public static CallerContext? TryGetCallerContext(this HttpRequest req)
    {
        var user = req.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return null;

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId)) return null;
        if (!Enum.TryParse<UserRole>(roleClaim, out var role)) return null;

        return new CallerContext(userId, role);
    }
}
