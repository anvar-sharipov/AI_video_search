using System.Net;
using System.Text;
using System.Xml.Linq;

namespace VMS.MediaEngine.Onvif;

public class OnvifRequestException : Exception
{
    public OnvifRequestException(string serviceUri, HttpStatusCode statusCode, string responseBody)
        : base(BuildMessage(serviceUri, statusCode, responseBody))
    {
    }

    /// <summary>
    /// A SOAP fault's body is mostly namespace boilerplate — the actual human-readable reason
    /// lives in one small &lt;Reason&gt;&lt;Text&gt; element. Surface just that when present (this is
    /// what onboarding failures/API error responses show to the user), falling back to the raw
    /// body only when it isn't a recognizable SOAP fault at all.
    /// </summary>
    private static string BuildMessage(string serviceUri, HttpStatusCode statusCode, string responseBody)
    {
        var reason = TryExtractFaultReason(responseBody);
        return reason is not null
            ? $"ONVIF request to '{serviceUri}' failed: {reason}"
            : $"ONVIF request to '{serviceUri}' failed with {statusCode}: {responseBody}";
    }

    private static string? TryExtractFaultReason(string responseBody)
    {
        try
        {
            return XDocument.Parse(responseBody).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Text")?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Minimal ONVIF SOAP client covering exactly what camera onboarding needs:
/// capabilities -> media service address -> profiles -> RTSP stream URI.
/// Hand-rolled instead of a NuGet ONVIF wrapper because most all-in-one ONVIF
/// packages are unmaintained; this is the standard approach for .NET ONVIF integration.
/// </summary>
public class OnvifClient(HttpClient httpClient, string deviceServiceUri, string username, string password)
{
    public async Task<string> GetMediaServiceUriAsync(CancellationToken ct = default)
    {
        const string body = """
            <GetCapabilities xmlns="http://www.onvif.org/ver10/device/wsdl">
              <Category>Media</Category>
            </GetCapabilities>
            """;

        var doc = await SendSoapAsync(deviceServiceUri, body, includeAuth: false, ct);

        var mediaXAddr = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Media")?
            .Elements().FirstOrDefault(e => e.Name.LocalName == "XAddr")?.Value;

        return mediaXAddr ?? throw new InvalidOperationException(
            "Media service XAddr not found in GetCapabilities response.");
    }

    public async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(string mediaServiceUri, CancellationToken ct = default)
    {
        const string body = """<GetProfiles xmlns="http://www.onvif.org/ver10/media/wsdl"/>""";

        var doc = await SendSoapAsync(mediaServiceUri, body, includeAuth: true, ct);

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "Profiles")
            .Select(p =>
            {
                var resolution = p.Descendants().FirstOrDefault(e => e.Name.LocalName == "Resolution");
                var width = ParseInt(resolution?.Elements().FirstOrDefault(e => e.Name.LocalName == "Width")?.Value);
                var height = ParseInt(resolution?.Elements().FirstOrDefault(e => e.Name.LocalName == "Height")?.Value);
                var videoSourceToken = p.Descendants().FirstOrDefault(e => e.Name.LocalName == "VideoSourceConfiguration")?
                    .Elements().FirstOrDefault(e => e.Name.LocalName == "SourceToken")?.Value ?? string.Empty;

                return new OnvifProfile(
                    Token: p.Attribute("token")?.Value ?? string.Empty,
                    Name: p.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? string.Empty,
                    Width: width,
                    Height: height,
                    VideoSourceToken: videoSourceToken);
            })
            .ToList();
    }

    public async Task<string> GetStreamUriAsync(string mediaServiceUri, string profileToken, CancellationToken ct = default)
    {
        var body = $"""
            <GetStreamUri xmlns="http://www.onvif.org/ver10/media/wsdl">
              <StreamSetup>
                <Stream xmlns="http://www.onvif.org/ver10/schema">RTP-Unicast</Stream>
                <Transport xmlns="http://www.onvif.org/ver10/schema">
                  <Protocol>RTSP</Protocol>
                </Transport>
              </StreamSetup>
              <ProfileToken>{profileToken}</ProfileToken>
            </GetStreamUri>
            """;

        var doc = await SendSoapAsync(mediaServiceUri, body, includeAuth: true, ct);

        var uri = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value;

        return uri ?? throw new InvalidOperationException("Stream URI not found in GetStreamUri response.");
    }

    private async Task<XDocument> SendSoapAsync(string serviceUri, string bodyXml, bool includeAuth, CancellationToken ct)
    {
        var header = includeAuth ? WsSecurityHeaderBuilder.Build(username, password) : string.Empty;

        var envelope = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope">
              <s:Header>{header}</s:Header>
              <s:Body>{bodyXml}</s:Body>
            </s:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, serviceUri)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml")
        };

        using var response = await httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new OnvifRequestException(serviceUri, response.StatusCode, responseBody);
        }

        return XDocument.Parse(responseBody);
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var result) ? result : null;
}
