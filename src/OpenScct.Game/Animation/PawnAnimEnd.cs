using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// SBasePawn.AnimEnd: the pawn's bookkeeping when a clip ends on a channel, before the controller's AnimEnd. The
/// overlay channels blend out (reanimate 16, interact 24, weapon 21, attack 19, goggle 18, damage 17, the palm's quick
/// throw 22), the draw channel ends the weapon transit and moves the back-to-wall fire pose on, the no-run overlay 1
/// hands back to the run channel, and channel 0 chains the clips that follow a one-shot (the split jump fall, the hack
/// loop, the corner turn, the terminate holds, the spy's lying pose after a death, the fall after a grab from a ledge).
/// </summary>
public static class PawnAnimEnd
{
    private static readonly Name ButtStAIFd0 = new("ButtStAIFd0");
    private static readonly Name ButtStAIFd2 = new("ButtStAIFd2");
    private static readonly Name MineStAlEd0 = new("MineStAlEd0");
    private static readonly Name MineCrAlEd0 = new("MineCrAlEd0");
    private static readonly Name ReloStSpFd2 = new("ReloStSpFd2");
    private static readonly Name ReloStSpFd1 = new("ReloStSpFd1");
    private static readonly Name ReloStSpFd2SU = new("ReloStSpFd2SU");
    private static readonly Name ReloStSpFd2S = new("ReloStSpFd2S");
    private static readonly Name ReloStSpFd3 = new("ReloStSpFd3");
    private static readonly Name BackStSpBgL = new("BackStSpBgL");
    private static readonly Name BackCrSpBgL = new("BackCrSpBgL");
    private static readonly Name BackStSpBgR = new("BackStSpBgR");
    private static readonly Name BackCrSpBgR = new("BackCrSpBgR");
    private static readonly Name SpltStAlRt0 = new("SpltStAlRt0");
    private static readonly Name SpltStAlLt0 = new("SpltStAlLt0");
    private static readonly Name HackStAlBg0 = new("HackStAlBg0");
    private static readonly Name HackStAlNt0 = new("HackStAlNt0");
    private static readonly Name BackCrAlTr0 = new("BackCrAlTr0");
    private static readonly Name BackStAlTr0 = new("BackStAlTr0");
    private static readonly Name AtakDnMmBg2 = new("AtakDnMmBg2");
    private static readonly Name AtakDnMmNt2 = new("AtakDnMmNt2");
    private static readonly Name AtkdDnMmBg0 = new("AtkdDnMmBg0");
    private static readonly Name AtkdDnMmNt0 = new("AtkdDnMmNt0");
    private static readonly Name XxxxDnAlFd0 = new("XxxxDnAlFd0");
    private static readonly Name XxxxCrAlAk0 = new("XxxxCrAlAk0");
    private static readonly Name XxxxStAlAk0 = new("XxxxStAlAk0");
    private static readonly Name StrkCrAlBk0 = new("StrkCrAlBk0");
    private static readonly Name XxxxDnAlBk0 = new("XxxxDnAlBk0");
    private static readonly Name GrbdStLgRail = new("GrbdStLgRail");
    private static readonly Name FallStAlFr0 = new("FallStAlFr0");
    private static readonly Name ThroStAlMix = new("ThroStAlMix");
    private static readonly Name s_FallAfterGrabbedFromLedge = new("s_FallAfterGrabbedFromLedge");

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd: the overlay channels blend out over 0.2 s")]
    public const float OverlayBlendOut = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 17: the damage channel blends out over 0.3 s")]
    public const float DamageBlendOut = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 22: ThroStAlMix blends out over 0.1 s")]
    public const float QuickThrowBlendOut = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 4: BlendChangeTime = 0.25")]
    public const float RunEndBlendChangeTime = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 0: the split jump falls with AFall over 0.35 s")]
    public const float SplitFallTween = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 0: the corner turn plays backward over a 0.2 s tween")]
    public const float CornerTween = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 0, GrbdStLgRail: Velocity = 77.5 X, Z = -681")]
    public const float LedgeFallForward = 77.5f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd channel 0, GrbdStLgRail: Velocity Z")]
    public const float LedgeFallZ = -681f;

