namespace VMS.MediaEngine.Onvif;

/// <summary>
/// Onboards a camera by IP: resolves the media service, reads its profiles, and
/// fetches RTSP URIs for the highest-resolution profile (main/archive stream) and
/// lowest-resolution profile (sub/grid stream). Falls back to using the single
/// profile for both if the camera only exposes one.
/// </summary>
public class OnvifCameraDiscoveryService(IHttpClientFactory httpClientFactory)
{
    /// <summary>
    /// Enumerates every channel the device exposes, grouping profiles by their
    /// VideoSourceConfiguration/SourceToken — a plain IP camera has exactly one group;
    /// an NVR/DVR has one group per physical camera plugged into it. Within each group,
    /// the same highest/lowest-resolution heuristic as before picks main vs. sub stream,
    /// now scoped to that channel instead of mixing profiles across channels.
    /// </summary>
    public async Task<IReadOnlyList<CameraChannelInfo>> GetChannelsAsync(
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

        var channels = new List<CameraChannelInfo>();
        foreach (var group in profiles.GroupBy(p => p.VideoSourceToken))
        {
            var ordered = group.OrderByDescending(p => (p.Width ?? 0) * (p.Height ?? 0)).ToList();
            var mainProfile = ordered.First();
            var subProfile = ordered.Last();

            var mainUri = await client.GetStreamUriAsync(mediaServiceUri, mainProfile.Token, ct);
            var subUri = mainProfile.Token == subProfile.Token
                ? mainUri
                : await client.GetStreamUriAsync(mediaServiceUri, subProfile.Token, ct);

            channels.Add(new CameraChannelInfo(
                VideoSourceToken: group.Key,
                MainStreamRtspUri: InjectCredentials(mainUri, username, password),
                SubStreamRtspUri: InjectCredentials(subUri, username, password),
                MainProfileToken: mainProfile.Token,
                SubProfileToken: subProfile.Token));
        }

        return channels;
    }

    /// <summary>
    /// Connects to a single channel — the one matching <paramref name="channelToken"/> if given
    /// (used when re-onboarding an already-added camera, so it keeps mapping to the same NVR
    /// channel), otherwise the device's first channel (plain single-channel IP cameras only ever
    /// have one, so this preserves the original single-channel behavior).
    /// </summary>
    public async Task<CameraConnectionInfo> ConnectAsync(
        string ipAddress,
        string username,
        string password,
        int onvifPort = 80,
        string? channelToken = null,
        CancellationToken ct = default)
    {
        var channels = await GetChannelsAsync(ipAddress, username, password, onvifPort, ct);
        var channel = channelToken is null
            ? channels[0]
            : channels.FirstOrDefault(c => c.VideoSourceToken == channelToken)
                ?? throw new InvalidOperationException($"Camera at {ipAddress} no longer exposes channel '{channelToken}'.");

        return new CameraConnectionInfo(
            MainStreamRtspUri: channel.MainStreamRtspUri,
            SubStreamRtspUri: channel.SubStreamRtspUri,
            MainProfileToken: channel.MainProfileToken,
            SubProfileToken: channel.SubProfileToken);
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
