using Microsoft.EntityFrameworkCore;
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
            TokenService tokenService) =>
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
            await db.SaveChangesAsync();

            var (token, expiresAt) = tokenService.IssueToken(user);

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = user.Id,
                Username = user.Username,
                Action = "Login",
                IsSuccess = true
            });

            return Results.Ok(new LoginResponse(token, user.Username, user.Role.ToString(), expiresAt));
        }).AllowAnonymous();
    }
}
