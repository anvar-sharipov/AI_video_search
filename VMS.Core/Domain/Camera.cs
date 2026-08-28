namespace VMS.Core.Domain;

public class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable short identifier used everywhere outside Postgres — archive/snapshot
    /// directory names, Elasticsearch's cameraId field, clip filenames. Kept separate
    /// from Id so filesystem paths and cross-module correlation never depend on the
    /// database's surrogate key.
    /// </summary>
    public required string Code { get; set; }

    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public int OnvifPort { get; set; } = 80;

    /// <summary>High-res profile, used for single-camera expanded view and archive recording.</summary>
    public string? RtspMainStreamUri { get; set; }

    /// <summary>Low-res profile, used for grid/multi-view live tiles.</summary>
    public string? RtspSubStreamUri { get; set; }

    public string? OnvifUsername { get; set; }

    // NOTE: plaintext for the 1-camera MVP. Before adding real camera fleets,
    // this must move to DPAPI/secret-store encryption at rest (VMS.Core owns
    // AccessControlManager, so credential encryption belongs here too).
    public string? OnvifPasswordPlaintext { get; set; }

    public bool IsEnabled { get; set; } = true;
    public Guid? RetentionPolicyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
