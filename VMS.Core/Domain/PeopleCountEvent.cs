namespace VMS.Core.Domain;

/// <summary>
/// One person crossing a camera's configured counting line (see CameraCountingLine). Indexed
/// into Elasticsearch like ObjectMetadataEvent (time-series data queried by date range), not
/// Postgres — People Counting's "count people that entered/left during a certain period" is a
/// time-range aggregation query, exactly what ObjectMetadataEvent's own search already does.
/// </summary>
public class PeopleCountEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CameraId { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required bool IsIn { get; set; }
}
