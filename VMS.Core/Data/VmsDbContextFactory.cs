using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VMS.Core.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works directly against this
/// class library without needing the Backend.Server host. The connection string here
/// is only used by the EF tooling; the running app supplies its own via DI.
/// </summary>
public class VmsDbContextFactory : IDesignTimeDbContextFactory<VmsDbContext>
{
    public VmsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VmsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=vms;Username=vms;Password=vms_dev_password");
        return new VmsDbContext(optionsBuilder.Options);
    }
}
