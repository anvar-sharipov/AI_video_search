namespace VMS.Core.Domain;

public class RetentionPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int RetentionDays { get; set; } = 30;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
