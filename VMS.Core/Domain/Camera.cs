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

    /// <summary>
    /// Which ONVIF VideoSourceConfiguration/SourceToken this row maps to — null for a plain
    /// single-channel IP camera. Set when a camera is added from a multi-channel NVR/DVR's
    /// discovered channel list, so re-onboarding (edit, server restart) keeps reconnecting to
    /// the same physical channel instead of whichever one GetChannelsAsync happens to list first.
    /// </summary>
    public string? OnvifVideoSourceToken { get; set; }

    // Encrypted at rest via ASP.NET Core Data Protection (see Backend.Server's
    // ICameraCredentialProtector) — Core itself stays web-framework-agnostic, so this is just an
    // opaque ciphertext blob as far as this type is concerned, same as OnvifUsername is a plain
    // string regardless of who happens to encrypt it.
    public string? OnvifPasswordProtected { get; set; }

    public bool IsEnabled { get; set; } = true;
    public Guid? RetentionPolicyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
