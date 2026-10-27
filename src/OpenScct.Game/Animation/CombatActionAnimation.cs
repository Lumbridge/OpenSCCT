using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The ExecuteAction cases no action port plays (spec/native/execute-action.md section 4): the weapon overlay (reload,
/// fire), deaths and the stand-up, hits (PlayDamage), the cough, the binoculars, the back-to-wall fire transitions,
/// throws, blasts and their wake-ups, the grab and grabbed clips, the terminate and counter-grab clips, the inert fall
/// and landing, and the switch interaction. Each case does what the table gives (physics mode, root motion, clip,
/// blend, fields); the script events of a case (skins, sounds, the BTW arm control) are left to their owners.
/// <see cref="ActionAnimation.End"/> calls <see cref="Execute"/> after the ports, so it only runs for these actions.
/// </summary>
public static class CombatActionAnimation
{
    private static readonly Name BoneSpine1 = new("B Spine1");
    private static readonly Name BoneHead = new("B Head");

    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.4: the death clips tween over 0.1 s")]
    public const float DeathTween = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.4: the standing drop-attack death tweens over 0.25 s")]
    public const float DropDeathStandTween = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md: the 0.1 s tween of the enter clips (t0.1)")]
    public const float T01 = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 48, 55, 56, 74: to cN 0/0.2")]
    public const float BlendOut = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.2: PlayLanding of 101 tweens LandStXxBk2 over 0.0833 s")]
    public const float InertLandTween = 0.0833f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 106: c0 FallStXxBk2 t0.2")]
    public const float InertFallTween = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 317, 318: terminate begin t0.15")]
    public const float TerminateTween = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 22: GrabStAlxxa at rate 4")]
    public const float GrabReleaseLdRate = 4f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 314: Velocity = 77.5 X with Z = -681")]
    public const float GrabbedLedgeFallForward = 77.5f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 314: Velocity Z -681")]
    public const float GrabbedLedgeFallZ = -681f;

    /// <summary>Plays one of the cases this class covers; false for any other action.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 6, 7, 11, 12, 18 to 37, 40 to 65, 73, 74, 92, 101, 106, 183 to 192, 230 to 233, 239 to 243, 249 to 251, 283 to 285, 291 to 297, 312 to 319, 325, 327")]
    public static bool Execute(SBasePawn pawn, int action)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        bool merc = pawn.SPawnType == PawnType.SPAWNDEFENSE;
        bool cr = pawn.bIsCrouched;
        switch (action)
        {
            // ---- the weapon overlay (6, 291 to 297): blend c21 1 from B Spine1 ----
            case SBasePawn.SA_RELOAD:
            case SBasePawn.SA_RELOAD_1:
            case SBasePawn.SA_RELOAD_2:
            case SBasePawn.SA_RELOAD_3:
            case SBasePawn.SA_RELOAD_4:
            case SBasePawn.SA_RELOAD_5:
                pawn.iNbAmmoToReload = (byte)(action == SBasePawn.SA_RELOAD ? 6 : action - SBasePawn.SA_RELOAD_1 + 1);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                a.SetBlendParams(SBasePawn.WEAPONCHANNEL, 1f, bone: BoneSpine1);
                PawnActions.Once(pawn, SBasePawn.WEAPONCHANNEL, ReloadClip(pawn), 0f);
                return true;
            case SBasePawn.SA_FIRE:
                a.SetBlendParams(SBasePawn.WEAPONCHANNEL, 1f, bone: BoneSpine1);
                Once(pawn, SBasePawn.WEAPONCHANNEL, "ReloStSpFd3", 0f);
                return true;
            case SBasePawn.SA_FIRE_GRENADE:
                a.SetBlendParams(SBasePawn.WEAPONCHANNEL, 1f, bone: BoneSpine1);
                Once(pawn, SBasePawn.WEAPONCHANNEL, "ReloStSpFd1", 0f);
                return true;

            // ---- landings and the inert fall (7, 92, 101, 106) ----
            case SBasePawn.SA_DROPATTACK_LAND:
                PawnActions.Walking(pawn);
                pawn.m_AttackTarget = null;
                PlayLanding(pawn, action);
                return true;
            case SBasePawn.SA_LAND_DROPATTACK:
                PawnActions.Walking(pawn);
                pawn.Acceleration = default;
                pawn.Velocity = default;
                PlayLanding(pawn, action);
                return true;
            case SBasePawn.SA_LAND_INERTE:
                pawn.SetCollision(true, false, false);
                PawnActions.Walking(pawn);
                pawn.Acceleration = default;
                pawn.Velocity = default;
                PlayLanding(pawn, action);
                return true;
            case SBasePawn.SA_FALL_INERTE:
                pawn.SetCollision(true, false, false);
                pawn.bBeginSpecial12Anim = false;
                pawn.IK_Type = IK_Type_.IKT_NONE;
                pawn.PipHFire = default;
                pawn.bWeaponTransit = false;
                PawnActions.Falling(pawn);
                Once(pawn, 0, "FallStXxBk2", InertFallTween);
                return true;

            // ---- knock-out and wake (11, 12) ----
            case SBasePawn.SA_STANDUP:
                pawn.bStillGrabbed = false;
                pawn.m_AttackedBy = null;
                pawn.m_AttackTarget = null;
                pawn.SetCollision(true, true, true);
                pawn.bCollideWorld = true;
                PawnActions.Walking(pawn);
                AnimateStandUp(pawn);
                return true;
            case SBasePawn.SA_REANIMATE:
                a.SetBlendParams(SBasePawn.REANIMATECHANNEL, 1f);
                Once(pawn, SBasePawn.REANIMATECHANNEL,
                    pawn.WeaponLoaded == 2 ? (cr ? "ReanCrAlFd2" : "ReanStAlFd2") : (cr ? "ReanCrAlFd0" : "ReanStAlFd0"), 0f);
                return true;

            // ---- grab (18, 20 to 29) ----
            case SBasePawn.SA_GRAB_CATCH:
                PawnActions.Walking(pawn);
                pawn.m_PawnNoMoreGrabbed = pawn.m_AttackTarget;
                pawn.bCanWalkOffLedges = false;
                pawn.bAvoidLedges = true;
                pawn.bStopAtLedges = true;
                pawn.IK_Type = IK_Type_.IKT_NONE;
                Once(pawn, 0, "GrabStAlBg0", 0f);
                Partner(pawn.m_AttackTarget, pawn, SBasePawn.SA_GRABBED_CATCH);
                return true;
            case SBasePawn.SA_GRAB:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                PawnActions.AnimateWalking(pawn, new Name("GrabStAlNt0"));
                if (pawn.m_AttackTarget is { } grabbed && !ReferenceEquals(grabbed, pawn))
                {
                    grabbed.m_AttackedBy = pawn;
                    grabbed.ExecuteAction(SBasePawn.SA_GRABBED);
                }

                return true;
            case SBasePawn.SA_GRAB_AVOIDED:
                PawnActions.AnimateWalking(pawn, pawn.AWaitSt0);
                return true;
            case SBasePawn.SA_GRAB_RELEASE:
                Once(pawn, 0, "GrabStAlXxb", 0f);
                Partner(pawn.m_PawnNoMoreGrabbed, pawn, SBasePawn.SA_GRABBED_RELEASE_KO);
                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_RELEASE_GRABBED_LD:
                Once(pawn, 0, "GrabStAlxxa", 0f, GrabReleaseLdRate);
                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_RELEASE_KO:
                Once(pawn, 0, "GrabStAlKo0", 0f);
                Partner(pawn.m_PawnNoMoreGrabbed, pawn, SBasePawn.SA_GRABBED_RELEASE_KO);
                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_RELEASE_STRANGLE:
                Once(pawn, 0, "GrabStAlxxa", 0f);
                Partner(pawn.m_PawnNoMoreGrabbed, pawn, SBasePawn.SA_GRABBED_RELEASE_STRANGLE);
                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_RELEASE_DMG:
                Once(pawn, 0, "GrabStAlXxC", 0f);
                if (pawn.m_PawnNoMoreGrabbed is { } held && !ReferenceEquals(held, pawn))
                {
                    GrabbedEndRestore(held);
                }

                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_RELEASE_GRABBED_DMGED:
            case SBasePawn.SA_GRAB_RELEASE_BOTH_DIE:
                Once(pawn, 0, action == SBasePawn.SA_GRAB_RELEASE_BOTH_DIE ? "GrabStAlXxC" : "GrabStAlxxa", 0f);
                Partner(pawn.m_PawnNoMoreGrabbed, pawn, SBasePawn.SA_GRABBED_RELEASE_DMG);
                Release(pawn);
                return true;
            case SBasePawn.SA_GRAB_DROPATTACKED:
                if (!merc)
                {
                    DeathRootMotion(pawn, 3, EPhysics.PHYS_None);
                }

                PlayDeath(pawn, 2);
                Partner(pawn.m_PawnNoMoreGrabbed, pawn, SBasePawn.SA_GRABBED_RELEASE_DROPTECH);
                Release(pawn);
                pawn.SetCollision(false, false, false);
                return true;

            // ---- grabbed (30 to 37, 327) ----
            case SBasePawn.SA_GRABBED_AVOID:
                pawn.SetCollision(true, true, true);
                pawn.bCollideWorld = true;
                pawn.bActionsSendByServerMove = false;
                PawnActions.AnimateWalking(pawn, pawn.AWaitSt2);
                return true;
            case SBasePawn.SA_GRABBED_CATCH:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_FollowParent);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                pawn.SetCollision(true, false, false);
                pawn.bCollideWorld = false;
                pawn.bActionsSendByServerMove = false;
                Once(pawn, 0, "GrbdStAlBg0", 0f);
                return true;
            case SBasePawn.SA_GRABBED:
                if (pawn.m_AttackedBy is not null)
                {
                    PawnActions.SetPhysics(pawn, EPhysics.PHYS_FollowParent);
                    pawn.IK_Type = IK_Type_.IKT_NONE;
                    PawnActions.Loop(pawn, 0, new Name("GrbdStAlNt0"), WaitTween(pawn));
                }

                return true;
            case SBasePawn.SA_GRABBED_RELEASE:
            case SBasePawn.SA_GRABBED_RELEASE_STRANGLE:
            case SBasePawn.SA_GRABBED_RELEASE_DMG:
                PawnActions.Walking(pawn);
                pawn.bStillGrabbed = true;
                pawn.bActionsSendByServerMove = false;
                pawn.m_AttackedBy = null;
                if (action != SBasePawn.SA_GRABBED_RELEASE)
                {
                    pawn.SetCollision(true, false, false);
                }

                pawn.bCollideWorld = true;
                Once(pawn, 0, "GrbdStAlXxa", 0f);
                return true;
            case SBasePawn.SA_GRABBED_RELEASE_KO:
                PawnActions.Walking(pawn);
                pawn.bStillGrabbed = true;
                pawn.bSleeping = true;
                pawn.bActionsSendByServerMove = false;
                pawn.m_AttackedBy = null;
                pawn.SetCollision(true, false, false);
                pawn.bCollideWorld = true;
                Once(pawn, 0, "GrbdStAlKo0", 0f);
                return true;
            case SBasePawn.SA_GRABBED_RELEASE_GRABBING_LD:
                PawnActions.Walking(pawn);
                pawn.bStillGrabbed = true;
                pawn.m_AttackedBy = null;
                pawn.SetCollision(true, true, true);
                pawn.bCollideWorld = true;
                pawn.bActionsSendByServerMove = false;
                Once(pawn, 0, "GrbdStAlEd0", 0f);
                return true;
            case SBasePawn.SA_GRABBED_RELEASE_DROPTECH:
                RootMotion(pawn, 3, null, viaNoneWhenInRootMotion: true);
                pawn.bStillGrabbed = true;
                pawn.m_AttackedBy = null;
                pawn.SetCollision(true, false, false);
                Once(pawn, 0, "GrbdStAlXxb", 0f);
                return true;

            // ---- deaths (40 to 46) ----
            case SBasePawn.SA_DIE_FORWARD:
            case SBasePawn.SA_DIE_LEFT:
            case SBasePawn.SA_DIE_RIGHT:
                if (merc)
                {
                    PawnActions.Walking(pawn);
                }
                else
                {
                    DeathRootMotion(pawn, 3, EPhysics.PHYS_Walking);
                }

                PlayDeath(pawn, action == SBasePawn.SA_DIE_FORWARD ? 1 : action == SBasePawn.SA_DIE_LEFT ? 3 : 4);
                Dying(pawn);
                return true;
            case SBasePawn.SA_DIE_BACKWARD:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_None);
                RootMotion(pawn, 1, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false);
                PlayDeath(pawn, 2);
                Dying(pawn);
                return true;
            case SBasePawn.SA_DIE_INPLACE:
                PawnActions.Walking(pawn);
                if (!merc)
                {
                    RootMotion(pawn, 3, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false);
                }

                PlayDeath(pawn, 0);
                Dying(pawn);
                return true;
            case SBasePawn.SA_DIE_FALLING:
                PawnActions.Walking(pawn);
                pawn.m_AttackedBy = null;
                pawn.SetCollision(true, false, false);
                Once(pawn, 0, "LandStXxDn2", 0f);
                return true;
            case SBasePawn.SA_DIE_DROPATTACK:
                PlayDeath(pawn, 5);
                if (merc)
                {
                    PawnActions.Walking(pawn);
                }
                else
                {
                    DeathRootMotion(pawn, 3, EPhysics.PHYS_Walking);
                }

                Dying(pawn);
                return true;

            // ---- cough (47, 48) ----
            case SBasePawn.SA_COUGHT_START:
                a.SetBlendParams(SBasePawn.COUGHTCHANNEL, 1f, bone: BoneHead);
                PawnActions.Loop(pawn, SBasePawn.COUGHTCHANNEL, new Name("CaughStSpFd2"), 0f);
                return true;
            case SBasePawn.SA_COUGHT_STOP:
                a.BlendToAlpha(SBasePawn.COUGHTCHANNEL, 0f, BlendOut);
                return true;

            // ---- hits (49 to 65) ----
            case SBasePawn.SA_DMG_ELECTRIC:
            case SBasePawn.SA_DMG_ELECTRIC_NOANIMS:
                // execute-action.md 53, 54: the Stickess skin, the flag, the seven sparks (sticky-shock-effect.md).
                pawn.BeginStickySkin();
                pawn.bStickyShocking = true;
                pawn.evStickyShockSfx(true);
                DamageTail(pawn, action);
                return true;
            case SBasePawn.SA_DMG_SPECIAL_LEFT:
            case SBasePawn.SA_DMG_SPECIAL_RIGHT:
            case SBasePawn.SA_DMG_SPECIAL_FRONT:
            case SBasePawn.SA_DMG_SPECIAL_BACK:
            case SBasePawn.SA_DMG_HEAD:
            case SBasePawn.SA_DMG_CHEST_BACK:
            case SBasePawn.SA_DMG_CHEST_FRONT:
            case SBasePawn.SA_DMG_CHEST_LEFT:
            case SBasePawn.SA_DMG_CHEST_RIGHT:
            case SBasePawn.SA_DMG_ARM_LEFT:
            case SBasePawn.SA_DMG_ARM_RIGHT:
            case SBasePawn.SA_DMG_LEG_LEFT:
            case SBasePawn.SA_DMG_LEG_RIGHT:
                DamageTail(pawn, action);
                return true;
            case SBasePawn.SA_DMG_ELECTRIC_STOP:
            case SBasePawn.SA_DMG_ELECTRIC_STOP_ANIM:
                a.BlendToAlpha(SBasePawn.ELECTRICCHANNEL, 0f, BlendOut);
                if (pawn.IK_Type == IK_Type_.IKT_NONE)
                {
                    pawn.IK_Type = pawn.IK_TypeBeforeDamage;
                }

                pawn.IK_TypeBeforeDamage = IK_Type_.IKT_NONE;
                if (action == SBasePawn.SA_DMG_ELECTRIC_STOP)
                {
                    pawn.bStickyShocking = false;
                    pawn.RestoreSkins();
                    pawn.evStickyShockSfx(false);
                }

                return true;

            // ---- binoculars (73, 74) ----
            case SBasePawn.SA_BINOCULAR_USING:
                PawnActions.Loop(pawn, SBasePawn.PALMCHANNEL, new Name("WaitSpSpMix"), 0f);
                a.SetBlendParams(SBasePawn.PALMCHANNEL, 0f, bone: BoneSpine1);
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 1f, BlendOut);
                return true;
            case SBasePawn.SA_BINOCULAR_END_USING:
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 0f, BlendOut);
                return true;

            // ---- back-to-wall fire transitions (183 to 192) and fire holds (184, 186) ----
            case SBasePawn.SA_BTW_FIRE_ST_LEFT_TR:
                BtwFireTransition(pawn, "BackStSpBgL", backward: false);
                return true;
            case SBasePawn.SA_BTW_FIRE_ST_RIGHT_TR:
                BtwFireTransition(pawn, "BackStSpBgR", backward: false);
                return true;
            case SBasePawn.SA_BTW_FIRE_ST_LEFT_UNTR:
                BtwFireTransition(pawn, "BackStSpBgL", backward: true);
                return true;
            case SBasePawn.SA_BTW_FIRE_ST_RIGHT_UNTR:
                BtwFireTransition(pawn, "BackStSpBgR", backward: true);
                return true;
            case SBasePawn.SA_BTW_FIRE_CR_LEFT_TR:
                BtwFireTransition(pawn, "BackCrSpBgL", backward: false);
                return true;
            case SBasePawn.SA_BTW_FIRE_CR_RIGHT_TR:
                BtwFireTransition(pawn, "BackCrSpBgR", backward: false);
                return true;
            case SBasePawn.SA_BTW_FIRE_CR_LEFT_UNTR:
                BtwFireTransition(pawn, "BackCrSpBgL", backward: true);
                return true;
            case SBasePawn.SA_BTW_FIRE_CR_RIGHT_UNTR:
                BtwFireTransition(pawn, "BackCrSpBgR", backward: true);
                return true;
            case SBasePawn.SA_BTW_FIRE_LEFT:
                BtwSet(pawn, cr ? BTW_.BTWFireCrouchLeft : BTW_.BTWFireStandLeft);
                return true;
            case SBasePawn.SA_BTW_FIRE_RIGHT:
                BtwSet(pawn, cr ? BTW_.BTWFireCrouchRight : BTW_.BTWFireStandRight);
                return true;

            // ---- throws (230 to 233, 251) ----
            case SBasePawn.SA_THROW_ENTER:
                Once(pawn, 0, cr ? "ThroCrAlBgH" : "ThroStAlBgH", T01);
                return true;
            case SBasePawn.SA_THROW_LOOP:
                PawnActions.Loop(pawn, 0, new Name(cr ? "ThroCrAlNtH" : "ThroStAlNtH"), 0f);
                return true;
            case SBasePawn.SA_THROW:
                PawnActions.Loop(pawn, 0, new Name(cr ? "ThroCrAlEdH" : "ThroStAlEdH"), 0f);
                return true;
            case SBasePawn.SA_THROW_LEAVE:
                Back(pawn, 0, cr ? "ThroCrAlBgH" : "ThroStAlBgH", 0f);
                return true;
            case SBasePawn.SA_QUICK_THROW:
                pawn.bQuickGadgetHasBegun = true;
                Once(pawn, SBasePawn.PALMCHANNEL, "ThroStAlMix", 0f);
                a.SetBlendParams(SBasePawn.PALMCHANNEL, 0f, bone: BoneSpine1);
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 1f, T01);
                return true;

            // ---- blasts (239 to 243, 249, 250) ----
            case SBasePawn.SA_BLAST_LOOP:
                PawnActions.Falling(pawn);
                pawn.bLyingOnGround = true;
                pawn.SetCollision(true, false, false);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                PawnActions.Loop(pawn, 0, new Name("BlstStAlDn2"), T01);
                return true;
            case SBasePawn.SA_BLAST_LOOP_AFTERGRAB:
                PawnActions.Falling(pawn);
                pawn.bLyingOnGround = true;
                pawn.SetCollision(true, false, false);
                pawn.bCollideWorld = true;
                pawn.m_AttackTarget = null;
                pawn.m_AttackedBy = null;
                pawn.bAvoidLedges = false;
                pawn.bStopAtLedges = false;
                pawn.bCanWalkOffLedges = true;
                PawnActions.Loop(pawn, 0, new Name("BlstStAlDn2"), T01);
                return true;
            case SBasePawn.SA_BLAST_OBSTACLE:
                PawnActions.Falling(pawn);
                pawn.m_AttackTarget = null;
                pawn.m_AttackedBy = null;
                pawn.bLyingOnGround = true;
                Once(pawn, 0, "BlstStAlIt2", 0f);
                return true;
            case SBasePawn.SA_BLAST_FLOOR:
                // +W end0 with the lock type left as it is.
                RootMotion(pawn, pawn.iLockRootMotionType, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false, viaNone: true);
                pawn.m_AttackTarget = null;
                pawn.m_AttackedBy = null;
                pawn.bLyingOnGround = true;
                Once(pawn, 0, "BlstStAlEd2", 0f);
                return true;
            case SBasePawn.SA_BLAST_WAKEUP:
                RootMotion(pawn, 1, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false, viaNone: true);
                PawnActions.Once(pawn, 0, merc ? (cr ? pawn.AWakeCrBack2H : pawn.AWakeStBack2H) : (cr ? pawn.AWakeCrBack : pawn.AWakeStBack), 0f);
                return true;
            case SBasePawn.SA_LAND_AFTER_BLAST_WALL:
                PawnActions.Walking(pawn);
                pawn.bWeaponTransit = true;
                if (merc)
                {
                    Equipment.PawnVision.EvSetWeaponLoaded(pawn, 0);
                }

                Once(pawn, 0, "LandStXxFd2", 0f);
                return true;
            case SBasePawn.SA_WAKE_AFTER_BLAST_WALL:
                RootMotion(pawn, 1, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false, viaNone: true);
                pawn.bWeaponTransit = true;
                Once(pawn, 0, merc ? "WakeStSpFd2" : "WakeStSpDn0", 0f);
                return true;

            // ---- switch held down (283 to 285) ----
            case SBasePawn.SA_SWM_ENTER:
                PawnActions.Walking(pawn);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                MovesetActions.QuickGadgetCancel(pawn);
                a.SetBlendParams(SBasePawn.INTERACTCHANNEL, 0f);
                Once(pawn, 0, "ButtStAIFd0", T01);
                return true;
            case SBasePawn.SA_SWM_BACK:
                PawnActions.Walking(pawn);
                Back(pawn, 0, "ButtStAIFd0", 0f);
                return true;
            case SBasePawn.SA_SWM_MAINTAIN:
                PawnActions.Walking(pawn);
                PawnActions.Loop(pawn, 0, new Name("ButtStAINt0"), T01);
                return true;

            // ---- grab from a ledge (312 to 314) ----
            case SBasePawn.SA_GRABFROMLEDGE_GO:
                RootMotion(pawn, 1, null, viaNoneWhenInRootMotion: false);
                if (!merc)
                {
                    Once(pawn, 0, "GrabLgEdRail", T01);
                    Partner(pawn.m_AttackTarget, pawn, SBasePawn.SA_GRABFROMLEDGE_GO);
                }
                else
                {
                    Once(pawn, 0, "GrbdStLgRail", T01);
                }

                return true;
            case SBasePawn.SA_GRABFROMLEDGE_BEGIN:
                Once(pawn, 0, "GrabLgBgRail", 0f);
                return true;
            case SBasePawn.SA_GRABBEDFROMLEDGE_FALL:
                pawn.Rotation.GetAxes(out FVector x, out _, out _);
                pawn.Velocity = new FVector(x.X * GrabbedLedgeFallForward, x.Y * GrabbedLedgeFallForward, GrabbedLedgeFallZ);
                PawnActions.Falling(pawn);
                Once(pawn, 0, "FallStAlFr0", 0f);
                return true;

            // ---- terminate (317 to 319) and the counter grab (325) ----
            case SBasePawn.SA_TERMINATEPAWN_BEGIN:
                if (merc)
                {
                    pawn.IK_Type = IK_Type_.IKT_NONE;
                    RootMotion(pawn, 5, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false, viaNone: true);
                    Once(pawn, 0, "AtakDnMmBg2", TerminateTween);
                    Partner(pawn.m_PawnToTerminate, pawn, SBasePawn.SA_TERMINATEPAWN_BEGIN);
                }
                else
                {
                    PawnActions.SetPhysics(pawn, EPhysics.PHYS_None);
                    Once(pawn, 0, "AtkdDnMmBg0", TerminateTween);
                }

                return true;
            case SBasePawn.SA_TERMINATEPAWN_KILL:
                if (merc)
                {
                    PawnActions.SetPhysics(pawn, EPhysics.PHYS_None);
                    Once(pawn, 0, "AtakDnMmKo2", 0f);
                    Partner(pawn.m_PawnToTerminate, pawn, SBasePawn.SA_TERMINATEPAWN_KILL);
                }
                else
                {
                    Once(pawn, 0, "AtkdDnMmKo0", 0f);
                }

                return true;
            case SBasePawn.SA_TERMINATEPAWN_RELEASE:
                if (!merc)
                {
                    Once(pawn, 0, "AtkdDnMmEd0", T01);
                }

                return true;
            case SBasePawn.SA_COUNTER_GRAB_GO:
                a.ZeroMovementAlpha(1, PawnAnimation.ChannelCount, T01);
                if (merc)
                {
                    RootMotion(pawn, 1, null, viaNoneWhenInRootMotion: false);
                    Once(pawn, 0, "GrbdStMmEd", 0f);
                    Partner(pawn.m_AttackedBy, pawn, SBasePawn.SA_COUNTER_GRAB_GO);
                }
                else
                {
                    RootMotion(pawn, 5, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false);
                    Once(pawn, 0, "GrabStMmEd1", 0f);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// PlayDeath(d) (execute-action.md rule 5.4): IK_Type 1, lying on the ground, then the death clip on channel 0 by
    /// direction (0 down, 1 forward, 2 back, 3 left, 4 right) and stance from the spy fields, the 2H fields for a merc,
    /// over 0.1 s; d 5 is the drop-attack fall.
    /// </summary>
    [UcNative("ASBasePawn::PlayDeath", Editor = 0x111f9410)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public static void PlayDeath(SBasePawn pawn, int direction)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        pawn.IK_Type = IK_Type_.IKT_NONE;
        pawn.bLyingOnGround = true;
        bool merc = pawn.SPawnType == PawnType.SPAWNDEFENSE;
        bool cr = pawn.bIsCrouched;
        Name clip;
        float tween = DeathTween;
        if (direction == 5)
        {
            clip = new Name(merc ? (cr ? "XxxxCrAlAk2" : "XxxxStAlAk2") : (cr ? "XxxxCrAlAk0" : "XxxxStAlAk0"));
            tween = cr ? DeathTween : DropDeathStandTween;
        }
        else if (merc)
        {
            clip = direction switch
            {
                1 => cr ? pawn.ADeathCrForward2H : pawn.ADeathStForward2H,
                2 => cr ? pawn.ADeathCrBack2H : pawn.ADeathStBack2H,
                3 => cr ? pawn.ADeathCrLeft2H : pawn.ADeathStLeft2H,
                4 => cr ? pawn.ADeathCrRight2H : pawn.ADeathStRight2H,
                _ => cr ? pawn.ADeathCrDown2H : pawn.ADeathStDown2H,
            };
        }
        else
        {
            clip = direction switch
            {
                1 => cr ? pawn.ADeathCrForward : pawn.ADeathStForward,
                2 => cr ? pawn.ADeathCrBack : pawn.ADeathStBack,
                3 => cr ? pawn.ADeathCrLeft : pawn.ADeathStLeft,
                4 => cr ? pawn.ADeathCrRight : pawn.ADeathStRight,
                _ => cr ? pawn.ADeathCrDown : pawn.ADeathStDown,
            };
        }

        PawnActions.Once(pawn, 0, clip, tween);
    }

    /// <summary>
    /// PlayDamage(Action) (execute-action.md rule 5.5) after the damage tail: the merc's first-person pain clip, then
    /// the special hits on channel 17 from B Head; a moving pawn only flinches its head (GoglStAlDn2); at rest the
    /// electric loop on channel 23 or the body hit on channel 17 by stance, weapon and side.
    /// </summary>
    [UcNative("ASBasePawn::PlayDamage", Editor = 0x111fd9a0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public static void PlayDamage(SBasePawn pawn, int action)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        PlayFirstPersonDamage(pawn, action);
        string? special = action switch
        {
            SBasePawn.SA_DMG_SPECIAL_LEFT => "PainStHsLt2",
            SBasePawn.SA_DMG_SPECIAL_RIGHT => "PainStHsRt2",
            SBasePawn.SA_DMG_SPECIAL_FRONT => "PainStHsFd2",
            SBasePawn.SA_DMG_SPECIAL_BACK => "PainStHsBk2",
            _ => null,
        };
        if (special is not null)
        {
            a.SetBlendParams(SBasePawn.DAMAGECHANNEL, 1f, bone: BoneHead);
            Once(pawn, SBasePawn.DAMAGECHANNEL, special, 0f);
            return;
        }

        if (!pawn.Velocity.IsZero() && action != SBasePawn.SA_DMG_ELECTRIC)
        {
            a.SetBlendParams(SBasePawn.DAMAGECHANNEL, 1f, bone: BoneHead);
            Once(pawn, SBasePawn.DAMAGECHANNEL, "GoglStAlDn2", 0f);
            return;
        }

        if (action != SBasePawn.SA_DMG_ELECTRIC && action != SBasePawn.SA_DMG_ELECTRIC_NOANIMS)
        {
            a.SetBlendParams(SBasePawn.DAMAGECHANNEL, 1f);
        }

        bool cr = pawn.bIsCrouched;
        bool armed = pawn.WeaponLoaded != 0;
        switch (action)
        {
            case SBasePawn.SA_DMG_ELECTRIC:
            case SBasePawn.SA_DMG_ELECTRIC_NOANIMS:
                if (action == SBasePawn.SA_DMG_ELECTRIC)
                {
                    a.SetBlendParams(SBasePawn.ELECTRICCHANNEL, 1f);
                }
                else
                {
                    a.SetBlendParams(SBasePawn.ELECTRICCHANNEL, 1f, bone: BoneHead);
                }

                Name electric = cr ? (armed ? pawn.ADamageCrElectric2 : pawn.ADamageCrElectric0) : (armed ? pawn.ADamageStElectric2 : pawn.ADamageStElectric0);
                PawnActions.Loop(pawn, SBasePawn.ELECTRICCHANNEL, electric, BlendOut);
                return;
            case SBasePawn.SA_DMG_HEAD:
            case SBasePawn.SA_DMG_CHEST_BACK:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrChestBack2 : pawn.ADamageCrChestBack0) : (armed ? pawn.ADamageStChestBack2 : pawn.ADamageStChestBack0));
                return;
            case SBasePawn.SA_DMG_CHEST_FRONT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrChestForward2 : pawn.ADamageCrChestForward0) : (armed ? pawn.ADamageStChestForward2 : pawn.ADamageStChestForward0));
                return;
            case SBasePawn.SA_DMG_CHEST_LEFT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrChestLeft2 : pawn.ADamageCrChestLeft0) : (armed ? pawn.ADamageStChestLeft2 : pawn.ADamageStChestLeft0));
                return;
            case SBasePawn.SA_DMG_CHEST_RIGHT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrChestRight2 : pawn.ADamageCrChestRight0) : (armed ? pawn.ADamageStChestRight2 : pawn.ADamageStChestRight0));
                return;
            case SBasePawn.SA_DMG_ARM_LEFT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrArmLeft2 : pawn.ADamageCrArmLeft0) : (armed ? pawn.ADamageStArmLeft2 : pawn.ADamageStArmLeft0));
                return;
            case SBasePawn.SA_DMG_ARM_RIGHT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrArmRight2 : pawn.ADamageCrArmRight0) : (armed ? pawn.ADamageStArmRight2 : pawn.ADamageStArmRight0));
                return;
            case SBasePawn.SA_DMG_LEG_LEFT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrLegLeft2 : pawn.ADamageCrLegLeft0) : (armed ? pawn.ADamageStLegLeft2 : pawn.ADamageStLegLeft0));
                return;
            case SBasePawn.SA_DMG_LEG_RIGHT:
                Hit(pawn, cr ? (armed ? pawn.ADamageCrLegRight2 : pawn.ADamageCrLegRight0) : (armed ? pawn.ADamageStLegRight2 : pawn.ADamageStLegRight0));
                return;
            default:
                a.SetBlendParams(SBasePawn.DAMAGECHANNEL, 0f);
                return;
        }
    }

    /// <summary>
    /// AnimateStandUp (execute-action.md rule 5.12, editor 0x111f8ce0): the merc's wake clip follows the fall-down
    /// pose on channel 0 (the backward ones through root motion); the spy always wakes on its back through root motion.
    /// </summary>
    [UcNative("ASBasePawn::AnimateStandUp", Editor = 0x111f8ce0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    [Provenance(ProvenanceKind.Decoded, "AnimateStandUp 0x111f8ce0: the merc's pose to wake table, root motion L1 +W end0 for the backward ones; the spy by stance and weapon; iLastWakeTime = int(now + wake clip length)")]
    public static void AnimateStandUp(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        Name pose = pawn.PawnAnim[0].Sequence;
        Name wake;
        if (pawn.SPawnType != PawnType.SPAWNDEFENSE)
        {
            StandUpRootMotion(pawn);
            wake = pawn.bIsCrouched ? (pawn.WeaponLoaded == 0 ? pawn.AWakeCrBack : pawn.AWakeCrBack2H)
                : (pawn.WeaponLoaded == 0 ? pawn.AWakeStBack : pawn.AWakeStBack2H);
        }
        else if (!MercWake(pawn, pose, out wake))
        {
            return;
        }

        if (wake.IsNone)
        {
            return;
        }

        PawnActions.Once(pawn, 0, wake, 0f);

        // The wake time is the end of the wake clip: int(TimeSeconds + frames / rate) when the rate is positive (the _ftol at 0x1123fb80 truncates)
        // (editor 0x111f9067..0x111f912b, iLastWakeTime at +0x63c).
        PawnClip clip = PawnActions.Clip(pawn, wake);
        if (clip.Fps > 0f)
        {
            pawn.iLastWakeTime = SvmMath.Trunc(pawn.Level.TimeSeconds + clip.Frames / clip.Fps);
        }
    }

    private static readonly Name StrkCrAlBk0 = new("StrkCrAlBk0");
    private static readonly Name GrabStMmEd1 = new("GrabStMmEd1");

    private static bool MercWake(SBasePawn p, Name pose, out Name wake)
    {
        bool strk = pose == StrkCrAlBk0;
        wake = Name.None;
        if (pose == p.ADeathCrForward) { wake = p.AWakeCrForward; }
        else if (pose == p.ADeathCrLeft) { wake = p.AWakeCrLeft; }
        else if (pose == p.ADeathCrRight) { wake = p.AWakeCrRight; }
        else if (pose == p.ADeathCrDown) { wake = p.AWakeCrDown; }
        else if (pose == new Name("XxxxCrAlAk0")) { wake = new Name("WakeCrSpFd0"); }
        else if (pose == p.ADeathCrBack || (strk && p.StrkType == StrkType_.STRK_CR0)) { StandUpRootMotion(p); wake = p.AWakeCrBack; }
        else if (pose == p.ADeathStForward) { wake = p.AWakeStForward; }
        else if (pose == p.ADeathStBack || (strk ? p.StrkType == StrkType_.STRK_ST0 : pose == GrabStMmEd1)) { StandUpRootMotion(p); wake = p.AWakeStBack; }
        else if (pose == p.ADeathStLeft) { wake = p.AWakeStLeft; }
        else if (pose == p.ADeathStRight) { wake = p.AWakeStRight; }
        else if (pose == p.ADeathStDown) { wake = p.AWakeStDown; }
        else if (pose == p.ADeathCrForward2H) { wake = p.AWakeCrForward2H; }
        else if (pose == p.ADeathCrBack2H || (strk && p.StrkType == StrkType_.STRK_CR2)) { StandUpRootMotion(p); wake = p.AWakeCrBack2H; }
        else if (pose == p.ADeathCrLeft2H) { wake = p.AWakeCrLeft2H; }
        else if (pose == p.ADeathCrRight2H) { wake = p.AWakeCrRight2H; }
        else if (pose == p.ADeathCrDown2H) { wake = p.AWakeCrDown2H; }
        else if (pose == p.ADeathStForward2H) { wake = p.AWakeStForward2H; }
        else if (pose == p.ADeathStBack2H || (strk && p.StrkType == StrkType_.STRK_ST2)) { StandUpRootMotion(p); wake = p.AWakeStBack2H; }
        else if (pose == p.ADeathStLeft2H) { wake = p.AWakeStLeft2H; }
        else if (pose == p.ADeathStRight2H) { wake = p.AWakeStRight2H; }
        else if (pose == p.ADeathStDown2H) { wake = p.AWakeStDown2H; }
        else if (pose == new Name("GrbdStAlXxa") || pose == new Name("GrbdStAlXxb") || pose == new Name("GrbdStAlKo0")) { wake = new Name("GrbdStAlWkA"); }
        else if (pose == new Name("XxxxStAlAk0")) { wake = new Name("WakeStSpFd0"); }
        else if (pose == new Name("XxxxStAlAk2")) { wake = new Name("WakeStSpFd2"); }
        else if (pose == new Name("XxxxCrAlAk2")) { wake = new Name("WakeCrSpFd2"); }
        else if (pose == new Name("XxxxDnAlBk0")) { wake = p.bIsCrouched ? p.AWakeCrBack2H : p.AWakeStBack2H; }
        else if (pose == new Name("LandStXxBk2")) { StandUpRootMotion(p); wake = new Name("WakeStSpBk2"); }
        else { return false; }

        return true;
    }

    /// <summary>PlayLanding(Action) for the landings no port plays (execute-action.md rule 5.2): 7, 92 and 101.</summary>
    [UcNative("ASBasePawn::PlayLanding", Editor = 0x111f99d0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    private static void PlayLanding(SBasePawn pawn, int action)
    {
        bool loaded = pawn.WeaponLoaded != 0;
        switch (action)
        {
            case SBasePawn.SA_DROPATTACK_LAND:
                Once(pawn, 0, loaded ? "LandStAkDA2" : "LandStAkDA0", 0f);
                PawnActions.Walking(pawn);
                break;
            case SBasePawn.SA_LAND_DROPATTACK:
                pawn.PawnAnim.SetBlendParams(SBasePawn.ATTACKCHANNEL, 1f);
                Once(pawn, SBasePawn.ATTACKCHANNEL, loaded ? "LandStAkKA2" : "LandStAkKA0", 0f);
                PawnActions.Walking(pawn);
                break;
            case SBasePawn.SA_LAND_INERTE:
                RootMotion(pawn, 1, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false);
                Once(pawn, 0, "LandStXxBk2", InertLandTween);
                break;
        }
    }

    private static void PlayFirstPersonDamage(SBasePawn pawn, int action)
    {
        int side = Combat.CombatRules.FirstPersonPain(action);
        if (side >= 0 && pawn.MyWeapon is SWeaponDefense rifle)
        {
            rifle.AnimatePain(side);
        }
    }

    /// <summary>The damage tail (49 to 54, 57 to 65): the IK type before the hit is kept, IK off, then PlayDamage.</summary>
    private static void DamageTail(SBasePawn pawn, int action)
    {
        if (pawn.IK_TypeBeforeDamage == IK_Type_.IKT_NONE)
        {
            pawn.IK_TypeBeforeDamage = pawn.IK_Type;
        }

        pawn.IK_Type = IK_Type_.IKT_NONE;
        PlayDamage(pawn, action);
    }

    private static void Hit(SBasePawn pawn, Name clip) => PawnActions.Once(pawn, SBasePawn.DAMAGECHANNEL, clip, 0f);

    /// <summary>The fire transition of the back-to-wall (183, 185, 187 to 192): Walking, the weapon in transit, c14 at 1.</summary>
    private static void BtwFireTransition(SBasePawn pawn, string clip, bool backward)
    {
        PawnActions.Walking(pawn);
        pawn.bWeaponTransit = true;
        pawn.PawnAnim.SetBlendParams(SBasePawn.DRAWCHANNEL, 1f);
        if (backward)
        {
            Back(pawn, SBasePawn.DRAWCHANNEL, clip, 0f);
        }
        else
        {
            Once(pawn, SBasePawn.DRAWCHANNEL, clip, 0f);
        }
    }

    /// <summary>The BTW set (184, 186): Walking, the BTW value, IK_Type 3, then AnimateBTW.</summary>
    private static void BtwSet(SBasePawn pawn, BTW_ value)
    {
        PawnActions.Walking(pawn);
        pawn.BTW = value;
        pawn.IK_Type = IK_Type_.IKT_BTW;
        ActionAnimation.AnimateBTW(pawn);
    }

    /// <summary>"dying" (40 to 44, 46): no attacker, sleeping, collision with actors only.</summary>
    private static void Dying(SBasePawn pawn)
    {
        pawn.m_AttackedBy = null;
        pawn.bSleeping = true;
        pawn.SetCollision(true, false, false);
    }

    /// <summary>"release" (21 to 26, 28, 29): no target, and the pawn walks off ledges again.</summary>
    private static void Release(SBasePawn pawn)
    {
        pawn.m_AttackTarget = null;
        pawn.bAvoidLedges = false;
        pawn.bStopAtLedges = false;
        pawn.bCanWalkOffLedges = true;
    }

    /// <summary>The grabbed-end restore of the partner of 25 (editor 0x111fa1f0).</summary>
    private static void GrabbedEndRestore(SBasePawn held)
    {
        PawnActions.Walking(held);
        held.bActionsSendByServerMove = false;
        held.m_AttackedBy = null;
        held.SetCollision(true, true, true);
        held.bCollideWorld = true;
        Once(held, 0, "GrbdStAlEd0", 0f);
    }

    /// <summary>X(other, n): the partner pawn runs its own action inside this one.</summary>
    private static void Partner(SBasePawn? other, SBasePawn self, int action)
    {
        if (other is not null && !ReferenceEquals(other, self))
        {
            other.ExecuteAction(action);
        }
    }

    /// <summary>The spy's death move: PHYS_None first when already in root motion, then root motion with the lock.</summary>
    private static void DeathRootMotion(SBasePawn pawn, int lockType, EPhysics additional) =>
        RootMotion(pawn, lockType, additional, viaNoneWhenInRootMotion: true);

    private static void StandUpRootMotion(SBasePawn pawn) =>
        RootMotion(pawn, 1, EPhysics.PHYS_Walking, viaNoneWhenInRootMotion: false, viaNone: true);

    /// <summary>
    /// A root-motion move with no end correction (end0): the lock type, the additional physics (null: none), then
    /// PHYS_RootMotion, through PHYS_None first when asked (or when already in root motion and asked for that).
    /// </summary>
    private static void RootMotion(SBasePawn pawn, int lockType, EPhysics? additional, bool viaNoneWhenInRootMotion, bool viaNone = false)
    {
        pawn.iLockRootMotionType = lockType;
        pawn.bUseRootMotionAdditionnalPhysics = additional is not null;
        if (additional is { } extra)
        {
            pawn.RootMotionAdditionnalPhysics = extra;
        }

        pawn.vMoveAfterRM = default;
        pawn.rRotAfterRM = 0;
        if (viaNone || (viaNoneWhenInRootMotion && pawn.Physics == EPhysics.PHYS_RootMotion))
        {
            PawnActions.SetPhysics(pawn, EPhysics.PHYS_None);
        }

        PawnActions.SetPhysics(pawn, EPhysics.PHYS_RootMotion);
    }

    private static float WaitTween(SBasePawn pawn) =>
        pawn.OldPhysics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_Falling ? 0f : ActionAnimation.DefaultBlendChangeTime;

    private static Name ReloadClip(SBasePawn pawn)
    {
        object? weapon = (object?)pawn.MyWeapon ?? (pawn as SPAWNDEFENSE)?.WeaponDummy;
        return weapon switch
        {
            SWeaponDefense_Shotgun or SWeaponDefenseDummy_Shotgun => new Name("ReloStSpFd2S"),
            SWeaponDefense_Uzi or SWeaponDefenseDummy_Uzi => new Name("ReloStSpFd2SU"),
            _ => new Name("ReloStSpFd2"),
        };
    }

    private static void Once(SBasePawn pawn, int channel, string clip, float tween, float rate = 1f) =>
        PawnActions.Once(pawn, channel, new Name(clip), tween, rate);

    private static void Back(SBasePawn pawn, int channel, string clip, float tween) =>
        pawn.PawnAnim.Play(channel, PawnActions.Clip(pawn, new Name(clip)), 1f, tween, loop: false, mirror: false, backward: true);
}
