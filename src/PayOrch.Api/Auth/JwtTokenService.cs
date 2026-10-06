using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PayOrch.Domain.Entities;

namespace PayOrch.Api.Auth;

public class JwtTokenService
{
    private readonly JwtOptions _options;
    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) IssueToken(AppUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("token_version", user.TokenVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims,
            expires: expiresAt.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
