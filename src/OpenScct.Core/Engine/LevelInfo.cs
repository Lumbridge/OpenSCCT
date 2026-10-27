namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.LevelInfo: level-wide settings and state. Every level has exactly one, created by the runtime
/// <see cref="OpenScct.Core.Level"/>; actors reach it as <c>Level</c>. Only the core fields so far.
/// </summary>
public partial class LevelInfo : ZoneInfo
{
    public LevelInfo()
    {
        bStatic = true;
        bNoDelete = true;
    }

    /// <summary>Seconds of game time since the level began, accumulated in float each tick.</summary>
    public float TimeSeconds;

    public float TimeDilation = 1f;
    public ENetMode NetMode;
    public GameInfo? Game;
    public GameReplicationInfo? GRI;
    public PlayerController? LocalPlayerController;

    /// <summary>Head of the controller list (Controller.nextController links).</summary>
    public Controller? ControllerList { get; internal set; }

    public string Title = string.Empty;
    public string Author = string.Empty;

    /// <summary>Bits that enable traversal (GE) records by id; toggled by SGETrigger.</summary>
    public byte GEActivationMask;

    /// <summary>Set once the begin-play passes of the level load have run.</summary>
    public bool bBegunPlay;

    public bool bPlayersOnly;

    /// <summary>Default gravity Z. The SCCT class default is not decoded yet; map data or the defaults pack sets it.</summary>
    public float DefaultGravity;

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Game = Live(Game);
        GRI = Live(GRI);
        LocalPlayerController = Live(LocalPlayerController);
    }
}
