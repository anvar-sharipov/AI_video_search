using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VMS.Core.Domain;

namespace VMS.Backend.Server.Api;

public class TokenService(string signingKey, int expiryMinutes)
{
    public const string RoleClaimType = ClaimTypes.Role;

    public (string Token, DateTimeOffset ExpiresAt) IssueToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(RoleClaimType, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static UserRole GetRole(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(TokenService.RoleClaimType);
        return Enum.TryParse<UserRole>(value, out var role) ? role : UserRole.Viewer;
    }

    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
