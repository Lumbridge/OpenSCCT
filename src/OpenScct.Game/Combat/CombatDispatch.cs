using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>Which PawnTakeDamage handler a controller state uses (spec/combat/state-damage-reactions.md).</summary>
public enum DamageHandler : byte
{
    Global,
    Walking,
    Jump,
    InteractWithObject,
    Berserk,
    Charging,
    Blast,
    GrabBlastOnly,
    GrabFromLedge,
    GrabbedByPlayer,
    GrabbingPlayer,
    ReleaseGrabbed,
    FallDown,
    TerminateBase,
    CoopSBase,
    CoopSGoto,
    ShortLadderPBase,
    BtwBase,
    BinocularsDef,
    BinocularsAtt,
    UseObjectMirador,
    UseObjectStickyCam,
    StickyCamSmooth,
    MaintainEnterUse,
    MaintainLeave,
    HackEnter,
    HackUse,
    HackLeave,
    ComputerEnter,
    ComputerUse,
    ComputerLeave,
    PutFlag,
    PlaceBomb,
    PlaceBombEnd,
    Ignored,
}

/// <summary>Which ClientDamage version runs on the owner (state-damage-reactions.md rules 33 to 36).</summary>
public enum ClientDamageKind : byte
{
    Global,
    GrabBase,
    LaserFirst,
}

/// <summary>Which PlayAnimDmg version runs on the owner (damage-pipeline.md rules 39 to 41).</summary>
public enum HitAnimKind : byte
{
    Global,
    Walking,
    None,
}

/// <summary>Which CanBeBlasted / ExplosionHit version a state has (blast-and-fall-damage.md rules 1 and 2).</summary>
public enum BlastKind : byte
{
    None,
    Standing,
    BackToWall,
    Fence,
    GrabBase,
    BlastOnly,
}