    /// <summary>The pawn's part of AnimEnd on <paramref name="channel"/>.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEnd (script, Sha1 fade18fa): every channel case; SBasePawn.AnimEnd calls this")]
    public static void Channel(SBasePawn pawn, int channel)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        Name anim;
        switch (channel)
        {
            case SBasePawn.REANIMATECHANNEL:
                a.SetBlendParams(channel, 0f);
                break;
            case SBasePawn.INTERACTCHANNEL:
                anim = pawn.GetAnimName(channel);
                if (((anim == ButtStAIFd0 || anim == ButtStAIFd2) && a.IsAnimBackward(channel)) || anim == MineStAlEd0 || anim == MineCrAlEd0)
                {
                    a.BlendToAlpha(channel, 0f, OverlayBlendOut);
                }

                break;
            case SBasePawn.WEAPONCHANNEL:
                pawn.sAction = 0;
                anim = pawn.GetAnimName(channel);
                a.BlendToAlpha(channel, 0f, OverlayBlendOut);
                if (anim == ReloStSpFd2 || anim == ReloStSpFd1 || anim == ReloStSpFd2SU || anim == ReloStSpFd2S || anim == ReloStSpFd3)
                {
                    pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                }

                break;
            case SBasePawn.ATTACKCHANNEL:
                a.BlendToAlpha(channel, 0f, OverlayBlendOut);
                break;
            case 4:
                pawn.BlendChangeTime = RunEndBlendChangeTime;
                break;
            case SBasePawn.GOGGLECHANNEL:
                pawn.sAction = 0;
                pawn.bGogglesUp = !pawn.bGogglesUp;
                a.BlendToAlpha(channel, 0f, OverlayBlendOut);
                break;
            case SBasePawn.DAMAGECHANNEL:
                pawn.sAction = 0;
                a.BlendToAlpha(channel, 0f, DamageBlendOut);
                if (pawn.IK_Type == IK_Type_.IKT_NONE)
                {
                    pawn.IK_Type = pawn.IK_TypeBeforeDamage;
                }

                pawn.IK_TypeBeforeDamage = IK_Type_.IKT_NONE;
                break;
            case SBasePawn.DRAWCHANNEL:
                DrawEnd(pawn);
                break;
            case 1:
                if (pawn.bNoRunAnimation)
                {
                    pawn.bNoRunAnimation = false;
                    a.SetBlendParams(channel, 0f);
                    if (pawn.Physics == EPhysics.PHYS_Walking)
                    {
                        a.SetBlendParams(4, 1f);
                    }
                }

                break;
            case 0:
                BaseEnd(pawn);
                break;
            case SBasePawn.PALMCHANNEL:
                if (pawn.GetAnimName(channel) == ThroStAlMix)
                {
                    a.BlendToAlpha(channel, 0f, QuickThrowBlendOut);
                }

                break;
        }
    }

    /// <summary>The draw channel: off, the transit over, and the back-to-wall fire pose the transition led to.</summary>
    private static void DrawEnd(SBasePawn pawn)
    {
        PawnAnimation a = pawn.PawnAnim;
        a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f);
        pawn.bWeaponTransit = false;
        if (pawn.BTW == BTW_.NOBTW || pawn.IK_Type != IK_Type_.IKT_BTW || pawn.bStickyShocking)
        {
            pawn.BTW = BTW_.NOBTW;
            return;
        }

        Name anim = pawn.GetAnimName(SBasePawn.DRAWCHANNEL);
        bool back = a.IsAnimBackward(SBasePawn.DRAWCHANNEL);
        BTW_? next = null;
        if (anim == BackStSpBgL)
        {
            next = back ? BTW_.BTWStandLeft : BTW_.BTWFireStandLeft;
        }
        else if (anim == BackCrSpBgL)
        {
            next = back ? BTW_.BTWCrouchLeft : BTW_.BTWFireCrouchLeft;
        }
        else if (anim == BackCrSpBgR)
        {
            next = back ? BTW_.BTWCrouchRight : BTW_.BTWFireCrouchRight;
        }
        else if (anim == BackStSpBgR)
        {
            next = back ? BTW_.BTWStandRight : BTW_.BTWFireStandRight;
        }

        if (next is { } value)
        {
            pawn.BTW = value;
            ActionAnimation.AnimateBTW(pawn);
        }
    }

    /// <summary>Channel 0: the clip that follows the one that ended.</summary>
    private static void BaseEnd(SBasePawn pawn)
    {
        Name anim = pawn.GetAnimName(0);
        if (anim == SpltStAlRt0 || anim == SpltStAlLt0)
        {
            PawnActions.Once(pawn, 0, pawn.WeaponLoaded == 0 ? pawn.AFall : pawn.AFall2H, SplitFallTween);
        }
        else if (anim == HackStAlBg0)
        {
            PawnActions.Loop(pawn, 0, HackStAlNt0, 0f);
        }
        else if (anim == BackCrAlTr0 || anim == BackStAlTr0)
        {
            // PlayAnim(clip, , 0.2, , true): the turn plays back the way it came.
            pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, pawn.bIsCrouched ? BackCrAlTr0 : BackStAlTr0), 1f, CornerTween, loop: false, mirror: false, backward: true);
        }
        else if (anim == AtakDnMmBg2)
        {
            PawnActions.Walking(pawn);
            PawnActions.Loop(pawn, 0, AtakDnMmNt2, 0f);
        }
        else if (anim == AtkdDnMmBg0)
        {
            PawnActions.Loop(pawn, 0, AtkdDnMmNt0, 0f);
        }
        else if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
        {
            if (anim == XxxxDnAlFd0 || anim == pawn.ADeathCrRight || anim == pawn.ADeathCrLeft || anim == pawn.ADeathCrForward ||
                anim == pawn.ADeathStBack || anim == pawn.ADeathCrDown || anim == pawn.ADeathStRight || anim == pawn.ADeathStLeft ||
                anim == pawn.ADeathStForward || anim == pawn.ADeathCrBack || anim == pawn.ADeathStDown || anim == XxxxCrAlAk0 ||
                anim == XxxxStAlAk0 || anim == StrkCrAlBk0)
            {
                LieDown(pawn);
            }
        }
        else if (anim == StrkCrAlBk0)
        {
            LieDown(pawn);
        }
        else if (anim == GrbdStLgRail)
        {
            pawn.Rotation.GetAxes(out FVector x, out _, out _);
            PawnActions.Falling(pawn);
            pawn.bInternalChangePhysics = true;
            pawn.Velocity = new FVector(x.X * LedgeFallForward, x.Y * LedgeFallForward, LedgeFallZ);
            PawnActions.Once(pawn, 0, FallStAlFr0, 0f);
            if (pawn.Controller is SPlayerController pc)
            {
                pc.m_LastSpecialMoveTime = pawn.Level.TimeSeconds;
                pc.GotoState(s_FallAfterGrabbedFromLedge);
            }
        }
    }

    /// <summary>The lying pose after a fall-down clip: walking again and XxxxDnAlBk0.</summary>
    private static void LieDown(SBasePawn pawn)
    {
        PawnActions.Walking(pawn);
        pawn.bInternalChangePhysics = true;
        PawnActions.Once(pawn, 0, XxxxDnAlBk0, 0f);
    }
}
