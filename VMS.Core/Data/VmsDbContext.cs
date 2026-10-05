using Microsoft.EntityFrameworkCore;
using VMS.Core.Domain;

namespace VMS.Core.Data;

public class VmsDbContext(DbContextOptions<VmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<KnownPerson> KnownPersons => Set<KnownPerson>();
    public DbSet<EMap> EMaps => Set<EMap>();
    public DbSet<EMapPin> EMapPins => Set<EMapPin>();
    public DbSet<CameraCountingLine> CameraCountingLines => Set<CameraCountingLine>();
    public DbSet<AlarmAcknowledgement> AlarmAcknowledgements => Set<AlarmAcknowledgement>();
    public DbSet<CameraGroup> CameraGroups => Set<CameraGroup>();
    public DbSet<CameraGroupMember> CameraGroupMembers => Set<CameraGroupMember>();
    public DbSet<VideoWallLayout> VideoWallLayouts => Set<VideoWallLayout>();
    public DbSet<VideoWallCell> VideoWallCells => Set<VideoWallCell>();

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

        modelBuilder.Entity<KnownPerson>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<EMap>(e =>
        {
            e.Property(m => m.Name).HasMaxLength(200);
            e.Property(m => m.ImageFileName).HasMaxLength(260);
        });

        modelBuilder.Entity<EMapPin>(e =>
        {
            e.HasIndex(p => p.EMapId);
        });

        modelBuilder.Entity<CameraGroup>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<CameraGroupMember>(e =>
        {
            e.HasIndex(m => m.CameraGroupId);
        });

        modelBuilder.Entity<VideoWallLayout>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<VideoWallCell>(e =>
        {
            e.HasIndex(c => c.VideoWallLayoutId);
            e.HasOne<VideoWallLayout>()
                .WithMany()
                .HasForeignKey(c => c.VideoWallLayoutId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Camera>()
                .WithMany()
                .HasForeignKey(c => c.CameraId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CameraCountingLine>(e =>
        {
            e.HasIndex(l => l.CameraCode).IsUnique();
            e.Property(l => l.CameraCode).HasMaxLength(64);
        });

        modelBuilder.Entity<AlarmAcknowledgement>(e =>
        {
            e.HasIndex(a => a.DetectionEventId).IsUnique();
        });
    }
}
