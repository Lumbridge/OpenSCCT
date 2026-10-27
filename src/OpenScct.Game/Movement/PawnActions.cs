using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// SBasePawn.ExecuteAction (native 1411, 329 cases; spec/native/execute-action.md) for the walking, stance, draw,
/// jump, fall, landing and roll actions: each plays its clips and makes the native changes of that step (physics
/// mode, crouch cylinder, roll flag). The traversal cases are in <see cref="TraversalActions"/>.
/// <para>
/// The clips come from the pawn's sequence fields (AJumpStart, AFall, ALandLow...) or the case's own clip; the
/// tweens are the measured ones (stand 0.25, fall 0.2, low landing 1/12, high landing 0.05), which agree with the
/// decoded AnimateWalking and PlayLanding rules, or 0.1 where no run shows the action. The walk blender sets, the
/// overlay blends, the jump and roll clip choice are OpenScct.Game.Animation.ActionAnimation's (SBasePawn.ExecuteAction
/// runs its Begin and End around every action). Unknown actions do nothing.
/// </para>
/// </summary>
public static class PawnActions
{
    [Provenance(ProvenanceKind.Measured, "goldens: WaitStAlFd0 / WaitStSpFd2 tween 1 / (0.5 x 8) = 0.25 s")]
    public const float StandTween = 0.25f;

    [Provenance(ProvenanceKind.Measured, "goldens movement.spy-jump-in-place, spy-fall-ledge: fallstaldn0 tween 0.2 s")]
    public const float FallTween = 0.2f;

    [Provenance(ProvenanceKind.Measured, "goldens movement.spy-jump-in-place, merc-jump: landStAlLo0 / landStAlLo2 tween 1 / (1.2 x 10) s")]
    [Provenance(ProvenanceKind.Decoded, "PlayLanding 0x111f99d0: the low and quiet landings tween over 0.083333 s")]
    public const float LandLowTween = 0.083333f;

    [Provenance(ProvenanceKind.Measured, "golden movement.spy-fall-ledge: landStAlHi0 tween 1 / (1.0 x 20) s")]
    public const float LandHighTween = 0.05f;

    [Provenance(ProvenanceKind.Estimated, "No run shows the tween of this clip")]
    public const float DefaultTween = 0.1f;

    /// <summary>The draw clips play at twice their rate (cases 79 and 88; the merc's 88 plays none).</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 79, 88: c14 ADrawCrouch2H / ADrawStand2H r2 (spy only for 88), no tween")]
    public const float DrawRate = 2f;

    /// <summary>AnimateWalking's tween and blend time while channel 0 plays a forward jump.</summary>
    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360: 0.1 (0x3dcccccd)")]
    public const float JumpForwardTween = 0.1f;

    /// <summary>AnimateWalking's tween after leaning on a wall (OldBTW).</summary>
    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360: 0.3 (0x3e99999a)")]
    public const float LeaveWallTween = 0.3f;

    /// <summary>The crouch and stand-up clips (SA_WAIT_CR2ST_* and SA_WAIT_ST2CR_*) start at once.</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 81 to 84: c0 AWaitCrOut / AWaitCrIn with tween 0")]
    [Provenance(ProvenanceKind.Measured, "goldens traversal.spy-crawl-vent, spy-roll: WaitCrAlEd0 from frame 0, no tween")]
    public const float StanceTween = 0f;

    [Provenance(ProvenanceKind.Estimated, "A clip the animation source lacks plays as two frames at 15 fps so AnimEnd still comes")]
    private const int MissingClipFrames = 2;

    [Provenance(ProvenanceKind.Estimated, "The stand-in clip's rate")]
    private const float MissingClipFps = 15f;

