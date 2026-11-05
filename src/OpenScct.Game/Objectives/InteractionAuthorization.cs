using System;
using System.Runtime.CompilerServices;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Objectives;

/// <summary>Server-owned interaction sessions. RPC arguments identify a request, never permission to use an actor.</summary>
internal static class InteractionAuthorization
{
    private sealed class Session(SPlayerController player, STriggerInteraction entry)
    {
        public readonly SPlayerController Player = player;
        public readonly Pawn Pawn = player.Pawn!;
        public readonly STriggerInteraction Entry = entry;
        public readonly float Created = player.XLevel.WorldTimeSeconds;
        public bool Started;
        public bool HoldStarted;
        public float EarliestCompletion;
    }

    private static readonly ConditionalWeakTable<SPlayerController, Session> Players = new();
    private static readonly ConditionalWeakTable<SInteractObject, Session> Entries = new();

    public static bool Begin(SPlayerController pc, SInteractObject? entry, int index)
    {
        // All world/mate entries have one Init action. SubStart/Success/Hold are later phases, never menu indices.
        if (index != 0 || entry is null || !CanReach(pc, entry) ||
            (!pc.eventCanInteract() && !pc.IsInState(new Name("s_InteractWithObjectWaitingEvent")))) return false;
        if (entry is SMateInteraction && !ReferenceEquals(entry.Owner, pc)) return false;
        if (entry is SLeaveInteraction or SShareObjectInteraction) return false;

        if (Entries.TryGetValue(entry, out Session? reserved))
        {
            if (IsCurrent(reserved) && (reserved.Started || pc.XLevel.WorldTimeSeconds - reserved.Created < 10f))
                return false;
            Cancel(reserved);
        }
        if (!entry.CanUse(pc)) return false;

        if (entry is STriggerInteraction { bHackable: true } or STriggerInteraction { bMaintain: true })
        {
            if (Players.TryGetValue(pc, out Session? previous)) Cancel(previous);
            var session = new Session(pc, (STriggerInteraction)entry);
            Players.Add(pc, session);
            Entries.Add(entry, session);
        }
        return true;
    }

    public static bool Start(SPlayerController pc, SInteractObject? entry, int index)
    {
        if (index != K.SubStart || !TrySession(pc, entry, out Session session) || session.Started || !IsCurrent(session))
            return false;
        if (!CanStartInState(session)) return false;
        if (entry is not STriggerInteraction { Owner: SInterrupteur sw } || entry.bInUse) return false;
        session.Started = true;
        session.EarliestCompletion = pc.XLevel.WorldTimeSeconds + Math.Max(0, sw.iHackingTime);
        return true;
    }

    public static bool Finish(SPlayerController pc, SInteractObject? entry, int index)
    {
        if (!TrySession(pc, entry, out Session session)) return false;
        if (index == K.SubFailed)
        {
            // The owner may cancel after dying or moving away, but cannot cancel someone else's session.
            bool dispatch = session.Started && (session.Entry.bHackable || session.HoldStarted);
            Forget(session);
            if (!dispatch && session.Started) session.Entry.GotoState(Name.None);
            return dispatch;
        }
        if (!session.Started || !IsCurrent(session)) return false;
        if (index == K.SubHoldStarted && session.Entry.bMaintain && !session.Entry.bHackable && !session.HoldStarted)
        {
            // A remote hold changes to Use on the client only; the authority legitimately remains in Enter.
            if (!pc.IsInState("s_Maintain_Enter") && !pc.IsInState("s_Maintain_Use")) return false;
            session.HoldStarted = true;
            return true;
        }
        if (index != K.SubSuccess || !session.Entry.bHackable ||
            (!pc.IsInState("s_Hack_Use") && !pc.IsInState("s_Hack_Leave")) ||
            pc.XLevel.WorldTimeSeconds + 0.001f < session.EarliestCompletion) return false;
        Forget(session);
        return true;
    }

    private static bool TrySession(SPlayerController pc, SInteractObject? entry, out Session session)
    {
        if (Players.TryGetValue(pc, out Session? found) && ReferenceEquals(found.Entry, entry) && !found.Entry.bDeleteMe)
        {
            session = found;
            return true;
        }
        session = null!;
        return false;
    }

    private static bool IsCurrent(Session session) => ReferenceEquals(session.Player.Pawn, session.Pawn) &&
        session.Entry.bUsable && ObjectiveNatives.UsableByAllows(session.Entry.UsableBy, ObjectiveNatives.PawnTypeOf(session.Player)) &&
        (!session.Started || InWorkflow(session)) &&
        CanReach(session.Player, session.Entry);

    private static bool CanStartInState(Session session) => session.Entry.bHackable
        ? session.Player.GetStateName().ToString() is "s_Hack_GoTo" or "s_Hack_Enter" or "s_Hack_Use"
        : session.Player.GetStateName().ToString() is "s_Maintain_GoTo" or "s_Maintain_Enter" or "s_Maintain_Use";

    private static bool InWorkflow(Session session) => CanStartInState(session) ||
        session.Player.IsInState(session.Entry.bHackable ? "s_Hack_Leave" : "s_Maintain_Leave");

    private static bool CanReach(SPlayerController pc, SInteractObject entry)
    {
        if (!ObjectiveNatives.IsAlive(pc) || pc.Pawn is SBasePawn { bLyingOnGround: true } or SBasePawn { bStickyShocking: true } ||
            entry.bDeleteMe || entry.Owner is { bDeleteMe: true } ||
            !ReferenceEquals(entry.XLevel, pc.XLevel) || !ReferenceEquals(pc.Pawn!.Controller, pc)) return false;
        // Mate CanUse performs its own server search, including team, distance, facing and occlusion.
        if (entry is SMateInteraction) return ReferenceEquals(entry.Owner, pc);
        Pawn pawn = pc.Pawn!;
        FVector delta = entry.Location - pawn.Location;
        float radius = Math.Max(entry.CollisionRadius, entry.ItRadius) + pawn.CollisionRadius;
        float height = Math.Max(entry.CollisionHeight, entry.ItHeight) + pawn.CollisionHeight;
        if (!(delta.X * delta.X + delta.Y * delta.Y <= radius * radius) || !(Math.Abs(delta.Z) <= height)) return false;
        // We need the nearest obstruction: a BSP surface behind an attached panel must not mask the panel hit.
        var flags = GadgetWorld.SightFlags & ~OpenScct.Physics.Collision.ETraceFlags.TRACE_StopAtFirstHit;
        // AquaE's wall medkit origins sit about one unit inside the mounting BSP. Test the exposed front
        // of the station instead of tracing into its mounting wall; intervening walls still block use.
        FVector sightTarget = entry.Location;
        if (entry is SInfirmaryInteraction or SAmmoStockInteraction)
            sightTarget += entry.Rotation.Vector() * K.StationSightFrontOffset;
        return !GadgetWorld.Line(pawn, sightTarget, pawn.Location, flags, out var hit) ||
            ReferenceEquals(hit.Actor, entry) || ReferenceEquals(hit.Actor, entry.Owner);
    }

    private static void Forget(Session session)
    {
        Players.Remove(session.Player);
        Entries.Remove(session.Entry);
    }

    private static void Cancel(Session session)
    {
        Forget(session);
        if (!session.Entry.bDeleteMe && session.Started)
        {
            if (session.Entry.bHackable || session.HoldStarted) session.Entry.eventPostInteract(session.Player, K.SubFailed);
            else session.Entry.GotoState(Name.None);
        }
    }
}
