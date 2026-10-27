using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;

namespace OpenScctGame.Smoke;

/// <summary>
/// Counts the move protocol's corrections that reach the local player's client: the server's
/// <c>MyShortClientAdjustPosition</c>, <c>MyClientAdjustPosition</c> and <c>ClientReinitPawnBecauseOfDivergences</c>
/// calls, by wrapping their receive handlers on the client driver (the wrapped handler still runs first), and the
/// corrections the client applied (<see cref="ClientMoves.CorrectionsApplied"/>).
/// </summary>
public sealed class InspectNet
{
    private static readonly string[] Watched = ["MyShortClientAdjustPosition", "MyClientAdjustPosition", "ClientReinitPawnBecauseOfDivergences"];

    private readonly ClientSession _client;
    private readonly Dictionary<string, long> _received = new(StringComparer.Ordinal);
    private bool _installed;

    public InspectNet(ClientSession client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Called on every watched call received, with the function name.</summary>
    public Action<string>? Received { get; set; }

    public long ShortCorrections => Count("MyShortClientAdjustPosition");

    public long Corrections => Count("MyClientAdjustPosition");

    public long Reinits => Count("ClientReinitPawnBecauseOfDivergences");

    /// <summary>Corrections the client applied (ClientUpdatePosition ran without cancelling).</summary>
    public long Applied(Actor? controller) =>
        controller is not null && _client.Movement.EndpointOf(controller)?.Client is { } moves ? moves.CorrectionsApplied : 0;

    /// <summary>True once the handlers are wrapped (needs the local controller to find the functions).</summary>
    public bool Installed => _installed;

    /// <summary>Wraps the handlers once <paramref name="controller"/> (the client's local player controller) exists.</summary>
    public bool TryInstall(Actor? controller)
    {
        if (_installed || controller is null)
        {
            return _installed;
        }

        NetDriver driver = _client.Driver;
        if (driver.Classes.Find(controller.GetType()) is not { } cls)
        {
            return false;
        }

        foreach (string name in Watched)
        {
            if (cls.Layout.FindFunction(name) is not { } function)
            {
                continue;
            }

            NetFunctionHandler? inner = driver.Functions.Find(controller, function);
            string watched = name;
            driver.Functions.Register(NetMoveTransport.ControllerClass, name, (self, f, args, from) =>
            {
                inner?.Invoke(self, f, args, from);
                _received[watched] = Count(watched) + 1;
                Received?.Invoke(watched);
            });
        }

        _installed = true;
        return true;
    }

    private long Count(string name) => _received.TryGetValue(name, out long n) ? n : 0;
}