/// <summary>
/// The state dispatch of the combat functions, as tables of original state names. The tables follow the effective
/// handlers the specs list (resolved through the original <c>extends</c> chains), so they hold for states that no
/// port has defined yet; a ported state that implements the matching state interface takes precedence.
/// </summary>
[Provenance(ProvenanceKind.Decoded, "spec/combat/state-damage-reactions.md dispatch table (extends chains and ignores of all 280 states)")]
public static class CombatDispatch
{
    private static readonly Dictionary<Name, DamageHandler> s_damage = BuildDamage();
    private static readonly Dictionary<Name, ClientDamageKind> s_clientDamage = BuildClientDamage();
    private static readonly HashSet<Name> s_walkingHitAnim = Set(WalkingFamily());
    private static readonly HashSet<Name> s_noHitAnim = Set(
        "s_PlayerJump", "s_PreSplitJump", "s_SplitJump", "PlayerFalling", "PlayerFallToBackGrab", "PlayerFallingFromRoll",
        "PlayerLanding", "PlayerLandingSilent", "s_PlayerCharging_HitStickyShocked", "s_Dead", "s_BaseSpectating",
        "s_MultiSpectating", "s_MultiSpectating_WhenKilled", "s_Spectating_Visitor", "s_Cinematic");
    private static readonly HashSet<Name> s_noDeathAnim = Set(
        "s_Dead", "s_LyingOnGround", "s_TerminatedPawn", "s_BaseSpectating", "s_MultiSpectating",
        "s_MultiSpectating_WhenKilled", "s_Spectating_Visitor", "s_Cinematic");
    private static readonly HashSet<Name> s_noFatalBlastNotice = Set(
        "s_Blast", "s_BlastFallAfterObstacle", "s_BlastFloor", "s_FallAfterGrabbedFromLedge");
    private static readonly Dictionary<Name, BlastKind> s_canBeBlasted = BuildCanBeBlasted();
    private static readonly Dictionary<Name, BlastKind> s_explosionHit = BuildExplosionHit();
    private static readonly HashSet<Name> s_inactive = Set(
        "s_Inactive", "s_FallDown", "s_FallDownDying", "s_LyingOnGround", "s_StandUp", "s_Reanimating",
        "s_KOAfterBlastFall", "s_SleepAfterKOBlastFall", "s_BlastWakeUp", "s_LandingInerte", "s_TerminatedPawn",
        "s_PlayerFalling_OutOfWorld");
    private static readonly HashSet<Name> s_chargeHit = Set(
        "s_PlayerCharging_Hit", "s_PlayerCharging_HitEnemy", "s_PlayerCharging_HitFriend", "s_PlayerCharging_HitWall",
        "s_PlayerCharging_HitStickyShocked");
    private static readonly HashSet<Name> s_noSmoke = BuildNoSmoke();
    private static readonly HashSet<Name> s_plainWalking = Set(
        "s_PlayerWalkingSt2H", "s_PlayerWalkingSt0H", "s_PlayerWalkingCr0H", "s_PlayerWalkingCr2H");
    private static readonly HashSet<Name> s_canBeGrabbed = Set(
        "s_PlayerWalkingSt2H", "s_PlayerWalkingCr2H", "s_GrabbingPlayer", "s_ReanimateFriend_2H",
        "s_UseObject_CamNetworkCrWaitForServer", "s_UseObject_CamNetworkStWaitForServer", "s_InteractWithObject",
        "s_InteractWithObjectWaitingEvent", "s_InteractWithObjectWaitingEventCrToSt", "s_MenuInteraction",
        "s_MenuInteraction_GoToInteractionPosition", "s_BasePutMineOnWall", "s_PutMineOnWall", "s_PutMineOnWall_Grab",
        "s_PutMineOnWall_UnGrab", "s_PutMineOnWall_InteractionMenu", "s_PlayerCharging_Hit", "s_PlayerCharging_HitEnemy",
        "s_PlayerCharging_HitFriend", "s_PlayerCharging_HitWall", "s_TerminatePawn", "s_TerminatePawn_Maintain",
        "s_TerminatePawn_Kill", "s_Hack_Use", "s_ThrowReady", "s_UseObject_Mirador", "s_StickyCam_MenuInteraction",
        "s_UseObject_CamNetwork", "s_Binoculars_Def", "s_Binoculars_Def_Reloading");
    private static readonly HashSet<Name> s_walking = Set(WalkingFamily());
    private static readonly HashSet<Name> s_jump = Set(JumpFamily());
    private static readonly HashSet<Name> s_rolling = Set("s_PlayerRolling", "s_PlayerRollingUp");
    private static readonly HashSet<Name> s_laser = Set(
        "s_PlayerWalking", "s_PlayerWalkingMario", "s_PlayerWalkingSt0H", "s_PlayerWalkingCr0H",
        "s_PlayerWalkingMario_T_CrToSt", "s_PlayerWalkingMario_T_StToCr", "s_PlayerWalkingWeapon", "s_PlayerWalkingSt2H",
        "s_PlayerWalkingCr2H", "s_PlayerWalkingWeapon_T_CrToSt", "s_PlayerWalkingWeapon_T_StToCr", "s_PlayerHit",
        "s_PlayerRolling", "s_PlayerRollingUp", "s_PipH_Fire2", "s_PipH_Fire4", "s_PipH_Fire2TR2", "s_PipH_Fire2TR4",
        "s_PipH_TR2Fire2", "s_PipH_TR2Fire4", "VandammeWeapon", "s_BTWFireCrouchLeft", "s_BTWFireCrouchRight",
        "s_BTWFireStandLeft", "s_BTWFireStandRight", "s_Binoculars_Def");

    /// <summary>The PawnTakeDamage handler of a state (the global one for states not listed).</summary>
    public static DamageHandler DamageHandlerFor(Name State) => s_damage.GetValueOrDefault(State, DamageHandler.Global);

    /// <summary>The ClientDamage version of a state.</summary>
    public static ClientDamageKind ClientDamageFor(Name State) =>
        s_clientDamage.GetValueOrDefault(State, ClientDamageKind.Global);

