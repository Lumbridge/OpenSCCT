using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>A weapon clip the merc's first-person weapon plays with a movement step (equipment spec).</summary>
public enum MovementWeaponClip
{
    Jump,
    Fall,
    Land,
    LandHigh,
}

/// <summary>
/// Calls from the movement port into units other areas own: the camera (modes and smoothing), the weapons and
/// gadgets (draw, holster, laser, the merc weapon's movement clips), the combat and interaction searches the Use
/// key starts, the death animation and footprints. Install an implementation with <see cref="Level.SetService{T}"/>;
/// without one, <see cref="MovementHooksLocal"/> applies the plain local behaviour described on each member.
/// </summary>
public interface IMovementHooks
{
    /// <summary>SPlayerCam.SetMode on the controller's camera.</summary>
    void SetCameraMode(SPlayerController PC, ECamMode Mode);

    /// <summary>SPlayerCam.ActivateSmooth on the controller's camera.</summary>
    void CameraSmooth(SPlayerController PC, float Smooth);

    /// <summary>The merc weapon's movement clip (AnimateJump, AnimateFall, AnimateLand, AnimateLandHigh).</summary>
    void PlayWeaponClip(SPlayerController PC, MovementWeaponClip Clip);

    /// <summary>SBasePawn.SetWeaponLoaded (equipment spec). Local behaviour: WeaponLoaded = num.</summary>
    void SetWeaponLoaded(SBasePawn Pawn, int Num);

    /// <summary>SPlayerController.ServerSwitchLaser (merc laser and spy laser, equipment spec).</summary>
    void SwitchLaser(SPlayerController PC, bool On);

    /// <summary>The Use key searches in walking order: AskToGrabPawn, FindPawnToReanimate, FindPawnToElbowShotAndGoForIt, DoInteraction. True stops the chain.</summary>
    bool UseSearch(SPlayerController PC, int Step, bool bCantRollCuzColl);

    /// <summary>SPlayerController.DiscardPickup.</summary>
    void DiscardPickup(SPlayerController PC);

    /// <summary>SPlayerController.PlayAnimDeath (combat spec).</summary>
    void PlayAnimDeath(SPlayerController PC);

    /// <summary>SBasePawn.FootPrint (view and sound).</summary>
    void FootPrint(SBasePawn Pawn, bool bTrueIfLeftFoot);

    /// <summary>A pending quick gadget is launched (SA_QUICK_THROW and the gadget spec).</summary>
    void LaunchQuickGadget(SPlayerController PC);

    /// <summary>SPlayerController.performDropAttack of a falling player (drop attack spec).</summary>
    void PerformDropAttack(SPlayerController PC, SBasePawn PawnToAttack);

    /// <summary>SPlayerController.TellServerForDropAttack, the client's reliable drop-attack request (drop-attack spec).</summary>
    void TellServerForDropAttack(SPlayerController PC, SBasePawn PawnToAttack);

    /// <summary>
    /// SPlayerCam.AttachCamToBone on the controller's camera (the merc's head camera on a ladder). Local behaviour:
    /// nothing (the camera is presentation).
    /// </summary>
    void AttachCameraToBone(SPlayerController PC, bool YesNo, Name BoneName, FVector Offset)
    {
    }
}

/// <summary>
/// <see cref="IMovementHooks"/> until the owning areas install theirs: camera and presentation calls do nothing, the
/// Use searches find nothing, SetWeaponLoaded only writes the field.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Local stand-in for units of other areas")]
public sealed class MovementHooksLocal : IMovementHooks
{
    public static readonly MovementHooksLocal Instance = new();

    private MovementHooksLocal()
    {
    }

    public void SetCameraMode(SPlayerController PC, ECamMode Mode)
    {
    }

    public void CameraSmooth(SPlayerController PC, float Smooth)
    {
    }

    public void PlayWeaponClip(SPlayerController PC, MovementWeaponClip Clip)
    {
    }

    public void SetWeaponLoaded(SBasePawn Pawn, int Num) => Pawn.WeaponLoaded = (byte)Num;

    public void SwitchLaser(SPlayerController PC, bool On)
    {
    }

    public bool UseSearch(SPlayerController PC, int Step, bool bCantRollCuzColl) => false;

    public void DiscardPickup(SPlayerController PC)
    {
    }

    public void PlayAnimDeath(SPlayerController PC)
    {
    }

    public void FootPrint(SBasePawn Pawn, bool bTrueIfLeftFoot)
    {
    }

    public void LaunchQuickGadget(SPlayerController PC)
    {
    }

    public void PerformDropAttack(SPlayerController PC, SBasePawn PawnToAttack)
    {
    }

    public void TellServerForDropAttack(SPlayerController PC, SBasePawn PawnToAttack)
    {
    }
}

/// <summary>
/// The movement layer of a level: when installed (<see cref="Level.SetService{T}"/>), every player controller with a
/// local viewport runs PlayerTick in its tick and every SBasePawn advances its animation channels. Hosts that run
/// gameplay install it; without it controllers and pawns do neither (other areas' tests drive them by hand).
/// </summary>
public sealed class MovementService
{
    public MovementService(IPawnAnimationSource? animations = null, IMovementHooks? hooks = null)
    {
        Animations = animations ?? PawnAnimationSource.Default;
        Hooks = hooks ?? MovementHooksLocal.Instance;
    }

    /// <summary>Animation sequences of the pawns.</summary>
    public IPawnAnimationSource Animations { get; }

    /// <summary>Calls into other areas.</summary>
    public IMovementHooks Hooks { get; set; }

    /// <summary>The service of an actor's level, or null.</summary>
    public static MovementService? Of(Actor actor) => actor.XLevel?.GetService<MovementService>();

    /// <summary>The hooks of an actor's level, or the local stand-in.</summary>
    public static IMovementHooks HooksOf(Actor actor) => Of(actor)?.Hooks ?? MovementHooksLocal.Instance;

    private readonly System.Collections.Generic.Dictionary<Name, PawnClip> _standIns = new();

    /// <summary>
    /// The short stand-in for a sequence the animation source lacks (made once per level and name), so a missing
    /// clip still ends and raises AnimEnd.
    /// </summary>
    public PawnClip StandIn(Name Sequence, int Frames, float Fps)
    {
        if (!_standIns.TryGetValue(Sequence, out PawnClip? clip))
        {
            clip = new PawnClip(Sequence, Frames, Fps);
            _standIns[Sequence] = clip;
        }

        return clip;
    }
}
