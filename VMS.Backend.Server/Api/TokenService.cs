using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

/// <summary>
/// Generates the opaque refresh-token value handed to the client, and hashes it the same way a
/// password would be before it's ever written to RefreshToken.TokenHash — only the hash is
/// persisted, so a stolen database dump doesn't hand out live sessions.
/// </summary>
public static class RefreshTokenGenerator
{
    public static string GenerateRaw() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

public class TokenService(string signingKey, int expiryMinutes)
{
    public (string Token, DateTimeOffset ExpiresAt) IssueToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimsPrincipalExtensions.RoleClaimType, user.Role.ToString()),
            new(ClaimsPrincipalExtensions.TokenVersionClaimType, user.TokenVersion.ToString())
        };

        var additional = user.AdditionalPermissionsCsv;
        if (!string.IsNullOrWhiteSpace(additional))
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.PermissionsClaimType, additional));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
