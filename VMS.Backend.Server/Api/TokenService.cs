using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public class TokenService(string signingKey, int expiryMinutes)
{
    public (string Token, DateTimeOffset ExpiresAt) IssueToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimsPrincipalExtensions.RoleClaimType, user.Role.ToString())
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
