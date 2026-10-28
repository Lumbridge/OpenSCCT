using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// One call that leaves this machine, or a UI request, made by the game rules: named after the original function
/// (<c>ClientSetHUD</c>, <c>ClientPlaySound</c>, <c>ReceiveLocalizedMessage</c>, <c>ClientTravel</c>,
/// <c>PCChangeServerState</c>, <c>OpenMenu</c>...). Only the arguments the call uses are set.
/// </summary>
public readonly record struct GameCall(Name Function)
{
    public int Int { get; init; }

    public bool Bool { get; init; }

    public string? Text { get; init; }

    public ClassRef Class { get; init; }

    public ClassRef Class2 { get; init; }

    public AssetRef Asset { get; init; }

    public PlayerReplicationInfo? Pri1 { get; init; }

    public PlayerReplicationInfo? Pri2 { get; init; }

    public UObject? Object { get; init; }

    public Actor? Actor { get; init; }

    public FRotator Rotation { get; init; }

    public FVector Vector { get; init; }

    public Name Name1 { get; init; }

    public Name Name2 { get; init; }

    public float Float1 { get; init; }

    public float Float2 { get; init; }

    public override string ToString() => Function.ToString();
}

/// <summary>
/// What the game rules need from the machine they run on: the calls that go to a player's own machine (HUD, sounds,
/// music, messages, menus, travel, and the state calls of remote players), and the natives and front-end facts that
/// are not part of the simulation (GUI state, pending connections, ping, the current map URL).
/// <para>
/// For a remote player the network layer delivers <see cref="ClientCall"/>; for the local player the view layer
/// shows the cosmetic ones, and the simulation runs the state calls itself without asking the host. Install one
/// with <c>Level.SetService&lt;IGameHost&gt;(host)</c>; without it the level uses <see cref="NullGameHost"/>.
/// </para>
/// </summary>
public interface IGameHost
{
    /// <summary>A call for <paramref name="target"/>'s own machine (server to owning client), or a UI request.</summary>
    void ClientCall(PlayerController target, in GameCall call);

    /// <summary>A call from <paramref name="sender"/>'s own machine to the server (client to server).</summary>
    void ServerCall(PlayerController sender, in GameCall call);

    /// <summary>
    /// <c>PlayerController(Level.ControllerList).Player.GUIController.bInGame</c>: true once the front end loaded the
    /// map for a match (menus and network launches), false in the editor's Play Level.
    /// </summary>
    bool IsGUIInGame(PlayerController? player);

    /// <summary>SPlayerController.NbPendingClientOnServer (native 1665): clients still connecting.</summary>
    int NbPendingClientOnServer();

    /// <summary>Actor.GetURLMap (native 547): the map part of the current URL, for example "Lobby".</summary>
    string GetURLMap();

    /// <summary>Object.IsPsx2: true only on the console build.</summary>
    bool IsPsx2();

    /// <summary>Actor.IsPBClientEnabled (native 1699).</summary>
    bool IsPBClientEnabled();

    /// <summary>Actor.OnScriptEvent (native 1798): a notification to the hosting application (the editor).</summary>
    void OnScriptEvent(Actor source, int id);

    /// <summary>Controller.GetPing: the connection's ping in milliseconds.</summary>
    int GetPing(PlayerController player);

    /// <summary>Actor.NetType (native 1069): how this session was set up (none, LAN, game service).</summary>
    NetworkType NetType();

    /// <summary>
    /// SPlayerController.IsDedicatedServerMode: the non-playing host's spectator UI is active. The native shares
    /// its stub with IsPsx2 (false) in this build.
    /// </summary>
    bool IsDedicatedServerMode();
}

/// <summary>
/// The host used when nothing else is attached: calls go nowhere, there is no GUI (as in the editor's Play Level),
/// no client is pending, the map URL is empty and ping is 0.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Stand-in for the front end and natives until the client and net layers attach theirs")]
public sealed class NullGameHost : IGameHost
{
    public static readonly NullGameHost Instance = new();

    private NullGameHost()
    {
    }

    public void ClientCall(PlayerController target, in GameCall call)
    {
    }

    public void ServerCall(PlayerController sender, in GameCall call)
    {
    }

    public bool IsGUIInGame(PlayerController? player) => false;

    public int NbPendingClientOnServer() => 0;

    public string GetURLMap() => string.Empty;

    public bool IsPsx2() => false;

    public bool IsPBClientEnabled() => false;

    public void OnScriptEvent(Actor source, int id)
    {
    }

    public int GetPing(PlayerController player) => 0;

    public NetworkType NetType() => NetworkType.NET_NONE;

    public bool IsDedicatedServerMode() => false;
}

/// <summary>The local viewport of a player on this machine (UE2 UViewport), as set by <see cref="GameInfo.SpawnPlayActor"/>.</summary>
public sealed class SimViewport : UViewport
{
}

/// <summary>The network connection of a remote player (UE2 UNetConnection), as set by <see cref="GameInfo.SpawnPlayActor"/>.</summary>
public sealed class SimNetConnection : UNetConnection
{
}