    /// <summary>The PlayAnimDmg version of a state.</summary>
    public static HitAnimKind HitAnimFor(Name State) =>
        s_noHitAnim.Contains(State) ? HitAnimKind.None : s_walkingHitAnim.Contains(State) ? HitAnimKind.Walking : HitAnimKind.Global;

    /// <summary>True when the state ignores PlayAnimDeath on the owner (damage-pipeline.md rule 46).</summary>
    public static bool IgnoresPlayAnimDeath(Name State) => s_noDeathAnim.Contains(State);

    /// <summary>True when the state ignores TellClientServerWaitForFatalBlast (the blast states).</summary>
    public static bool IgnoresFatalBlastNotice(Name State) => s_noFatalBlastNotice.Contains(State);

    /// <summary>The CanBeBlasted version of a state.</summary>
    public static BlastKind CanBeBlastedFor(Name State) => s_canBeBlasted.GetValueOrDefault(State, BlastKind.None);

    /// <summary>The ExplosionHit version of a state.</summary>
    public static BlastKind ExplosionHitFor(Name State) => s_explosionHit.GetValueOrDefault(State, BlastKind.None);

    /// <summary>True in s_Inactive and its children (knocked down, lying, standing up, blast KO, terminated).</summary>
    public static bool IsInactive(Name State) => s_inactive.Contains(State);

    /// <summary>True in the s_PlayerCharging_Hit family (Checkshock forces the shock animation).</summary>
    public static bool IsChargeHit(Name State) => s_chargeHit.Contains(State);

    /// <summary>
    /// True in the states that ignore GoInSmoke: the s_Inactive family, the grab family (s_GrabBase), s_Dead,
    /// the spectating states and GameEnded.
    /// </summary>
    public static bool IgnoresSmoke(Name State) => s_noSmoke.Contains(State);

    /// <summary>True in the four plain walking stances (the taser knock-down states).</summary>
    public static bool IsPlainWalking(Name State) => s_plainWalking.Contains(State);

    /// <summary>True in s_PlayerWalking and every state that extends it, the weapon walking states and transitions.</summary>
    public static bool IsWalking(Name State) => s_walking.Contains(State);

    /// <summary>True in s_PlayerJump and the falling states that extend it.</summary>
    public static bool IsJumping(Name State) => s_jump.Contains(State);

    /// <summary>CanBeGrabbed of states no combat state class defines (spy-grab.md, evidence).</summary>
    public static bool DefaultCanBeGrabbed(Name State) => s_canBeGrabbed.Contains(State);

    /// <summary>CanBeElbowShocked: the walking family when not shocking, the jump family, the scope.</summary>
    public static bool DefaultCanBeElbowShocked(Name State, bool bStickyShocking) =>
        (s_walking.Contains(State) && !bStickyShocking) || s_jump.Contains(State) || State == NAME_s_Binoculars_Def;

    /// <summary>CanBeDropAttacked of states no combat state class defines: the walking family and the scope.</summary>
    [Provenance(ProvenanceKind.Estimated, "Drop attack spec not ported: walking family and scope are attackable")]
    public static bool DefaultCanBeDropAttacked(Name State) => s_walking.Contains(State) || State == NAME_s_Binoculars_Def;

    /// <summary>CanActivateLaser: s_PlayerWalking and the weapon walking, pipe fire, Vandamme weapon and back-to-wall fire states.</summary>
    public static bool DefaultCanActivateLaser(Name State) => s_laser.Contains(State);

    /// <summary>CanIUseBinocular: the walking family unless rolling, and the scope itself.</summary>
    public static bool DefaultCanIUseBinocular(Name State) =>
        (s_walking.Contains(State) && !s_rolling.Contains(State)) || State == NAME_s_Binoculars_Def;

    private static readonly Name NAME_s_Binoculars_Def = new("s_Binoculars_Def");

