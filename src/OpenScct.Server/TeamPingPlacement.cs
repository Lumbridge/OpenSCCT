using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Server;

/// <summary>Shared source and placement rules for temporary team annotations.</summary>
public static class TeamPingPlacement
{
    public const float TraceRange = 4000f;
    public const float FallbackRange = 1200f;
    private const float EndpointTolerance = 8f;
    private const float ChaseCameraTolerance = 300f;
    private const ETraceFlags PlacementTrace = ETraceFlags.TRACE_World | ETraceFlags.TRACE_VisibleSurface;

    public static bool TryGetTeam(SPlayerController? player, out byte team)
    {
        team = 0;
        if (player is not { bDeleteMe: false, bGameEnded: false } ||
            player.Pawn is not SBasePawn { Health: > 0, bDeleteMe: false } pawn ||
            !ReferenceEquals(pawn.Controller, player) || player.IsMercDeathMatch || player.IsSpectating() ||
            player.PlayerReplicationInfo is not { bDeleteMe: false, bOnlySpectator: false, Team.TeamIndex: 0 or 1 } pri)
        {
            return false;
        }

        team = (byte)pri.Team!.TeamIndex;
        return true;
    }

    public static bool IsAllowed(TeamPingKind kind, byte team) => team <= 1 && kind switch
    {
        TeamPingKind.Generic or TeamPingKind.Watch or TeamPingKind.Check => true,
        TeamPingKind.CoopBoost or TeamPingKind.Mine => team == 0,
        _ => false,
    };

    /// <summary>Returns the possessed pawn or live camera currently providing the player's view.</summary>
    public static bool TryGetSource(SPlayerController? player, out Actor source, out FVector origin)
    {
        source = null!;
        origin = default;
        if (player is null || !TryGetTeam(player, out byte team) || player.FreeCameraFlying != 0) return false;

        var pawn = (SBasePawn)player.Pawn!;
        Name state = player.GetStateName();
        PlayerCalcView.EViewSource viewSource = PlayerCalcView.SourceOf(player);
        if (viewSource is PlayerCalcView.EViewSource.StickyCam or PlayerCalcView.EViewSource.Mirador &&
            !ReferenceEquals(player.SBPawn, pawn)) return false;

        switch (viewSource)
        {
            case PlayerCalcView.EViewSource.StickyCam when state == GadgetStates.s_UseObject_CamNetwork:
                if (team != 1 || pawn.UsedObject is not SCamNetwork { bDeleteMe: false, IsShotDisabled: false } network)
                    return false;
                source = network;
                PlayerCalcView.StickyCamView(player, false, out _, out origin, out _);
                break;
            case PlayerCalcView.EViewSource.StickyCam:
                if (team != 0 || pawn.UsedObject is not SStickyCam { bDeleteMe: false } sticky ||
                    sticky is SCamNetwork || !ReferenceEquals(sticky.Owner, pawn) || ReferenceEquals(sticky, player.PredictedStickyCam))
                    return false;
                source = sticky;
                PlayerCalcView.StickyCamView(player, false, out _, out origin, out _);
                break;
            case PlayerCalcView.EViewSource.Mirador:
                if (team != 1 || pawn.UsedObject is not SProjoFixe { bDeleteMe: false } projector) return false;
                source = projector;
                PlayerCalcView.MiradorView(player, false, out _, out origin, out _);
                break;
            case PlayerCalcView.EViewSource.Default:
                if ((player.ViewTarget is { } view && !ReferenceEquals(view, pawn)) ||
                    state == GadgetStates.s_UseObject_CamNetworkCrWaitForServer ||
                    state == GadgetStates.s_UseObject_CamNetworkStWaitForServer ||
                    state == GadgetStates.s_UseObject_StickyCam_Leave || state == new Name("s_IntroCinematic"))
                    return false;
                source = pawn;
                origin = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
                break;
            default:
                return false;
        }

        return IsFinite(origin);
    }

    /// <summary>Projects the centre of the drawn camera through the shared collision world.</summary>
    public static bool TryProjectTarget(SPlayerController player, Actor source, FVector cameraLocation,
        FRotator cameraRotation, out FVector target)
    {
        target = default;
        if (!IsFinite(cameraLocation) || !TryGetSource(player, out Actor current, out _) ||
            !ReferenceEquals(current, source) || GadgetWorld.Collision(source) is not { } world) return false;

        FVector direction = cameraRotation.Vector();
        FVector end = cameraLocation + direction * TraceRange;
        target = world.LineCheck(out FCheckResult hit, source, end, cameraLocation, FVector.Zero, PlacementTrace) && hit.bHit
            ? hit.Location
            : cameraLocation + direction * FallbackRange;
        return IsFinite(target);
    }

    /// <summary>Validates the requested point against the current authority view and world geometry.</summary>
    public static bool TryValidateTarget(SPlayerController player, Actor source, FVector target)
    {
        // The drawn Spy camera can sit outside the pawn's eye even when it sees the same surface.
        float maxRange = TraceRange + (source is SBasePawn ? ChaseCameraTolerance : EndpointTolerance);
        if (!IsFinite(target) || !TryGetSource(player, out Actor current, out FVector origin) ||
            !ReferenceEquals(current, source) || GadgetWorld.Collision(source) is not { } world ||
            FVector.DistSquared(origin, target) > maxRange * maxRange)
        {
            return false;
        }

        return !world.LineCheck(out FCheckResult hit, source, target, origin, FVector.Zero, PlacementTrace) ||
            !hit.bHit || FVector.DistSquared(hit.Location, target) <= EndpointTolerance * EndpointTolerance;
    }

    private static bool IsFinite(FVector point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
