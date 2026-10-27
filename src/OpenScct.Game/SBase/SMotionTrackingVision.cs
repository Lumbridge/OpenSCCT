using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMotionTrackingVision: the merc's motion tracker (spec/equipment/motion-tracking.md). Local to the merc's
/// machine; twice a second it locks onto pawns and projectiles that move fast enough within range and in sight
/// (<see cref="GetActors"/>, the Reloaded binding). Every frame the merc's client projects each lock to a screen box
/// (<see cref="ProjectLocks"/>) and the HUD draws the brackets (<see cref="BuildHud"/>).
/// </summary>
public partial class SMotionTrackingVision
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_Inactive.Instance, s_Active.Instance);

    private static readonly Name NAME_s_UseObject_CamNetwork = new("s_UseObject_CamNetwork");
    private static readonly Name NAME_s_MultiSpectating = new("s_MultiSpectating");
    private static readonly Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = new("s_MultiSpectating_WhenKilled");

    private readonly List<FCheckResult> _hits = new(16);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Not tracking (the auto state).</summary>
    [Uc("SBase.SMotionTrackingVision.IsActive", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SMotionTrackingVision:IsActive", Spec = "spec/equipment/motion-tracking.md#rules")]
    public bool IsActive() => StateNode is s_Active;

    /// <summary>
    /// GetActors (native 1422, the Reloaded binding Reloaded.Core 0x100ca650; spec rule "GetActors"): every lock
    /// stale; the viewer point (the used camera in the camera network, the controller while spectating, else the
    /// pawn); pawns other than the viewer pawn and projectiles, not hidden, within MaxDetectedDistance plus their
    /// radius and at least MinDetectedVelocity, a cloaked spy skipped, in sight (<see cref="InSight"/>), are locked:
    /// a lock already held is marked fresh, a new one goes first in the list (StartTime now) and plays the detect
    /// sound at the viewer point. Then each stale lock starts its hold (EndTime now + KeepLockTime) or, once
    /// EndTime + OutTime has passed, is removed.
    /// </summary>
    [UcNative("ASMotionTrackingVision::execGetActors", Exe = 0x10ac0a30)]
    [PortOf("nat:SBase.SMotionTrackingVision.GetActors", Spec = "spec/equipment/motion-tracking.md#getactors")]
    [PortOf("rl:SBase.SMotionTrackingVision.GetActors", Spec = "spec/equipment/motion-tracking.md#getactors")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100ca650 (GetActors body), game 0x10ac0a30 and editor 0x11220b70 (stock)")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded's viewer switch (byte 0x1036ab97) lives in zero-filled data and is never written: the viewer is the pawn")]
    public void GetActors()
    {
        if (PlayerOwner is not { } owner || owner.Pawn is not { } viewer || XLevel is null || Level is null)
        {
            return;
        }

        for (int i = 0; i < LockedObjects.Count; i++)
        {
            FLockedObject l = LockedObjects[i];
            l.bRemove = true;
            LockedObjects[i] = l;
        }

        FVector v = ViewerPoint(owner, viewer);
        float now = Level.TimeSeconds;
        foreach (Actor a in XLevel.AllActors<Actor>())
        {
            if (a.bDeleteMe || a.bHidden)
            {
                continue;
            }

            float reach = MaxDetectedDistance + a.CollisionRadius;
            if ((a.Location - v).SizeSquared() >= reach * reach)
            {
                continue;
            }

            if (!(MinDetectedVelocity <= a.Velocity.Size()) || !IsCandidate(a, viewer) || !InSight(v, a))
            {
                continue;
            }

            int found = IndexOfLock(a);
            if (found >= 0)
            {
                FLockedObject l = LockedObjects[found];
                l.bRemove = false;
                LockedObjects[found] = l;
                continue;
            }

            // Inserted at the head (TArray Insert(0, 1)).
            LockedObjects.Insert(0, new FLockedObject { Actor = a, StartTime = now });
            PlayDetectSound(viewer, v);
            GadgetFx.Notify(this, GadgetFx.MotionLock, default, a.ActorId);
        }

        int n = 0;
        while (n < LockedObjects.Count)
        {
            FLockedObject l = LockedObjects[n];
            if (l.bRemove)
            {
                if (l.EndTime == 0f)
                {
                    l.EndTime = now + KeepLockTime;
                    LockedObjects[n] = l;
                }
                else if (now > l.EndTime + OutTime)
                {
                    LockedObjects.RemoveAt(n);
                    continue;
                }
            }

            n++;
        }
    }

    /// <summary>The lock on <paramref name="actor"/>, or -1 (a loop, not FindIndex: a lambda made a closure per actor per tick).</summary>
    private int IndexOfLock(Actor actor)
    {
        for (int i = 0; i < LockedObjects.Count; i++)
        {
            if (ReferenceEquals(LockedObjects[i].Actor, actor))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The point the tracker looks from: the used object's location while the owner runs the camera network
    /// (s_UseObject_CamNetwork), the controller's location while it spectates with a view target, else the pawn's.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "GetActors: state name 0x7ef with Pawn.UsedObject; names 0x193, 0x194, 0x1fe with ViewTarget")]
    public static FVector ViewerPoint(PlayerController Owner, Pawn Viewer)
    {
        Name state = Owner.GetStateName();
        if (state == NAME_s_UseObject_CamNetwork)
        {
            if (Viewer is SBasePawn { UsedObject: { } used })
            {
                return used.Location;
            }

            return Viewer.Location;
        }

        if (Owner.ViewTarget is not null &&
            (state == NAME_s_MultiSpectating || state == NAME_s_Spectating_Visitor || state == NAME_s_MultiSpectating_WhenKilled))
        {
            return Owner.Location;
        }

        return Viewer.Location;
    }

    /// <summary>
    /// The class rule: a spy pawn (the SPawnAttaque vtable, its one-mesh subclass shares it) whose DesiredGlassEffect
    /// is 255 is skipped (the cloak); then a projectile, or a pawn other than the viewer pawn, is a candidate.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded GetActors: vtable 0x10bf9ec8 (SPawnAttaque) and byte +0xab5 == 0xff, then IsA Projectile, IsA Pawn and not the viewer")]
    public static bool IsCandidate(Actor A, Pawn Viewer)
    {
        if (A is SPawnAttaque { DesiredGlassEffect: SMotionTrackingVisionK.CloakedGlass })
        {
            return false;
        }

        if (A is Projectile)
        {
            return true;
        }

        return A is Pawn && !ReferenceEquals(A, Viewer);
    }

    /// <summary>
    /// The Reloaded sight rule. First a line from the viewer point to the actor with the sight flags (0x20086: movers,
    /// level, level geometry, every hit): SingleLineCheck passes over the hits on bullet-passable actors (reported,
    /// skipped) and keeps the first other hit with the hits after it. Only when that finds nothing, the stock line
    /// (0x86, single result: a bullet-passable actor is an ordinary hit there). Then the hits are walked past actors
    /// seen through in motion vision (glass); walking off the end, or stopping on nothing or on the actor, is clear.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded GetActors: SingleLineCheck 0x10a1d200 with 0x20086, then 0x86; walk while Actor.bSeeThroughInMotionVision (+0x2f4 bit 0x100000)")]
    [Provenance(ProvenanceKind.Decoded, "SingleLineCheck 0x10a1d200: hits whose pass-through word is set are skipped; the word is set when the hit actor has bBulletGoTru (+0x2ec bit 0x100000) and the flags have 0x20000 (0x10a188c0)")]
    public bool InSight(FVector From, Actor A)
    {
        if (GadgetWorld.Collision(this) is not ICollisionWorld world)
        {
            return true;
        }

        FVector to = A.Location;
        WorldLine(world, From, to, SMotionTrackingVisionK.SightFlags);
        int first = -1;
        for (int i = 0; i < _hits.Count; i++)
        {
            if (!PassesBullets(world, _hits[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            // The stock line: single result, the nearest hit (pass-through is only marked with 0x20000).
            WorldLine(world, From, to, SMotionTrackingVisionK.StockSightFlags);
            if (_hits.Count > 1)
            {
                _hits.RemoveRange(1, _hits.Count - 1);
            }

            first = 0;
        }

        int k = first;
        while (k < _hits.Count && _hits[k].Actor is { bSeeThroughInMotionVision: true })
        {
            k++;
        }

        if (k >= _hits.Count)
        {
            return true;
        }

        // Every kept entry is a hit: only the actor itself lets the line through. The original's "no actor" case is
        // the no-hit result, which never reaches here; a level hit names the LevelInfo.
        return ReferenceEquals(_hits[k].Actor, A);
    }

    /// <summary>
    /// Every hit of a world line (movers, level, level geometry) in time order, into <see cref="_hits"/>. The engine's
    /// ShouldTrace gives a bWorldGeometry actor to the level geometry flag only (collision-traces.md rule 3): SVitre
    /// panes and other world-geometry gameplay meshes are collected through the actor flag and kept when
    /// bWorldGeometry, other actors' hits are dropped.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "collision-traces.md ShouldTrace rule 3: bWorldGeometry actors return Flags & 0x80; SVitre defaults bWorldGeometry=True")]
    private void WorldLine(ICollisionWorld world, FVector From, FVector To, ETraceFlags Flags) =>
        WorldLineHits(world, this, From, To, Flags, _hits);

    /// <summary>The hits a world line of <paramref name="Flags"/> reports in the original (see <see cref="WorldLine"/>).</summary>
    public static void WorldLineHits(ICollisionWorld world, Actor? Source, FVector From, FVector To, ETraceFlags Flags, List<FCheckResult> Hits)
    {
        world.MultiLineCheck(Hits, Source, To, From, FVector.Zero, Flags | ETraceFlags.TRACE_Others);
        for (int i = Hits.Count - 1; i >= 0; i--)
        {
            if (!WorldTraced(world, Hits[i]))
            {
                Hits.RemoveAt(i);
            }
        }
    }

    private static bool WorldTraced(ICollisionWorld world, in FCheckResult hit)
    {
        if (hit.Collider >= 0 && world is CollisionWorld cw && hit.Collider < cw.ColliderCount)
        {
            switch (cw.GetCollider(hit.Collider).Kind)
            {
                case OpenScct.Content.Collision.ColliderKind.WorldBsp:
                case OpenScct.Content.Collision.ColliderKind.Brush:
                case OpenScct.Content.Collision.ColliderKind.MeshPoly:
                case OpenScct.Content.Collision.ColliderKind.MeshHull:
                case OpenScct.Content.Collision.ColliderKind.Mover:
                    return true;
            }
        }

        return hit.Actor is { bWorldGeometry: true } or Mover or LevelInfo;
    }

    /// <summary>True when the hit is on an actor bullets pass (bBulletGoTru, or a collider made from one).</summary>
    private static bool PassesBullets(ICollisionWorld world, in FCheckResult hit)
    {
        if (hit.Actor is { bBulletGoTru: true })
        {
            return true;
        }

        return hit.Collider >= 0 && world is CollisionWorld cw && hit.Collider < cw.ColliderCount &&
               (cw.GetCollider(hit.Collider).Flags & OpenScct.Content.Collision.ColliderFlags.BulletGoesThrough) != 0;
    }

    /// <summary>
    /// The detect sound: played by the audio subsystem for the viewer pawn at the viewer point, volume 1, radius 64,
    /// pitch 1, no override (sound id 1).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "GetActors: Audio->PlaySound(viewer, 1, detectsound, &V, 1.0, 64.0, 1.0, 0x10, ...)")]
    private void PlayDetectSound(Pawn Viewer, FVector At)
    {
        // Played by the owner's audio subsystem, never sent: nobody hears a bot's tracker (SGoggle.OwnerHearsVision).
        if (detectsound.IsNone || XLevel is null || !SGoggle.OwnerHearsVision(PlayerOwner))
        {
            return;
        }

        XLevel.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.PlaySound,
            ActorId = Viewer.ActorId,
            What = new Name(detectsound.Path),
            Location = At,
            Rotation = Viewer.Rotation,
            Slot = (int)ESoundSlot.SLOT_None,
            Volume = 1f,
            Radius = SMotionTrackingVisionK.DetectSoundRadius,
            Pitch = 1f,
            NoOverride = true,
        });
    }

    /// <summary>Removes every lock.</summary>
    public void ClearLocks() => LockedObjects.Clear();

    /// <summary>The lock on <paramref name="A"/>, or -1.</summary>
    public int FindLock(Actor A) => LockedObjects.FindIndex(l => ReferenceEquals(l.Actor, A));

    /// <summary>s_Inactive (auto): no locks.</summary>
    [Uc("SBase.SMotionTrackingVision.s_Inactive")]
    public class s_Inactive : UcActorState
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMotionTrackingVision.s_Inactive.BeginState", Sha1 = "544e2808e53e36dd3ceb1129d790a83a5d1969e8")]
        [PortOf("uc:SBase.SMotionTrackingVision:s_Inactive.BeginState", Spec = "spec/equipment/motion-tracking.md#rules")]
        public override void BeginState(Actor self) => ((SMotionTrackingVision)self).ClearLocks();
    }

    /// <summary>s_Active: the locks cleared, then a scan every 0.5 s.</summary>
    [Uc("SBase.SMotionTrackingVision.s_Active")]
    public class s_Active : UcActorState
    {
        public static readonly s_Active Instance = new(UcMeta.s_Active, null);

        protected s_Active(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMotionTrackingVision.s_Active.BeginState", Sha1 = "c1472e48374d1e7c026f0a0a62b8cb6f1472ca7c")]
        [PortOf("uc:SBase.SMotionTrackingVision:s_Active.BeginState", Spec = "spec/equipment/motion-tracking.md#rules")]
        public override void BeginState(Actor self)
        {
            ((SMotionTrackingVision)self).ClearLocks();
            self.SetTimer(SMotionTrackingVisionK.ScanPeriod, true);
        }

        [Uc("SBase.SMotionTrackingVision.s_Active.Timer", Sha1 = "550e970df24445a93c68b8e4af46bb31f137c0a6")]
        [PortOf("uc:SBase.SMotionTrackingVision:s_Active.Timer", Spec = "spec/equipment/motion-tracking.md#rules")]
        public override void Timer(Actor self) => ((SMotionTrackingVision)self).GetActors();

        [Uc("SBase.SMotionTrackingVision.s_Active.IsActive", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SMotionTrackingVision:s_Active.IsActive", Spec = "spec/equipment/motion-tracking.md#rules")]
        public bool IsActive(SMotionTrackingVision self) => true;
    }
}