    private static string[] WalkingFamily() =>
    [
        "s_PlayerWalking", "s_PlayerWalkingMario", "s_PlayerWalkingWeapon", "s_PlayerWalkingSt0H", "s_PlayerWalkingSt2H",
        "s_PlayerWalkingCr0H", "s_PlayerWalkingCr2H", "s_PlayerWalkingMario_T_0HTo2H", "s_PlayerWalkingMario_T_2HToOH",
        "s_PlayerWalkingMario_T_CrToSt", "s_PlayerWalkingMario_T_StToCr", "s_PlayerWalkingWeapon_T_CrToSt",
        "s_PlayerWalkingWeapon_T_StToCr", "s_PlayerRolling", "s_PlayerRollingUp", "PlayerLanding", "PlayerLandingSilent",
        "s_PlayerHit", "s_FirstWallJumpPrepare", "s_FirstWallJumpPrepareFromJump", "s_FirstWallJumpSaltoPrepareFromJump",
        "s_BasePutMineOnWall", "s_PutMineOnWall", "s_PutMineOnWall_Grab", "s_PutMineOnWall_UnGrab",
        "s_PutMineOnWall_InteractionMenu", "s_Throw", "s_ThrowGoTo", "s_ThrowLeave", "s_ThrowReady",
        "s_ReanimateFriend_0H", "s_ReanimateFriend_2H", "s_StickyCam_MenuInteraction", "s_UseObject_StickyCam_Leave",
        "s_UseObject_CamNetworkCrWaitForServer", "s_UseObject_CamNetworkStWaitForServer",
    ];

    private static string[] JumpFamily() =>
    [
        "s_PlayerJump", "s_PreSplitJump", "s_SplitJump", "PlayerFalling", "PlayerFallToBackGrab", "PlayerFallingFromRoll",
        "PlayerFallingInerte", "PlayerFallingKO",
    ];

    private static string[] GrabBlastOnlyFamily() =>
    [
        "s_GrabBaseWithBlastDamageOnly", "s_Grab_Avoided", "s_Grab_GotoPosition", "s_Grabbing_GoToPosToFreeGuy",
        "s_Grabbing_GotoPosition", "s_Grabbing_UncrouchForGrab", "s_GrabbingPlayer_Begin", "s_GrabbingPlayer_RealBegin",
        "s_GrabbedByPlayer_Begin", "s_GrabbedByPlayer_AvoidGrab", "s_GrabbedByPlayer_Released",
        "s_GrabbedByPlayer_Released_KO", "s_GrabbedByPlayer_Released_STRANGLE", "s_GrabbedByPlayer_Released_DMG",
        "s_GrabbedByPlayer_Released_Grabbing_LD", "s_GrabbedByPlayer_Released_GrabbingDMG", "s_GrabbedByPlayer_CounterGrab",
    ];

    private static string[] GrabFromLedgeFamily() =>
        ["s_GrabFromLedgeBase", "s_GrabbedFromLedge_Begin", "s_GrabbingFromLedge_Begin", "s_GrabbingFromLedge_Go"];

    private static string[] ReleaseFamily() =>
    [
        "s_ReleaseGrabbedPlayer", "s_ReleaseGrabbedPlayer_LD", "s_ReleaseGrabbedPlayer_KO", "s_ReleaseGrabbedPlayer_Strangle",
        "s_ReleaseGrabbedPlayer_DMG", "s_ReleaseGrabbedPlayer_BothDMG", "s_ReleaseGrabbedPlayer_Grabbed_DMGED",
        "s_GrabRelease_Counter",
    ];

    private static string[] BtwFamily() =>
    [
        "s_BTW_Base", "s_BTW", "s_BTWFireCrouchLeft", "s_BTWFireCrouchRight", "s_BTWFireStandLeft", "s_BTWFireStandRight",
        "s_BTW_CrouchLeft", "s_BTW_CrouchRight", "s_BTW_StandLeft", "s_BTW_StandRight", "s_BTW_down", "s_BTW_up",
        "s_BTWCorner", "s_EnterBTW", "s_SortieBTW",
    ];

    private static string[] CoopSFamily() =>
        ["s_Coop_S_Base", "s_Hoist_S_Base", "s_ShortLadder_S_Base", "s_ShortLadder_S_Jump", "s_Hoist_S_Jump"];

