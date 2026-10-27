namespace OpenScct.Server.Hub;

/// <summary>
/// The hub's numbers. The hub is an OpenSCCT addition (spec/additions/hub.md): these are our own choices, not values
/// of the original, so they carry no provenance.
/// </summary>
public static class HubK
{
    /// <summary>The hub map's id (tools/hubmap, fixtures/hub).</summary>
    public const string MapName = "Hub";

    /// <summary>Players one hub server takes by default.</summary>
    public const int DefaultMaxPlayers = 50;

    /// <summary>
    /// The SBase match the hub runs under its own rules: Story without a time limit and with lives that never run out
    /// (SHubGameInfo never ends the match and never counts a death).
    /// </summary>
    public const string GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=99?TimesRespawnsATT=99?LivesDEF=99";

    /// <summary>A player whose pawn is gone (or fell out of the world) is spawned again after this long.</summary>
    public const float RespawnSeconds = 3f;

    /// <summary>
    /// Longest wait (s) for a player's client to let go of the pawn a side change or respawn took away before the new
    /// pawn is spawned anyway.
    /// </summary>
    public const float PawnReleaseTimeoutSeconds = 3f;

    /// <summary>A pawn below this height (uu) has fallen out of the hall and is put back at the plaza.</summary>
    public const float KillZ = -2048f;

    /// <summary>A player may change side or respawn at most this often.</summary>
    public const float SideChangeCooldownSeconds = 2f;

    /// <summary>A player may swap his kit at most this often (s); the swap goes on at once (spec/additions/gear-change.md).</summary>
    public const float GearChangeCooldownSeconds = 10f;

    /// <summary>Lives a hub player is given (only so the SBase rules see a living player).</summary>
    public const int Lives = 99;

    /// <summary>Longest player name (as the SBase login keeps it).</summary>
    public const int MaxNameLength = 20;

    /// <summary>Name tags are drawn up to this far (uu) from the viewer.</summary>
    public const float NameTagRange = 4500f;

    /// <summary>Height over a pawn's location (its centre) where its name tag sits (uu).</summary>
    public const float NameTagHeight = 110f;
}
