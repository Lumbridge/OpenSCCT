namespace OpenScct.Core.Engine;

/// <summary>Engine.PlayerController: a controller driven by a human player. Only the core fields so far.</summary>
public partial class PlayerController : Controller
{
    public PlayerController()
    {
        bIsAPlayerController = true;
    }

    public Actor? ViewTarget;
    public GameReplicationInfo? GameReplicationInfo;
    public bool bBehindView;
    public float DesiredFOV;
    public float DefaultFOV;

    // Input axes and buttons, filled by the input system each tick.
    public float aForward;
    public float aStrafe;
    public float aUp;
    public float aTurn;
    public float aLookUp;
    public float aMouseX;
    public float aMouseY;
    public float aBaseY;
    public bool bPressedJump;

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        ViewTarget = Live(ViewTarget);
        GameReplicationInfo = Live(GameReplicationInfo);
    }
}
