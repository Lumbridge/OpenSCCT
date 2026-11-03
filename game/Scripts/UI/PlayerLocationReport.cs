using System;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScctGame.UI;

/// <summary>Copyable local-player coordinates, facing and movement state for reproducing traversal problems.</summary>
public static class PlayerLocationReport
{
    public static string Format(string map, SBasePawn pawn, SPlayerController controller)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(controller);
        string side = pawn is SPAWNDEFENSE ? "merc" : "spy";
        var animation = pawn.PawnAnim[0];
        string netMode = pawn.XLevel?.Info.NetMode.ToString() ?? "unavailable";
        string specialMoveAge = controller.XLevel is { } level
            ? (level.TimeSeconds - controller.m_LastSpecialMoveTime).ToString("R", CultureInfo.InvariantCulture) : "unavailable";
        // CanUnCrouch only queries clearance; it does not change the stance, cylinder or position.
        bool canUnCrouch = pawn.CanUnCrouch();
        return $"map={map} side={side} state={controller.GetStateName()} physics={pawn.Physics} crouched={pawn.bIsCrouched.ToString().ToLowerInvariant()} fast={controller.bFastSpeed.ToString().ToLowerInvariant()}\n" +
            $"net_mode={netMode} pawn_role={pawn.Role} controller_role={controller.Role} controller_remote_role={controller.RemoteRole}\n" +
            $"location_uu={Vector(pawn.Location)}\n" +
            $"pawn_rotation_ue={Rotation(pawn.Rotation)} view_rotation_ue={Rotation(controller.Rotation)}\n" +
            $"velocity_uu={Vector(pawn.Velocity)} acceleration_uu={Vector(pawn.Acceleration)}\n" +
            string.Create(CultureInfo.InvariantCulture, $"action={pawn.sAction} animation={animation.Sequence} frame={animation.Frame:R} rate={animation.Rate:R} playing={animation.bPlaying} loop={animation.bLoop} notify_ignored={animation.bNotifyIgnore}\n") +
            $"actions_pending={pawn.iNbAction} action_table={pawn.iNbActionInTable} actions_sent={pawn.iNbActionsSent} animation_tick={pawn.PawnAnim.LastAdvanceTick} world_tick={pawn.XLevel?.TickCount}\n" +
            $"root_lock={pawn.NetworkRootMotion.RootLockType} weapon_transit={pawn.bWeaponTransit} rolling={pawn.bIsRolling} input_disabled={controller.iDontUpdatePadInput} waiting_for_reinit={controller.bWaitingForReinit} free_camera={controller.FreeCameraFlying}\n" +
            string.Create(CultureInfo.InvariantCulture, $"zone_config={pawn.ZoneConfig} zone_for_client={pawn.ZoneConfigForClient} collision_radius_uu={pawn.CollisionRadius:R} collision_half_height_uu={pawn.CollisionHeight:R} crouch_requested={controller.bCrouchWithRoll} can_uncrouch={canUnCrouch} last_special_age_s={specialMoveAge}\n") +
            $"view_forward={Vector(controller.Rotation.Vector())}";
    }

    // Round-trip format preserves every coordinate bit and always uses a decimal point, irrespective of locale.
    private static string Vector(FVector value) => string.Create(CultureInfo.InvariantCulture, $"({value.X:R},{value.Y:R},{value.Z:R})");

    private static string Rotation(FRotator value) =>
        string.Create(CultureInfo.InvariantCulture, $"(pitch={value.Pitch},yaw={value.Yaw},roll={value.Roll})");
}
