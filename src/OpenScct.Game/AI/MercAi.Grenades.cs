using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

public sealed partial class MercAi
{
    private bool GrenadeOpportunity(AiContact? contact, float now, out FVector target)
    {
        target = default;
        if (contact is null || Difficulty.GadgetUse <= 0f || now - _fragAt <= 10f ||
            contact.Source is not (AiContactSource.Seen or AiContactSource.Told) ||
            now - contact.Time >= 4f || contact.Confidence <= 0.45f || contact.Pose is { Downed: true } ||
            contact.Visible && contact.Velocity.SizeSquared() > 180f * 180f ||
            GadgetSlot<SGadFragGrenade>(out SGadFragGrenade? grenade) < 0 || grenade is not { Charge: > 0 } ||
            !GrenadeWeaponReady())
        {
            return false;
        }

        int floor = Follower.Graph.NearestNode(contact.Position, 0f, 300f);
        if (floor < 0) return false;
        // Aim to land near the observed/reported position, rather than passing its chest and bouncing beyond it.
        target = new FVector(contact.Position.X, contact.Position.Y, Follower.Graph.Node(floor).Floor.Z + 8f);
        float range = (target - Pawn.Location).Size();
        return range is > 600f and < 1600f && GrenadeBlastClear(target) && GrenadeAim(target, out _);
    }

