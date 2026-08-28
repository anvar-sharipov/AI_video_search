using Microsoft.EntityFrameworkCore;
using VMS.Core.Domain;

namespace VMS.Core.Data;

public class VmsDbContext(DbContextOptions<VmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100);
        });

        modelBuilder.Entity<Camera>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(64);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.IpAddress).HasMaxLength(64);
        });

        modelBuilder.Entity<RetentionPolicy>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.UserId);
            e.Property(a => a.Username).HasMaxLength(100);
            e.Property(a => a.Action).HasMaxLength(200);
        });
    }
}