    private static string[] CoopGotoFamily() => ["s_Coop_S_Goto", "s_ShortLadder_S_GotoPos", "s_Hoist_S_GotoPos"];

    private static HashSet<Name> BuildNoSmoke()
    {
        HashSet<Name> set = Set(
            "s_Inactive", "s_FallDown", "s_FallDownDying", "s_LyingOnGround", "s_StandUp", "s_Reanimating",
            "s_KOAfterBlastFall", "s_SleepAfterKOBlastFall", "s_BlastWakeUp", "s_LandingInerte", "s_TerminatedPawn",
            "s_PlayerFalling_OutOfWorld", "s_GrabBase", "s_Dead", "s_BaseSpectating", "s_MultiSpectating",
            "s_MultiSpectating_WhenKilled", "s_Spectating_Visitor", "GameEnded");
        foreach (string s in GrabBlastOnlyFamily())
        {
            set.Add(new Name(s));
        }

        foreach (string s in GrabFromLedgeFamily())
        {
            set.Add(new Name(s));
        }

        return set;
    }

    private static Dictionary<Name, DamageHandler> BuildDamage()
    {
        var d = new Dictionary<Name, DamageHandler>();
        Add(d, DamageHandler.Walking, WalkingFamily());
        Add(d, DamageHandler.Jump, JumpFamily());
        Add(d, DamageHandler.GrabBlastOnly, GrabBlastOnlyFamily());
        Add(d, DamageHandler.GrabFromLedge, GrabFromLedgeFamily());
        Add(d, DamageHandler.ReleaseGrabbed, ReleaseFamily());
        Add(d, DamageHandler.GrabbedByPlayer, "s_GrabbedByPlayer");
        Add(d, DamageHandler.GrabbingPlayer, "s_GrabbingPlayer");
        Add(d, DamageHandler.Berserk, "s_Berserk360");
        Add(d, DamageHandler.Charging, "s_PlayerCharging");
        Add(d, DamageHandler.Blast, "s_Blast", "s_BlastFallAfterObstacle", "s_FallAfterGrabbedFromLedge");
        Add(d, DamageHandler.FallDown, "s_FallDown", "s_FallDownDying");
        Add(d, DamageHandler.TerminateBase, "s_TerminatePawn_Base", "s_TerminatePawn_ReposBegin", "s_TerminatePawn",
            "s_TerminatePawn_Maintain", "s_TerminatePawn_Kill", "s_TerminatePawn_Fall");
        Add(d, DamageHandler.InteractWithObject, "s_InteractWithObject", "s_InteractWithObjectWaitingEvent",
            "s_InteractWithObjectWaitingEventCrToSt", "s_MenuInteraction", "s_MenuInteraction_GoToInteractionPosition");
        Add(d, DamageHandler.CoopSBase, CoopSFamily());
        Add(d, DamageHandler.CoopSGoto, CoopGotoFamily());
        Add(d, DamageHandler.ShortLadderPBase, "s_ShortLadder_P_Base", "s_ShortLadder_P_WaitForShortLadder",
            "s_ShortLadder_P_Waiting", "s_ShortLadder_P_GotoDest", "s_ShortLadder_P_Out");
        Add(d, DamageHandler.BtwBase, BtwFamily());
        Add(d, DamageHandler.BinocularsDef, "s_Binoculars_Def", "s_Binoculars_Def_Reloading");
        Add(d, DamageHandler.BinocularsAtt, "s_Binoculars_Att");
        Add(d, DamageHandler.UseObjectStickyCam, "s_UseObject_StickyCam", "s_UseObject_CamNetwork");
        Add(d, DamageHandler.StickyCamSmooth, "s_UseObject_StickyCam_SmoothToPosition");
        Add(d, DamageHandler.UseObjectMirador, "s_UseObject_Mirador");
        Add(d, DamageHandler.MaintainEnterUse, "s_Maintain_Enter", "s_Maintain_Use");
        Add(d, DamageHandler.MaintainLeave, "s_Maintain_Leave");
        Add(d, DamageHandler.HackEnter, "s_Hack_Enter");
        Add(d, DamageHandler.HackUse, "s_Hack_Use");
        Add(d, DamageHandler.HackLeave, "s_Hack_Leave");
        Add(d, DamageHandler.ComputerEnter, "s_Computer_Enter");
        Add(d, DamageHandler.ComputerUse, "s_Computer_Use");
        Add(d, DamageHandler.ComputerLeave, "s_Computer_Leave");
        Add(d, DamageHandler.PutFlag, "s_PutFlagInDropZone");
        Add(d, DamageHandler.PlaceBomb, "s_PlaceBomb");
        Add(d, DamageHandler.PlaceBombEnd, "s_PlaceBomb_End");
        Add(d, DamageHandler.Ignored, "s_BlastFloor", "s_PlayerFalling_OutOfWorld", "s_Dead", "s_BaseSpectating",
            "s_MultiSpectating", "s_MultiSpectating_WhenKilled", "s_Spectating_Visitor", "GameEnded", "s_Cinematic");
        return d;
    }

