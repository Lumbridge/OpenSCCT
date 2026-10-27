using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.Modes;

/// <summary>
/// Numbers of the mode-specific rules: Disk Hunt (spec/objectives/disk-hunt.md), the bomb spots
/// (spec/objectives/bomb-target.md), the pickup walk (spec/objectives/interaction-system.md rule 22) and the menu
/// defaults a match starts with (spec/game/game-modes.md).
/// </summary>
public static class K
{
    // ---- Disk Hunt ------------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.DropDisks: disk i is thrown with i * 30 * (1, 1, 1)")]
    public const float DiskDropVelocityStep = 30f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.DropDisks: message 30 when the carrier is a merc")]
    public const int MsgDisksDroppedMerc = 30;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.DropDisks: message 31 when the carrier is a spy")]
    public const int MsgDisksDroppedSpy = 31;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup.EndState: the first disk at (10, 0, -7) on B L Thigh")]
    public static readonly FVector DiskThighOffset = new(10f, 0f, -7f);

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup.EndState: disk rotation (0, 0, 32768)")]
    public static readonly FRotator DiskThighRotation = new(0, 0, 32768);

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimNotifyPickup: a hand item at (8, -5, 0) on B L Hand")]
    public static readonly FVector HandItemOffset = new(8f, -5f, 0f);

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimNotifyPickup: a hand item (not a flag) at rotation (-4000, 0, 32768)")]
    public static readonly FRotator HandItemRotation = new(-4000, 0, 32768);

    [Provenance(ProvenanceKind.Decoded, "ASMission::execActivateDisks (game 0x10AC01A0): ten group slots")]
    public const int MaxDiskGroups = 10;

    // ---- bomb spots -----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.Triggering: message 27 (under attack)")]
    public const int MsgBombUnderAttack = 27;

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.TakeDamage, SBombTargetInteraction.InitInteract: message 28 (secured)")]
    public const int MsgBombSecured = 28;

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.TakeObjective: message 29 (bombed)")]
    public const int MsgBombBombed = 29;

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.Triggering: the armed charge's ambient sound")]
    public static readonly AssetRef BombArmedSound = new("Armes_impacts.Armes.ProxMine_ACTI_1");

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.TakeObjective: the explosion sound")]
    public static readonly AssetRef BombExplosionSound = new("Armes_impacts.Armes.RA_ProxMine_Explo");

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.TakeObjective: PlaySound radius 500")]
    public const float BombExplosionSoundRadius = 500f;

    [Provenance(ProvenanceKind.Decoded, "SBombTargetObjectiveTrigger.TakeObjective: Spawn(class'SMineProxEmitter')")]
    public static readonly ClassRef BombEmitterClass = new("SBase.SMineProxEmitter");

    [Provenance(ProvenanceKind.Decoded, "ASGadBomb::execCanUse (game 0x10A9FBB0): WhyCantUse 0, no modem left or refire")]
    public const int WhyCantUseNoModemLeft = 0;

    [Provenance(ProvenanceKind.Decoded, "ASGadBomb::execCanUse: WhyCantUse 6, not walking or rolling")]
    public const int WhyCantUseWrongPosition = 6;

    // ---- the pickup walk ------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup_GoTo: an entry more than 20 uu below the pawn stands closer")]
    public const float PickupLowEntry = 20f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup_GoTo.TopSynchro: standing low pickup below 30 uu")]
    public const float PickupLowStanding = 30f;

    // ---- menu defaults (GUI.Create_Match) -------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules: time limit clamp 0..20")]
    public const int MenuMaxTimeLimit = 20;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules: lives clamp 0..10")]
    public const int MenuMaxLives = 10;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules: respawn clamp floor..15")]
    public const int MenuMaxRespawn = 15;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules: respawn 5 without a map entry")]
    public const int MenuNoEntryRespawn = 5;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules: disk boxes 1 and 1 without a map entry")]
    public const int MenuNoEntryDisks = 1;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.SetMercDeathMatchRules: merc lives box 0 with untouched settings")]
    public const int MercDeathMatchDefaultLives = 0;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.SetMercDeathMatchRules: respawn box 1 with untouched settings")]
    public const int MercDeathMatchDefaultRespawn = 1;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.SetMercDeathMatchRules: kill limit box minimum 1")]
    public const int MinKillLimit = 1;

    [Provenance(ProvenanceKind.Decoded, "Create_Match.RevengeChange: a revenge pair is two games in a row")]
    public const int RevengeGamesInARow = 2;
}
