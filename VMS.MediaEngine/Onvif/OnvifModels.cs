namespace VMS.MediaEngine.Onvif;

public record OnvifProfile(string Token, string Name, int? Width, int? Height);

public record CameraConnectionInfo(
    string MainStreamRtspUri,
    string SubStreamRtspUri,
    string MainProfileToken,
    string SubProfileToken);
