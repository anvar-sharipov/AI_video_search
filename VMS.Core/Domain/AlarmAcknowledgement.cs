namespace VMS.Core.Domain;

/// <summary>
/// Marks one detection event (by its Elasticsearch ObjectMetadataEvent.Id) as acknowledged in
/// the Alarm Records screen. Kept separate from the detection event itself (which lives in
/// Elasticsearch, not Postgres) since acknowledgement is operator action/audit state, not
/// AI-detection output — the two have different write patterns and lifetimes.
/// </summary>
public class AlarmAcknowledgement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid DetectionEventId { get; set; }
    public required Guid AcknowledgedByUserId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; } = DateTimeOffset.UtcNow;
}
