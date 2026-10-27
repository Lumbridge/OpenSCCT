using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Online;

/// <summary>
/// UPnP port mapping for a listen host (rl:net.upnp, spec/native/reloaded-features-from-code.md rule 14): finds the
/// router, maps the game port (UDP, same port outside and inside, a 24 h lease) and says how it went, naming the port
/// to forward by hand when it did not. Runs on a worker thread (discovery waits up to two seconds). Only the game
/// client maps ports; a dedicated server on a VPS has none to map.
/// </summary>
[PortOf("rl:net.upnp", Spec = "spec/native/reloaded-features-from-code.md#3-upnp")]
public sealed class UpnpPorts
{
    /// <summary>Lease of a mapping, in seconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100bd5f0: UPNP_AddPortMapping lease 86400")]
    public const int LeaseSeconds = 86400;

    private Upnp? _upnp;
    private int _mapped;

    /// <summary>The router's public address once the mapping worked, else null.</summary>
    public string? ExternalAddress { get; private set; }

    /// <summary>Maps <paramref name="port"/>; the task's result is the line for the lobby.</summary>
    public Task<string> MapAsync(int port) => Task.Run(() => Map(port));

    private string Map(int port)
    {
        try
        {
            var upnp = new Upnp();
            int discover = upnp.Discover(2000, 2, "InternetGatewayDevice");
            if (discover != (int)Upnp.UpnpResult.Success || upnp.GetGateway() is not { } gateway || !gateway.IsValidGateway())
            {
                return Manual(port, "no UPnP router answered");
            }

            // A mapping left by an earlier session goes first, as Reloaded does.
            upnp.DeletePortMapping(port, "UDP");
            int result = upnp.AddPortMapping(port, port, "OpenSCCT game", "UDP", LeaseSeconds);
            if (result != (int)Upnp.UpnpResult.Success)
            {
                return Manual(port, "the router refused the mapping (" + ((Upnp.UpnpResult)result).ToString() + ")");
            }

            _upnp = upnp;
            _mapped = port;
            ExternalAddress = upnp.QueryExternalAddress();
            return string.Create(CultureInfo.InvariantCulture, $"UPnP: UDP port {port} is forwarded on the router ({ExternalAddress}).");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            return Manual(port, e.Message);
        }
    }

    private static string Manual(int port, string why) =>
        string.Create(CultureInfo.InvariantCulture,
            $"UPnP: {why}. Forward UDP port {port} on your router by hand, or players will join through the master's relay.");

    /// <summary>Removes the mapping this host made.</summary>
    public void Remove()
    {
        if (_upnp is { } upnp && _mapped > 0)
        {
            try
            {
                upnp.DeletePortMapping(_mapped, "UDP");
            }
            catch (InvalidOperationException)
            {
            }

            _upnp = null;
            _mapped = 0;
        }
    }
}
