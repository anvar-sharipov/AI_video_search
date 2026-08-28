using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Startup;

/// <summary>
/// Applies pending EF Core migrations and, on a brand-new database, seeds:
///  - a bootstrap SuperAdmin so the system is reachable on first run without manual SQL
///    (the generated password is logged once — change it immediately after first login)
///  - a default 30-day retention policy
///  - a Camera row from Vms:TestCamera, if configured and the Cameras table is empty
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VmsDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var vmsOptions = scope.ServiceProvider.GetRequiredService<IOptions<VmsOptions>>().Value;

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var bootstrapPassword = Guid.NewGuid().ToString("N")[..12];
            db.Users.Add(new User
            {
                Username = "superadmin",
                PasswordHash = auth.HashPassword(bootstrapPassword),
                Role = UserRole.SuperAdmin
            });
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Seeded bootstrap SuperAdmin user 'superadmin' with one-time password: {Password} — change it after first login.",
                bootstrapPassword);
        }

        var defaultPolicy = await db.RetentionPolicies.FirstOrDefaultAsync(p => p.IsDefault);
        if (defaultPolicy is null)
        {
            defaultPolicy = new RetentionPolicy { Name = "Default", RetentionDays = 30, IsDefault = true };
            db.RetentionPolicies.Add(defaultPolicy);
            await db.SaveChangesAsync();
        }

        if (vmsOptions.TestCamera is not null && !await db.Cameras.AnyAsync())
        {
            var testCamera = vmsOptions.TestCamera;
            db.Cameras.Add(new Camera
            {
                Code = testCamera.CameraId,
                Name = testCamera.CameraId,
                IpAddress = testCamera.IpAddress,
                OnvifPort = testCamera.OnvifPort,
                OnvifUsername = testCamera.Username,
                OnvifPasswordPlaintext = testCamera.Password,
                RetentionPolicyId = defaultPolicy.Id,
                IsEnabled = true
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded camera '{Code}' from Vms:TestCamera config.", testCamera.CameraId);
        }
    }
}
