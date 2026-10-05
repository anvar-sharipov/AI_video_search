namespace VMS.Core.Domain;

/// <summary>A named, saved arrangement of cells for the Video Wall — how many rows/columns and,
/// per VideoWallCell, which rectangle of that grid each cell occupies and which camera (if any)
/// it shows. Distinct from CameraGroup: a group is just a flat set of cameras for the Live View
/// grid, while a layout also fixes each camera's position and cell size on a specific wall.</summary>
public class VideoWallLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One rectangle of cells (Row/Column is its top-left corner, RowSpan/ColumnSpan its
/// size in grid units) within a VideoWallLayout, optionally showing one camera. CameraId is
/// nullable and has no navigation property — if the camera is deleted the cell just goes empty
/// (see VmsDbContext's SetNull configuration) rather than the whole layout disappearing.</summary>
public class VideoWallCell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid VideoWallLayoutId { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public Guid? CameraId { get; set; }
}
