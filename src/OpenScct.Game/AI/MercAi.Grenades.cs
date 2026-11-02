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

    /// <summary>A low lob using the actual full-tap launch speed, rejected when the grenade would hit cover en route.</summary>
    private bool GrenadeAim(FVector target, out FRotator aim)
    {
        const float gravity = 750f;
        float speed = 2010f; // Frag: 2 * ProjectileSpeed (900) + the native launch bonus (210).
        if (GadgetSlot<SGadFragGrenade>(out SGadFragGrenade? grenade) >= 0 && grenade?.ProjectileDefaults is { } defaults)
        {
            speed = 2f * defaults.ProjectileSpeed + Equipment.K.GrenadeLaunchBonus;
        }

        FVector start = AiSight.Eye(Pawn);
        aim = default;
        float flight = 0f;
        // Recompute once from the rifle's offset release point, instead of assuming an eye-centred projectile.
        for (int pass = 0; pass < 2; pass++)
        {
            FVector to = target - start;
            float flat = NavFollower.Horizontal(to);
            float squared = speed * speed;
            float discriminant = squared * squared - gravity * (gravity * flat * flat + 2f * to.Z * squared);
            if (flat < 1f || discriminant <= 0f) return false;
            float pitch = SvmMath.Atan2(squared - SvmMath.Sqrt(discriminant), gravity * flat);
            aim = new FRotator((int)(pitch * (32768f / MathF.PI)), NavFollower.YawTo(start, target), 0);
            flight = flat / (speed * SvmMath.Cos(pitch));
            if (pass == 0) start = CombatNatives.MercThrowFxCoords(Pawn, aim).Origin + aim.Vector() * 40f;
        }

        FVector velocity = aim.Vector() * speed;
        FVector previous = start;
        int steps = Math.Max(1, (int)MathF.Ceiling(flight / 0.04f));
        for (int i = 1; i <= steps; i++)
        {
            float time = flight * i / steps;
            FVector point = start + velocity * time - new FVector(0f, 0f, 0.5f * gravity * time * time);
            if (World.Collision.LineCheck(out _, Pawn, point, previous, new FVector(5f, 5f, 5f),
                ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers)) return false;
            previous = point;
        }

        return true;
    }
}
