namespace VMS.MediaEngine.Onvif;

/// <summary>
/// Onboards a camera by IP: resolves the media service, reads its profiles, and
/// fetches RTSP URIs for the highest-resolution profile (main/archive stream) and
/// lowest-resolution profile (sub/grid stream). Falls back to using the single
/// profile for both if the camera only exposes one.
/// </summary>
public class OnvifCameraDiscoveryService(IHttpClientFactory httpClientFactory)
{
    public async Task<CameraConnectionInfo> ConnectAsync(
        string ipAddress,
        string username,
        string password,
        int onvifPort = 80,
        CancellationToken ct = default)
    {
        var deviceServiceUri = $"http://{ipAddress}:{onvifPort}/onvif/device_service";
        var client = new OnvifClient(httpClientFactory.CreateClient(nameof(OnvifClient)), deviceServiceUri, username, password);

        var mediaServiceUri = await client.GetMediaServiceUriAsync(ct);
        var profiles = await client.GetProfilesAsync(mediaServiceUri, ct);

        if (profiles.Count == 0)
        {
            throw new InvalidOperationException($"Camera at {ipAddress} returned no media profiles.");
        }

        var ordered = profiles.OrderByDescending(p => (p.Width ?? 0) * (p.Height ?? 0)).ToList();
        var mainProfile = ordered.First();
        var subProfile = ordered.Last();

        var mainUri = await client.GetStreamUriAsync(mediaServiceUri, mainProfile.Token, ct);
        var subUri = mainProfile.Token == subProfile.Token
            ? mainUri
            : await client.GetStreamUriAsync(mediaServiceUri, subProfile.Token, ct);

        return new CameraConnectionInfo(
            MainStreamRtspUri: InjectCredentials(mainUri, username, password),
            SubStreamRtspUri: InjectCredentials(subUri, username, password),
            MainProfileToken: mainProfile.Token,
            SubProfileToken: subProfile.Token);
    }

    /// <summary>
    /// ONVIF's GetStreamUri response is not required to embed credentials, and most
    /// cameras don't. ffmpeg needs them in the URI, so we inject rtsp://user:pass@host/...
    /// </summary>
    private static string InjectCredentials(string rtspUri, string username, string password)
    {
        var uri = new Uri(rtspUri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return rtspUri;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = Uri.EscapeDataString(username),
            Password = Uri.EscapeDataString(password)
        };
        return builder.Uri.ToString();
    }
}
