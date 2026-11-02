using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>Visible map security: route around its coverage, or stop and shoot its emitter with the normal shocker.</summary>
public sealed class SpySecurity
{
    private readonly List<SBasicGamePlayObject> _known = new();
    private float _observeAt;
    private float _drawAt = -10f;
    private float _shotAt = -10f;
    private SBasePawn? _pawn;
    private SBasicGamePlayObject? _engaging;
    private float _engageAt;
    private readonly Dictionary<SBasicGamePlayObject, float> _avoidUntil = new();
    public bool Engaging => _engaging is not null;
    public bool Handling { get; private set; }

    public void Observe(AiController bot, float now)
    {
        Handling = false;
        if (!ReferenceEquals(_pawn, bot.Pawn))
        {
            _known.Clear();
            _avoidUntil.Clear();
            _engaging = null;
            _pawn = bot.Pawn;
            _observeAt = 0f;
        }
        if (now < _observeAt || bot.Pawn is not { } pawn) return;
        _observeAt = now + .3f;
        FVector eye = AiSight.Eye(pawn);
        foreach (SBasicGamePlayObject device in bot.World.Level.AllActors<SBasicGamePlayObject>())
        {
            if (device is not (SLaserTrigger or SCamera) || device.bDeleteMe || _known.Contains(device)) continue;
            FVector d = device.Location - eye;
            if (d.SizeSquared() > 1600f * 1600f || d.SafeNormal().Dot(bot.Controller.Rotation.Vector()) < .1f ||
                !bot.World.LineOpen(eye, device.Location)) continue;
            _known.Add(device);
            // A newly seen beam or camera can invalidate the route chosen before it was visible.
            bot.Brain?.Follower.Clear();
        }
        _known.RemoveAll(d => d.bDeleteMe);
    }

    public static bool Active(SBasicGamePlayObject device) => !device.bDeleteMe && device switch
    {
        SLaserTrigger laser => laser.IsInState(new Name("s_Active")) && !laser.bIsBroken,
        SCamera camera => camera.IsInState(new Name("s_Patrol")),
        _ => false,
    };

    /// <summary>Conservative body clearance from a beam, and the camera's real rectangular yaw/pitch cone.</summary>
    public static bool Covers(SBasicGamePlayObject device, FVector center, float radius, float height)
    {
        if (!Active(device)) return false;
        if (device is SLaserTrigger laser)
        {
            FVector a = laser.Location;
            FVector delta = laser.LaserEndLocation - a;
            float length = delta.SizeSquared();
            float t = length > 0f ? Math.Clamp((center - a).Dot(delta) / length, 0f, 1f) : 0f;
            FVector separation = center - (a + delta * t);
            return separation.X * separation.X + separation.Y * separation.Y <= radius * radius && MathF.Abs(separation.Z) <= height;
        }
        if (device is SCamera camera)
        {
            FVector d = center - camera.Location;
            if (d.Size() >= camera.VisibilityMaxDistance) return false;
            FRotator aim = d.Rotation();
            float cone = camera.VisibilityConeAngle * SCamera.UnitsPerDegree;
            return Math.Abs(FRotator.NormalizeAxis(aim.Yaw - camera.Rotation.Yaw)) < cone &&
                Math.Abs(FRotator.NormalizeAxis(aim.Pitch - camera.Rotation.Pitch)) < cone;
        }
        return false;
    }

    public float Penalty(AiController bot, FVector floor)
    {
        float cost = 0f;
        foreach (SBasicGamePlayObject device in _known)
        {
            FVector center = floor + new FVector(0, 0, AiK.SpyCrouchHeight);
            if (Covers(device, center, AiK.PawnRadius + 40f, AiK.SpyCrouchHeight) &&
                (device is SLaserTrigger || bot.World.LineOpen(device.Location, center)))
            {
                if (_avoidUntil.TryGetValue(device, out float until) && bot.World.Now < until) return float.PositiveInfinity;
                cost += 150f;
            }
        }
        return cost;
    }

