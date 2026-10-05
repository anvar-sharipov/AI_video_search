namespace VMS.MediaEngine.Onvif;

public record OnvifProfile(string Token, string Name, int? Width, int? Height, string VideoSourceToken);

public record CameraConnectionInfo(
    string MainStreamRtspUri,
    string SubStreamRtspUri,
    string MainProfileToken,
    string SubProfileToken);

/// <summary>One physical channel of a (possibly multi-channel) ONVIF device — an NVR/DVR
/// exposes one of these per camera plugged into it; a plain IP camera exposes exactly one.</summary>
public record CameraChannelInfo(
    string VideoSourceToken,
    string MainStreamRtspUri,
    string SubStreamRtspUri,
    string MainProfileToken,
    string SubProfileToken);

/// <summary>A device found by an unauthenticated WS-Discovery probe — before credentials are known.</summary>
public record DiscoveredOnvifDevice(string IpAddress, int OnvifPort, string? Model);
