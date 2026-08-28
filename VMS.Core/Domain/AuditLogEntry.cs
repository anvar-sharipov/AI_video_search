namespace VMS.Core.Domain;

/// <summary>
/// Append-only. No update/delete path is exposed anywhere in the codebase —
/// see IAuditLogger, which only offers Append/Query.
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UserId { get; set; }
    public required string Username { get; set; }
    public required string Action { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public bool IsSuccess { get; set; } = true;
}