    public bool Steer(AiController bot, float now, ref AiPlan plan)
    {
        if (bot.Pawn is not SPawnAttaque { Health: > 0, bSleeping: false, bLyingOnGround: false, bStickyShocking: false } pawn ||
            pawn.Physics != EPhysics.PHYS_Walking || pawn.HandItem is not null) return false;
        int yaw = plan.MoveYaw ?? plan.Yaw ?? bot.Controller.Rotation.Yaw;
        FVector direction = new FRotator(0, yaw, 0).Vector();
        // Keep the target while turning/drawing; the new heading need not still cross its coverage.
        SBasicGamePlayObject? threat = _engaging is { } current && Active(current) ? current : null;
        foreach (SBasicGamePlayObject device in _known)
        {
            if (threat is not null) break;
            if (_avoidUntil.TryGetValue(device, out float until) && now < until) continue;
            for (int step = 0; step <= (plan.Keys.Moving ? 6 : 0); step++)
            {
                FVector center = pawn.Location + direction * (step * 30f);
                if (!Covers(device, center, pawn.CollisionRadius + 12f, pawn.CollisionHeight) ||
                    device is SCamera && (pawn.DesiredGlassEffect == SCamera.CloakedGlass || !bot.World.LineOpen(device.Location, center))) continue;
                threat = device;
                break;
            }
            if (threat is not null) break;
        }
        if (threat is null)
        {
            _engaging = null;
            return false;
        }
        if (!ReferenceEquals(_engaging, threat))
        {
            _engaging = threat;
            _engageAt = now;
        }
        // Stop before entering coverage; draw, aim and fire through the same state/input gates as a human.
        Handling = true;
        plan.Keys = default;
        plan.MoveYaw = null;
        plan.Crouched = null;
        FVector aimPoint = threat.Location;
        FVector origin = pawn.MyWeapon?.GetFireStart() ?? AiSight.Eye(pawn);
        FRotator shotRotation = bot.Controller.Rotation;
        if (pawn.MyWeapon is SWeaponAttaque pistol)
        {
            FCoords muzzle = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
            shotRotation = muzzle.XAxis.Rotation();
            origin = SWeaponAttaque.GetAxesAndLocation(muzzle.Origin, shotRotation, out _, out _, out _);
        }
        FRotator ray = (aimPoint - origin).Rotation();
        // The pistol's pitch only follows part of the view pitch. Correct the actual ray error,
        // rather than assuming the model's muzzle points exactly along the controller's view.
        FRotator aim = new(bot.Controller.Rotation.Pitch + FRotator.NormalizeAxis(ray.Pitch - shotRotation.Pitch),
            bot.Controller.Rotation.Yaw + FRotator.NormalizeAxis(ray.Yaw - shotRotation.Yaw), 0);
        plan.Yaw = aim.Yaw;
        plan.Pitch = aim.Pitch;
        SPlayerController pc = bot.Controller;
        if (!bot.World.LineOpen(origin, aimPoint) || now - _engageAt > 6f)
        {
            // No usable shot, or repeated misses: exclude this coverage briefly and let navigation
            // find another route/objective. Never hold the spy facing an unreachable emitter forever.
            _avoidUntil[threat] = now + 15f;
            _engaging = null;
            bot.Brain?.Follower.Clear();
            return true;
        }
        if (pawn.WeaponLoaded == 0 && !pawn.bWeaponTransit && now - _drawAt > 1f)
        {
            _drawAt = now;
            plan.Keys.bFireMode = 1;
        }
        string state = pc.GetStateName().ToString();
        if (pawn.WeaponLoaded == 2 && !pawn.bWeaponTransit && !state.Contains("0HTo2H", StringComparison.Ordinal) &&
            !state.Contains("2HTo0H", StringComparison.Ordinal) && pawn.EnergyLevel >= SpyAiK.ShotEnergy &&
            now - _shotAt > 1f && NavFollower.YawError(pc.Rotation.Yaw, aim.Yaw) < 40 &&
            Math.Abs(FRotator.NormalizeAxis(pc.Rotation.Pitch - aim.Pitch)) < 40)
        {
            _shotAt = now;
            plan.Keys.bFire = 1;
            plan.Keys.Press(AiCommand.Fire);
        }
        return true;
    }
}
