using System.IdentityModel.Tokens.Jwt;
using VMS.Backend.Server.Api;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Tests.Api;

public class TokenServiceTests
{
    private static User CreateUser(UserRole role = UserRole.Operator, int tokenVersion = 0) => new()
    {
        Username = "test-user",
        PasswordHash = "irrelevant-for-this-test",
        Role = role,
        TokenVersion = tokenVersion
    };

    [Fact]
    public void IssueToken_embeds_the_users_role_and_token_version_as_claims()
    {
        var tokenService = new TokenService("at-least-32-bytes-long-signing-key-for-tests", expiryMinutes: 30);
        var user = CreateUser(UserRole.Admin, tokenVersion: 4);

        var (token, _) = tokenService.IssueToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == ClaimsPrincipalExtensions.RoleClaimType).Value);
        Assert.Equal("4", jwt.Claims.First(c => c.Type == ClaimsPrincipalExtensions.TokenVersionClaimType).Value);
    }

    [Fact]
    public void IssueToken_sets_expiry_according_to_the_configured_lifetime()
    {
        var tokenService = new TokenService("at-least-32-bytes-long-signing-key-for-tests", expiryMinutes: 30);

        var (_, expiresAt) = tokenService.IssueToken(CreateUser());

        var expectedExpiry = DateTimeOffset.UtcNow.AddMinutes(30);
        Assert.True(Math.Abs((expiresAt - expectedExpiry).TotalSeconds) < 5);
    }

    [Fact]
    public void IssueToken_omits_the_permissions_claim_when_the_user_has_no_additional_permissions()
    {
        var tokenService = new TokenService("at-least-32-bytes-long-signing-key-for-tests", expiryMinutes: 30);

        var (token, _) = tokenService.IssueToken(CreateUser());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == ClaimsPrincipalExtensions.PermissionsClaimType);
    }

    [Fact]
    public void IssueToken_includes_the_permissions_claim_when_the_user_has_additional_permissions()
    {
        var tokenService = new TokenService("at-least-32-bytes-long-signing-key-for-tests", expiryMinutes: 30);
        var user = CreateUser();
        user.SetAdditionalPermissions([Permission.SearchByFace]);

        var (token, _) = tokenService.IssueToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("SearchByFace", jwt.Claims.First(c => c.Type == ClaimsPrincipalExtensions.PermissionsClaimType).Value);
    }
}