    private bool GrenadeWeaponReady() => Pawn.WeaponLoaded != 0 && !Pawn.bWeaponTransit &&
        !Pawn.bSleeping && !Pawn.bLyingOnGround && !Pawn.bStickyShocking && !Pawn.bGasMask &&
        Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) &&
        Pawn.MyWeapon is { } weapon && !weapon.IsInState(NameReloading) &&
        !weapon.IsInState(NameFullReloading) && !weapon.IsInState(NameFireGrenade) &&
        !weapon.IsInState(NameElbowShocking);

    private bool GrenadeBlastClear(FVector target)
    {
        if ((target - Pawn.Location).SizeSquared() < 550f * 550f) return false;
        foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (!ReferenceEquals(mate, Pawn) && !mate.bDeleteMe && mate.Health > 0 &&
                (mate.Location - target).SizeSquared() < 550f * 550f) return false;
        }

        return true;
    }

    private const float FragGravity = 750f;
    private const ETraceFlags FragArcTrace = ETraceFlags.TRACE_World | ETraceFlags.TRACE_Others | ETraceFlags.TRACE_OnlyProjActor;
    private static readonly float[] ObjectiveArcOffsets = [0f, -100f, 100f, -200f, 200f];
    private static readonly (float Forward, float Height)[] ObjectiveArcs = [(0f, 12f), (110f, 100f), (110f, 180f), (110f, 55f)];
    private static readonly (float Forward, float Height)[] ElevatedArcs =
        [(0f, 100f), (0f, 195f), (-130f, 80f), (-130f, 165f), (230f, 75f), (230f, 155f), (230f, 250f)];

    // The sweep of arcs for a frag at a hacked terminal. It ran whole in one decision (up to 55 arcs, each a
    // flight traced through the level: 40 ms); now AdvanceObjectiveArcs traces a few arcs a tick, as the tick's
    // budget allows, and the decision takes what the sweep found.
    private SComputerObjectiveTrigger? _arcAlarm;
    private int _arcIndex = -1;
    private FVector _arcUseFloor;
    private FVector _arcFound;
    private float _arcFoundAt = -100f;
    private SComputerObjectiveTrigger? _arcFoundFor;

    /// <summary>A found arc is offered to the decisions of this long (s); the throw traces it again before the key.</summary>
    private const float ArcFoundSeconds = 1f;

    private bool ObjectiveGrenadeOpportunity(SComputerObjectiveTrigger alarm, float now, out FVector target)
    {
        target = default;
        if (now - _fragAt <= 10f || Bot.Utility <= 0f ||
            !GrenadeWeaponReady() || GadgetSlot<SGadFragGrenade>(out var grenade) < 0 || grenade is not { Charge: > 0 } ||
            World.Teams[1].ObjectiveFragReserved(alarm, Bot, now))
        {
            _arcIndex = -1;
            return false;
        }

        if (ReferenceEquals(_arcFoundFor, alarm) && now - _arcFoundAt < ArcFoundSeconds)
        {
            target = _arcFound;
            _fragObjectivePoint = _arcUseFloor + new FVector(0, 0, 70f);
            return true;
        }

        // A sweep for this alarm is under way, or the next one is not due yet.
        if ((_arcIndex >= 0 && ReferenceEquals(_arcAlarm, alarm)) || now < _nextObjectiveFrag) return false;
        _nextObjectiveFrag = now + 1.1f * Bot.Personality.Tempo;
        _arcIndex = -1;
        if (!UseSpot(alarm, out FVector useFloor, out _)) return false;
        FVector usePoint = useFloor + new FVector(0, 0, 70f);
        FVector from = usePoint - Feet;
        float range = from.Size();
        if (range is < 490f or > 2450f ||
            MathF.Abs(from.Z) < 130f && range < 950f && World.LineOpen(AiSight.Eye(Pawn), usePoint)) return false;
        _arcAlarm = alarm;
        _arcUseFloor = useFloor;
        _arcIndex = 0;
        return false;
    }

    /// <summary>
    /// Every tick: traces the next arcs of the sweep under way while the tick's budget lasts. The first whose blast
    /// reaches the use spot is kept for the next decision (<see cref="ObjectiveGrenadeOpportunity"/>).
    /// </summary>
    private void AdvanceObjectiveArcs(float now)
    {
        if (_arcIndex < 0) return;
        if (_target is not null || !ReferenceEquals(_alert, _arcAlarm))
        {
            _arcIndex = -1;
            return;
        }

        // Try the hack spot and its near side. Different heights and side offsets send the round
        // through different apertures, while every accepted blast must still reach the use spot.
        FVector usePoint = _arcUseFloor + new FVector(0, 0, 70f);
        FVector from = usePoint - Feet;
        FVector direction = new FVector(from.X, from.Y, 0).SafeNormal();
        FVector side = new(-direction.Y, direction.X, 0);
        int low = ObjectiveArcOffsets.Length * ObjectiveArcs.Length;
        int all = low + (from.Z < 250f ? 0 : ObjectiveArcOffsets.Length * ElevatedArcs.Length);
        while (_arcIndex < all && !World.BudgetSpent)
        {
            int index = _arcIndex++;
            (float Forward, float Height)[] arcs = index < low ? ObjectiveArcs : ElevatedArcs;
            int within = index < low ? index : index - low;
            float lateral = ObjectiveArcOffsets[within / arcs.Length];
            (float forward, float height) = arcs[within % arcs.Length];
            FVector candidate = _arcUseFloor + side * lateral - direction * forward + new FVector(0, 0, height);
            if (!TryObjectiveArc(candidate, usePoint, out _)) continue;
            _arcFound = candidate;
            _arcFoundAt = now;
            _arcFoundFor = _arcAlarm;
            _arcIndex = -1;
            return;
        }

        if (_arcIndex >= all) _arcIndex = -1;
    }

    private bool TryObjectiveArc(FVector target, FVector usePoint, out FRotator aim)
    {
        aim = default;
        if (GadgetSlot<SGadFragGrenade>(out var grenade) < 0 || grenade?.ProjectileDefaults is not SProjFrag frag ||
            !SolveFragAim(target, frag, out aim, out FVector position, out FVector velocity, out _)) return false;

        // Carry the arc through the fuse. A round clipping the outside wall, rolling too far,
        // or landing behind a second partition does not make the objective a valid target.
        const float step = .04f;
        var gravity = new FVector(0, 0, -FragGravity);
        var extent = new FVector(frag.CollisionRadius + 1f, frag.CollisionRadius + 1f, frag.CollisionHeight + 1f);
        for (float elapsed = 0; elapsed < frag.TempsExplosion - .001f; elapsed += step)
        {
            FVector next = position + (velocity + gravity * (.5f * step)) * step;
            velocity += gravity * step;
            World.ChargeWork(AiK.SweptTraceWork);
            if (!World.Collision.LineCheck(out var hit, frag, next, position, extent, FragArcTrace))
            {
                position = next;
                continue;
            }

            if (hit.bStartPenetrating) return false;
            position = hit.Location + hit.Normal * 1.5f;
            if (hit.Actor is null or LevelInfo &&
                !World.Collision.LineCheck(out _, frag, position + velocity, position, default, ETraceFlags.TRACE_World)) break;
            velocity = (velocity - 2f * (velocity | hit.Normal) * hit.Normal) * frag.WallReboundFactor;
            if (velocity.Z > Equipment.K.GrenadeUpBounceCap)
                velocity.Z = (velocity.Z + Equipment.K.GrenadeUpBounceCap) * .5f;
            if (velocity.SizeSquared() < 20f * 20f && hit.Normal.Z > .7f) break;
        }

        float range = MathF.Min(frag.Range * .7f, 340f);
        return (position - usePoint).SizeSquared() < range * range &&
            World.LineOpen(position, usePoint) && GrenadeBlastClear(position);
    }

    /// <summary>A low lob using the actual full-tap launch speed, rejected when the grenade would hit cover en route.</summary>
    private bool GrenadeAim(FVector target, out FRotator aim)
    {
        GadgetSlot<SGadFragGrenade>(out var grenade);
        if (!SolveFragAim(target, grenade?.ProjectileDefaults, out aim, out FVector start, out FVector velocity, out float flight))
            return false;

        FVector previous = start;
        int steps = Math.Max(1, (int)MathF.Ceiling(flight / 0.04f));
        for (int i = 1; i <= steps; i++)
        {
            float time = flight * i / steps;
            FVector point = start + velocity * time - new FVector(0f, 0f, 0.5f * FragGravity * time * time);
            if (World.Collision.LineCheck(out _, Pawn, point, previous, new FVector(5f, 5f, 5f),
                ETraceFlags.TRACE_World)) return false;
            previous = point;
        }

        return true;
    }

    private bool SolveFragAim(FVector target, SProjGrenade? grenade, out FRotator aim, out FVector start,
        out FVector velocity, out float flight)
    {
        float speed = 2f * (grenade?.ProjectileSpeed ?? 900f) + Equipment.K.GrenadeLaunchBonus;
        start = AiSight.Eye(Pawn);
        aim = default;
        velocity = default;
        flight = 0f;
        for (int pass = 0; pass < 3; pass++)
        {
            FVector to = target - start;
            float flat = NavFollower.Horizontal(to);
            float squared = speed * speed;
            float discriminant = squared * squared - FragGravity * (FragGravity * flat * flat + 2f * to.Z * squared);
            if (flat < 1f || discriminant <= 0f) return false;
            float pitch = SvmMath.Atan2(squared - SvmMath.Sqrt(discriminant), FragGravity * flat);
            aim = new FRotator((int)(pitch * (32768f / MathF.PI)), NavFollower.YawTo(start, target), 0);
            flight = flat / (speed * SvmMath.Cos(pitch));
            start = CombatNatives.MercThrowFxCoords(Pawn, aim).Origin + aim.Vector() * 40f;
        }

        velocity = aim.Vector() * speed;
        return true;
    }
}
