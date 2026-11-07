using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;

namespace OpenScct.Server.Hub;

public sealed partial class HubServer
{
    /// <summary>Collision-checked hub relocation, shared by travel and checkpoint recovery. Foot is a deck position.</summary>
    internal bool TryPlace(PlayerSlot slot, FVector foot, int yaw)
    {
        if (slot.Controller is not SPlayerController { Pawn: SBasePawn pawn } pc || pawn.Health <= 0) return false;
        var location = foot + new FVector(0, 0, Math.Max(pawn.CollisionHeight, 86) + 8);
        // Never place into another visitor, including a crouched or airborne one.
        foreach (var player in Host.Players)
        {
            if (ReferenceEquals(player, slot) || player.Controller?.Pawn is not { Health: > 0 } other) continue;
            var delta = location - other.Location;
            float radius = pawn.CollisionRadius + other.CollisionRadius + 8;
            if (Math.Abs(delta.Z) < 180 && delta.X * delta.X + delta.Y * delta.Y < radius * radius) return false;
        }
        if (World.Level.PhysicsDriver is not OpenScct.Physics.Movement.PhysicsDriver physics || !physics.FarMoveActor(pawn, location)) return false;
        physics.GetState(pawn).TraversalBase = null;
        pawn.SetBase(null);
        pawn.Velocity = pawn.Acceleration = FVector.Zero;
        pawn.Rotation = new FRotator(0, yaw, 0);
        pc.SetViewRotation(pawn.Rotation);
        pc.MyDesiredRotation = pc.LocalMyDesiredRotation = pawn.Rotation;
        pawn.bLyingOnGround = false;
        pawn.bIsRolling = false;
        pc.bCrouch = 0;
        pc.bPressedJump = false;
        pc.eventMirrorState(new Name(pawn.SPawnType == PawnType.SPAWNOFFENSE ? "s_PlayerWalkingSt0H" : "s_PlayerWalkingSt2H"));
        Host.Movement.EndpointOf(pc)?.Server?.ReinitPawn();
        pawn.DistantClientReinit(0);
        return true;
    }
}

public sealed partial class HubActivities
{
    private void Travel(PlayerSlot slot, PlayState state, string id)
    {
        if (!Layout.TeleportDestinations.TryGetValue(id, out var destination))
        {
            Send(slot, HubPlayProtocol.Toast("That destination is unavailable."));
            return;
        }
        if (Now - state.TravelAt < 2)
        {
            Send(slot, HubPlayProtocol.Toast("Please wait a moment before travelling again."));
            return;
        }
        bool placed = false;
        foreach (var arrival in destination.Arrivals)
        {
            if (!_hub.TryPlace(slot, arrival, destination.Yaw)) continue;
            placed = true;
            break;
        }
        if (!placed)
        {
            Send(slot, HubPlayProtocol.Toast("The arrival area is busy or blocked. Try again shortly."));
            return;
        }
        state.TravelAt = Now;
        if (state.Run is not null) CancelRun(slot, state, "Run cancelled: travelled to " + destination.Title + ".");
        state.InStart = null;
        foreach (var round in _rounds.ToArray())
            if (ReferenceEquals(round.Owner, slot)) EndRound(round, cancelled: true, reason: "Round cancelled: travel.");
        foreach (var player in _hub.Players)
        {
            if (!ReferenceEquals(player.Slot, slot)) continue;
            _hub.Pads?.Left(player);
            player.ReturnPad = null;
        }
        Send(slot, HubPlayProtocol.Toast("Arrived: " + destination.Title));
    }
}
