namespace VMS.Core.Domain;

/// <summary>A named, ordered subset of cameras — lets an operator group cameras into logical
/// "screens" (e.g. cameras 1-5 on one view, 6-20 on another) for the Live View grid, distinct
/// from the flat camera list itself. See CameraGroupMember for membership/ordering.</summary>
public class CameraGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One camera's membership in a CameraGroup, with SortOrder controlling the order cameras appear when that group is applied to the Live View grid.</summary>
public class CameraGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CameraGroupId { get; set; }
    public required Guid CameraId { get; set; }
    public int SortOrder { get; set; }
}