    private static Dictionary<Name, ClientDamageKind> BuildClientDamage()
    {
        var d = new Dictionary<Name, ClientDamageKind>();
        Add(d, ClientDamageKind.GrabBase, "s_GrabBase", "s_GrabbedByPlayer");
        Add(d, ClientDamageKind.GrabBase, GrabBlastOnlyFamily());
        Add(d, ClientDamageKind.GrabBase, GrabFromLedgeFamily());
        Add(d, ClientDamageKind.LaserFirst, "s_Berserk360", "s_PlayerCharging");
        return d;
    }

    private static Dictionary<Name, BlastKind> BuildCanBeBlasted()
    {
        var d = new Dictionary<Name, BlastKind>();
        Add(d, BlastKind.Standing, WalkingFamily());
        Add(d, BlastKind.Standing, JumpFamily());
        Add(d, BlastKind.Standing, "s_Berserk360");
        Add(d, BlastKind.Standing, CoopSFamily());
        Add(d, BlastKind.Standing, CoopGotoFamily());
        Add(d, BlastKind.BackToWall, "s_BTW", "s_BTWFireCrouchLeft", "s_BTWFireCrouchRight", "s_BTWFireStandLeft",
            "s_BTWFireStandRight", "s_BTW_CrouchLeft", "s_BTW_CrouchRight", "s_BTW_StandLeft", "s_BTW_StandRight",
            "s_BTW_down", "s_BTW_up");
        Add(d, BlastKind.Fence, "GrabFence", "OnFence");
        Add(d, BlastKind.BlastOnly, "s_Blast");
        return d;
    }

    private static Dictionary<Name, BlastKind> BuildExplosionHit()
    {
        var d = new Dictionary<Name, BlastKind>();
        foreach (KeyValuePair<Name, BlastKind> pair in BuildCanBeBlasted())
        {
            if (pair.Value != BlastKind.BlastOnly)
            {
                d[pair.Key] = pair.Value;
            }
        }

        Add(d, BlastKind.Standing, "s_StandUp", "s_Reanimating", "s_TerminatePawn_Base", "s_TerminatePawn_ReposBegin",
            "s_TerminatePawn", "s_TerminatePawn_Maintain", "s_TerminatePawn_Kill", "s_TerminatePawn_Fall");
        Add(d, BlastKind.GrabBase, "s_GrabBase", "s_GrabbedByPlayer");
        Add(d, BlastKind.GrabBase, GrabBlastOnlyFamily());
        Add(d, BlastKind.GrabBase, GrabFromLedgeFamily());
        return d;
    }

    private static void Add<T>(Dictionary<Name, T> d, T value, params string[] states)
    {
        foreach (string s in states)
        {
            d[new Name(s)] = value;
        }
    }

    private static HashSet<Name> Set(params string[] states)
    {
        var set = new HashSet<Name>();
        foreach (string s in states)
        {
            set.Add(new Name(s));
        }

        return set;
    }
}