    /// <summary>Executes <paramref name="action"/> on <paramref name="pawn"/>.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 75 to 110 and 130 to 132 (physics modes, clips, roll flag)")]
    [Provenance(ProvenanceKind.Measured, "Tweens and clip choices checked against the golden runs")]
    public static void Execute(SBasePawn pawn, int action)
    {
        switch (action)
        {
            case SBasePawn.SA_WANTS_TO_CROUCH:
                pawn.Crouch();
                break;
            case SBasePawn.SA_WANTS_TO_UNCROUCH:
                pawn.UnCrouch(false);
                break;

            // ---- resting stances (loops on the base channel) ----
            case SBasePawn.SA_WALK_ST_0H:
                pawn.bIsRolling = false;
                AnimateWalking(pawn, pawn.AWaitSt0);
                break;
            case SBasePawn.SA_WALK_ST_2H:
                pawn.bIsRolling = false;
                pawn.fBlendCoeffWalkAnim = pawn.DefaultObject.fBlendCoeffWalkAnim;
                AnimateWalking(pawn, pawn.AWaitSt2);
                break;
            case SBasePawn.SA_RUN_ST_2H:
                pawn.fBlendCoeffWalkAnim = MovementK.MercRunBlendCoeff;
                break;
            case SBasePawn.SA_WALK_CR_0H:
                pawn.bIsRolling = false;
                AnimateWalking(pawn, pawn.AWaitCr0);
                break;
            case SBasePawn.SA_WALK_CR_2H:
                pawn.bIsRolling = false;
                AnimateWalking(pawn, pawn.AWaitCr2);
                break;
            case SBasePawn.SA_WALK_ST_LAND_OH:
            case SBasePawn.SA_WALK_ST_0H_AFTERFL:
            case SBasePawn.SA_WALK_ST_2H_AFTERFL:
                Walking(pawn);
                break;

            // ---- stance transitions (one-shots on the base channel, no tween: cases 81 to 84) ----
            case SBasePawn.SA_WAIT_ST2CR_0H:
                AnimateWalking(pawn, pawn.AWaitCr0);
                Once(pawn, 0, pawn.AWaitCrIn0, StanceTween);
                break;
            case SBasePawn.SA_WAIT_CR2ST_0H:
                pawn.bIsRolling = false;
                AnimateWalking(pawn, pawn.AWaitSt0);
                Once(pawn, 0, pawn.AWaitCrOut0, StanceTween);
                break;
            case SBasePawn.SA_WAIT_ST2CR_2H:
                AnimateWalking(pawn, pawn.AWaitCr2);
                Once(pawn, 0, pawn.AWaitCrIn2, StanceTween);
                break;
            case SBasePawn.SA_WAIT_CR2ST_2H:
                pawn.bIsRolling = false;
                AnimateWalking(pawn, pawn.AWaitSt2);
                Once(pawn, 0, pawn.AWaitCrOut2, StanceTween);
                break;

            // ---- draw and holster (the draw channel; the weapon is loaded or unloaded at once) ----
            // AnimateWalking(79, 80, 88, 89) puts the new stance's wait loop on the base channel under the draw clip,
            // so the upper body has the armed (or unarmed) pose when the draw channel turns off.
            case SBasePawn.SA_WALK_ST_0Hto2H:
                AnimateWalking(pawn, pawn.AWaitSt2);
                Draw(pawn, pawn is SPAWNDEFENSE ? Name.None : pawn.ADrawStand2H, 2, DrawRate);
                break;
            case SBasePawn.SA_WALK_CR_0Hto2H:
                AnimateWalking(pawn, pawn.AWaitCr2);
                Draw(pawn, pawn.ADrawCrouch2H, 2, DrawRate);
                break;
            case SBasePawn.SA_WALK_ST_2Hto0H:
                AnimateWalking(pawn, pawn.AWaitSt0);
                Draw(pawn, pawn.AUnDrawStand2H, 0);
                break;
            case SBasePawn.SA_WALK_CR_2Hto0H:
                AnimateWalking(pawn, pawn.AWaitCr0);
                Draw(pawn, pawn.AUnDrawCrouch2H, 0);
                break;

            // ---- jump, fall, land ----
            case SBasePawn.SA_JUMP_ST_0H:
            case SBasePawn.SA_JUMP_ST_2H:
                // Phys 2 (execute-action.md cases 90, 91): the client's MyDoJump has set it already; a server
                // running the received action only gets the falling physics from here.
                Falling(pawn);
                Animation.ActionAnimation.PlayJumpStart(pawn, action);
                break;
            case SBasePawn.SA_FALL_ST_0H:
            case SBasePawn.SA_LONG_FALL_ST_0H:
            case SBasePawn.SA_LEDGEFALL:
            case SBasePawn.SA_FALL_DEAD:
                Falling(pawn);
                Loop(pawn, 0, pawn.AFall, FallTween);
                break;
            case SBasePawn.SA_FALL_ST_2H:
                Falling(pawn);
                Loop(pawn, 0, pawn.AFall2H, FallTween);
                break;
            case SBasePawn.SA_LAND_ST_0H_LOW:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandLow, LandLowTween);
                break;
            case SBasePawn.SA_LAND_ST_0H_HIGH:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandHi, LandHighTween);
                break;
            case SBasePawn.SA_LAND_CR_0H_QUIET:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandQuiet, LandLowTween);
                break;
            case SBasePawn.SA_LAND_ST_2H:
            case SBasePawn.SA_LAND_ST_2H_LOW:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandLow2H, LandLowTween);
                break;
            case SBasePawn.SA_LAND_ST_2H_HIGH:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandHi2H, LandHighTween);
                break;
            case SBasePawn.SA_LAND_CR_2H_QUIET:
                Walking(pawn);
                Once(pawn, 0, pawn.ALandQuiet2H, LandLowTween);
                break;

            // ---- roll ----
            case SBasePawn.SA_ROLLING:
                Animation.ActionAnimation.PlayRoll(pawn, up: false);
                break;
            case SBasePawn.SA_ROLLINGUP:
                Animation.ActionAnimation.PlayRoll(pawn, up: true);
                break;
            case SBasePawn.SA_ROLL_LAND:
                Walking(pawn);
                break;

            default:
                TraversalActions.Execute(pawn, action);
                break;
        }
    }

    /// <summary>
    /// A draw or holster: the clip on the draw channel (AnimEnd 14 ends the weapon transit) and the weapon state
    /// (equipment's SetWeaponLoaded, at the start of the clip here).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "When in the clip the weapon is loaded belongs to the equipment port (draw notifies)")]
    private static void Draw(SBasePawn pawn, Name sequence, int weaponLoaded, float rate = 1f)
    {
        pawn.bWeaponTransit = true;
        if (!sequence.IsNone)
        {
            Once(pawn, SBasePawn.DRAWCHANNEL, sequence, 0f, rate);
        }

        MovementService.HooksOf(pawn).SetWeaponLoaded(pawn, weaponLoaded);

        // The spy's holster clip notify NotifyUndrawWeapon (spy-shocker.md rule 5) also asks for the laser off, so a
        // later hold draw, the cam leave or another laser state does not bring back the old request.
        if (weaponLoaded == 0 && pawn is SPawnAttaque && pawn.Controller is SPlayerController pc)
        {
            MovementService.HooksOf(pawn).SwitchLaser(pc, false);
        }
    }

    /// <summary>
    /// The modelled part of ASBasePawn::AnimateWalking: PHYS_Walking, LastZipP far away, BlendChangeTime, OldBTW
    /// cleared and the resting loop <paramref name="wait"/> on the base channel with the tween T: 0.25, 0 when the
    /// pawn ended its last tick in root motion or falling (OldPhysics), 0.1 while channel 0 plays a forward jump,
    /// 0.3 when it was leaning on a wall (OldBTW). The walk blender sets are not modelled.
    /// </summary>
    [UcNative("ASBasePawn::AnimateWalking", Editor = 0x111fd360)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360: T 0.25, 0 for OldPhysics 5 or 2, 0.1 with IsJumpingForward 0x111f9350, 0.3 with OldBTW")]
    public static void AnimateWalking(SBasePawn pawn, Name wait)
    {
        Walking(pawn);
        pawn.LastZipP = MovementK.FarZipPoint;
        float tween = StandTween;
        if (pawn.OldPhysics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_Falling)
        {
            tween = 0f;
        }

        Name playing = pawn.PawnAnim[0].Sequence;
        if (playing == pawn.AJumpForwardR || playing == pawn.AJumpForwardL)
        {
            pawn.BlendChangeTime = JumpForwardTween;
            tween = JumpForwardTween;
        }
        else
        {
            pawn.BlendChangeTime = StandTween;
        }

        if (pawn.OldBTW != default)
        {
            tween = LeaveWallTween;
        }

        pawn.OldBTW = default;
        Loop(pawn, 0, wait, tween);
    }

    /// <summary>Puts the pawn in PHYS_Walking through the engine's setPhysics.</summary>
    public static void Walking(SBasePawn pawn) => SetPhysics(pawn, EPhysics.PHYS_Walking);

    /// <summary>Puts the pawn in PHYS_Falling through the engine's setPhysics (the apex starts here).</summary>
    public static void Falling(SBasePawn pawn) => SetPhysics(pawn, EPhysics.PHYS_Falling);

    /// <summary>The engine's setPhysics, or the field when the level has no driver.</summary>
    public static void SetPhysics(SBasePawn pawn, EPhysics mode) => MovementNatives.SetPhysics(pawn, mode);

    /// <summary>Starts a looping clip.</summary>
    public static void Loop(SBasePawn pawn, int channel, Name sequence, float tween, float rate = 1f) =>
        pawn.PawnAnim.Play(channel, Clip(pawn, sequence), rate, tween, loop: true);

    /// <summary>Starts a one-shot clip (AnimEnd on <paramref name="channel"/> at its end).</summary>
    public static void Once(SBasePawn pawn, int channel, Name sequence, float tween, float rate = 1f) =>
        pawn.PawnAnim.Play(channel, Clip(pawn, sequence), rate, tween, loop: false);

    /// <summary>The clip of a sequence from the level's animation source, or a short stand-in.</summary>
    public static PawnClip Clip(SBasePawn pawn, Name sequence)
    {
        MovementService? service = MovementService.Of(pawn);
        IPawnAnimationSource source = service?.Animations ?? PawnAnimationSource.Default;
        if (source.TryGetClip(pawn, sequence, out PawnClip clip))
        {
            return clip;
        }

        return service?.StandIn(sequence, MissingClipFrames, MissingClipFps) ?? new PawnClip(sequence, MissingClipFrames, MissingClipFps);
    }

}
