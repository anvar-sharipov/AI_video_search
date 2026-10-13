using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            VmsDbContext db,
            IAuthService auth,
            IAuditLogger auditLogger,
            TokenService tokenService,
            IOptions<VmsOptions> vmsOptions) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);

            if (user is null || !auth.VerifyPassword(request.Password, user.PasswordHash))
            {
                await auditLogger.AppendAsync(new AuditLogEntry
                {
                    Username = request.Username,
                    Action = "Login",
                    IsSuccess = false,
                    Details = "Invalid credentials"
                });
                return Results.Unauthorized();
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            var refreshToken = IssueRefreshToken(db, user.Id, vmsOptions.Value.RefreshTokenExpiryDays);
            await db.SaveChangesAsync();

            var (token, expiresAt) = tokenService.IssueToken(user);

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = user.Id,
                Username = user.Username,
                Action = "Login",
                IsSuccess = true
            });

            return Results.Ok(new LoginResponse(
                token, user.Username, user.Role.ToString(), expiresAt,
                user.GetAdditionalPermissions().Select(p => p.ToString()).ToList(), refreshToken));
        }).AllowAnonymous();

        // Lets the client silently obtain a new access token without re-prompting for a
        // password — see VmsOptions.JwtExpiryMinutes's doc comment for why the access token is
        // deliberately short-lived. Rotates the refresh token on every call (old row revoked,
        // new one issued) rather than reusing it, so a replayed old refresh token is rejected
        // instead of silently working.
        app.MapPost("/api/auth/refresh", async (
            RefreshRequest request,
            VmsDbContext db,
            TokenService tokenService,
            IOptions<VmsOptions> vmsOptions) =>
        {
            var hash = RefreshTokenGenerator.Hash(request.RefreshToken);
            var now = DateTimeOffset.UtcNow;
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
            if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt < now)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId && u.IsActive);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            stored.RevokedAt = now;
            var newRefreshToken = IssueRefreshToken(db, user.Id, vmsOptions.Value.RefreshTokenExpiryDays);
            await db.SaveChangesAsync();

            var (token, expiresAt) = tokenService.IssueToken(user);

            return Results.Ok(new LoginResponse(
                token, user.Username, user.Role.ToString(), expiresAt,
                user.GetAdditionalPermissions().Select(p => p.ToString()).ToList(), newRefreshToken));
        }).AllowAnonymous();
    }

    private static string IssueRefreshToken(VmsDbContext db, Guid userId, int expiryDays)
    {
        var raw = RefreshTokenGenerator.GenerateRaw();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = RefreshTokenGenerator.Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiryDays)
        });
        return raw;
    }
}
