using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace VMS.MediaEngine.Onvif;

/// <summary>
/// ONVIF WS-Discovery — finds devices on the local network via an unauthenticated UDP
/// multicast Probe, before any credentials are known. Independent of OnvifClient/
/// WsSecurityHeaderBuilder: WS-Discovery Probe/ProbeMatch is not WS-Security-authenticated
/// by spec — that's the point, it's how a device is found in order to then try credentials
/// against it. Only works within the same L2/broadcast domain as the server (standard
/// multicast limitation — it can't discover devices across routed subnets).
/// </summary>
public class OnvifDiscoveryClient
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int DiscoveryPort = 3702;

    /// <summary>
    /// Sends the probe out every active IPv4 network interface, not just the OS's default
    /// route — a multi-homed host (VPN adapters, WSL's virtual switch, etc. alongside the
    /// real LAN NIC) would otherwise send multicast out whichever interface the routing table
    /// picks as default, which is frequently not the LAN the cameras are actually on.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredOnvifDevice>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);
        var localAddresses = GetCandidateLocalAddresses();

        var found = new Dictionary<string, DiscoveredOnvifDevice>();
        var probeTasks = localAddresses.Select(addr => ProbeFromInterfaceAsync(addr, effectiveTimeout, ct)).ToList();
        var results = await Task.WhenAll(probeTasks);

        foreach (var device in results.SelectMany(r => r))
        {
            found[$"{device.IpAddress}:{device.OnvifPort}"] = device;
        }

        return found.Values.ToList();
    }

    private static List<IPAddress> GetCandidateLocalAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && nic.Supports(NetworkInterfaceComponent.IPv4))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
    }

    private static async Task<List<DiscoveredOnvifDevice>> ProbeFromInterfaceAsync(IPAddress localAddress, TimeSpan timeout, CancellationToken ct)
    {
        var devices = new List<DiscoveredOnvifDevice>();
        try
        {
            using var client = new UdpClient(new IPEndPoint(localAddress, 0));
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());
            client.JoinMulticastGroup(MulticastAddress, localAddress);

            var probeBytes = Encoding.UTF8.GetBytes(BuildProbeMessage());
            await client.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(MulticastAddress, DiscoveryPort));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                while (true)
                {
                    var result = await client.ReceiveAsync(cts.Token);
                    var device = ParseProbeMatch(Encoding.UTF8.GetString(result.Buffer));
                    if (device is not null)
                    {
                        devices.Add(device);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected — this interface's discovery window closed.
            }
        }
        catch (SocketException)
        {
            // This interface can't do multicast (e.g. a point-to-point VPN adapter) — skip it,
            // other interfaces are probed independently.
        }

        return devices;
    }

    private static string BuildProbeMessage() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                    xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                    xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                    xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID>
            <w:To e:mustUnderstand="1">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
            <w:Action e:mustUnderstand="1">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
          </e:Header>
          <e:Body>
            <d:Probe>
              <d:Types>dn:NetworkVideoTransmitter</d:Types>
            </d:Probe>
          </e:Body>
        </e:Envelope>
        """;

    private static DiscoveredOnvifDevice? ParseProbeMatch(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var xAddrs = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "XAddrs")?.Value;
            var firstAddr = xAddrs?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstAddr is null || !Uri.TryCreate(firstAddr, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var scopes = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Scopes")?.Value ?? string.Empty;
            var hardwareScope = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(s => s.Contains("/hardware/", StringComparison.OrdinalIgnoreCase));
            var model = hardwareScope?.Split("/hardware/").LastOrDefault();

            return new DiscoveredOnvifDevice(
                IpAddress: uri.Host,
                OnvifPort: uri.Port,
                Model: model is null ? null : Uri.UnescapeDataString(model));
        }
        catch (Exception)
        {
            // Malformed or unrelated multicast traffic sharing the port — ignore, not fatal.
            return null;
        }
    }
}
