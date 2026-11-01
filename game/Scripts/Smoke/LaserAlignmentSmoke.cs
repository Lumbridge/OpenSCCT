using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;
using OpenScctGame.Fx;

namespace OpenScctGame.Smoke;

/// <summary>Neutral sight zero and barrel attachment through idle and reload, using every merc rig.</summary>
public sealed class LaserAlignmentSmoke : SmokeTest
{
    public const string TestName = "laser-alignment";
    public override string Name => TestName;

    private sealed class Wall : IHitZones
    {
        public FVector Normal;
        public float Distance;
        public readonly List<FVector> Shots = new();
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire)
        {
            FVector delta = end - start;
            float divisor = delta | Normal;
            float t = MathF.Abs(divisor) > .00001f ? (Distance - (start | Normal)) / divisor : -1f;
            bool hit = t >= 0f && t <= 1.00001f;
            FVector point = hit ? start + delta * Math.Clamp(t, 0f, 1f) : end;
            if (realFire) Shots.Add(point);
            // Record real shots without producing impacts or damage; sight traces still stop at the plane.
            return new BoneTraceResult { Actor = shooter.XLevel.Info, bHit = hit && !realFire,
                HitLocation = point, HitNormal = -Normal };
        }
    }

    public override SmokeResult Step(SmokeContext context)
    {
        if (CheckBeamAnimation() is { } animationFailure) return SmokeResult.Fail(animationFailure);
        var world = new World(1, ENetMode.NM_Standalone);
        var level = world.Level;
        var pawn = level.SpawnActor(new SPawnDefense_1Mesh(), new FVector(100, 200, 300), default)!;
        var pc = level.SpawnActor(new SPlayerController(), pawn.Location, default)!;
        pc.Possess(pawn);
        var wall = new Wall();
        level.SetService<IHitZones>(wall);
        var weapons = new List<SWeaponDefense> { new SWeaponDefense(), new SWeaponDefense_Uzi(), new SWeaponDefense_Shotgun() };
        for (int kind = 0; kind < Sidearms.All.Count; kind++) weapons.Add(new MercSidearm { Kind = kind });
        float worst = 0f;
        int cases = 0, reloadFrames = 0;
        foreach (var weapon in weapons)
        {
            level.SpawnActor(weapon, pawn.Location, default);
            weapon.Instigator = pawn;
            weapon.SetOwner(pawn);
            weapon.GiveTo(pawn);
            pawn.MyWeapon = weapon;
            if (weapon is MercSidearm pistol) pistol.Configure(true);
            string asset = weapon.Mesh.Path!;
            string? path = SkeletalModel.FindGlb(asset);
            if (path is null) return SmokeResult.Fail("missing " + asset);
            var model = SkeletalModel.Load(path);
            context.Tree.Root.AddChild(model.Root);
            try
            {
                int fx = model.Skeleton.FindBone("FX");
                if (fx < 0 || model.Sampler.Find("FirstWaitStFd2") is not { } idle ||
                    model.Sampler.Find(weapon.AFirstReloSt.ToString()) is not { } reload)
                    return SmokeResult.Fail(asset + " missing muzzle/idle/reload");
                foreach (bool crouched in new[] { false, true })
                foreach (int pitch in new[] { -3500, 0, 2500 })
                foreach (float distance in new[] { 150f, 500f, 5000f })
                {
                    pawn.bIsCrouched = crouched;
                    pawn.EyeHeight = crouched ? 40f : 60f;
                    var aim = new FRotator(pitch, 9000, 0);
                    pc.Rotation = pc.MyDesiredRotation = weapon.AccurateRotation = aim;
                    var ray = weapon.AimRay(aim);
                    wall.Normal = aim.Vector();
                    wall.Distance = (ray.Start | wall.Normal) + distance;
                    model.Place(ray.Start, aim, default);
                    Transform3D idleReference = model.Skeleton.GlobalTransform * model.Sampler.SampleBone(idle, fx, .5f);
                    Vector3? idleDirection = null;
                    foreach (float frame in new[] { 0f, .5f, .95f })
                    {
                        Transform3D barrel = model.Skeleton.GlobalTransform * model.Sampler.SampleBone(idle, fx, frame);
                        wall.Shots.Clear();
                        weapon.TraceFire(0f, 0f, 0f);
                        if (wall.Shots.Count == 0) return SmokeResult.Fail(asset + " did not trace a shot");
                        var beam = MercLaserView.TraceMountedBeam(weapon, barrel, idleReference, aim);
                        FVector direction = (beam.End - beam.Start).SafeNormal();
                        Vector3 local = barrel.Basis.Orthonormalized().Inverse() * UeSpace.DirectionToGodot(direction);
                        if (idleDirection is { } expected && local.DistanceTo(expected) > .001f)
                            return SmokeResult.Fail(asset + " laser slid relative to barrel during idle");
                        idleDirection ??= local;
                        float pullBack = weapon is MercSidearm ? 0f : OpenScct.Game.Equipment.K.LaserFireStartPullBack;
                        if (FVector.Dist(beam.Start + direction * pullBack, UeSpace.ToUe(barrel.Origin)) > .01f)
                            return SmokeResult.Fail(asset + " idle laser detached from muzzle");
                        // Only the calibrated neutral pose is zeroed to the shot; breathing moves the laser with the gun.
                        if (frame != .5f) continue;
                        float error = FVector.Dist(wall.Shots[0], beam.End);
                        worst = MathF.Max(worst, error);
                        if (error > .1f) return SmokeResult.Fail($"{asset} aim mismatch {error:F3} uu at {distance}, pitch {pitch}");
                        cases++;
                    }
                }
                var rotation = new FRotator(0, 9000, 0);
                var centre = weapon.AimRay(rotation);
                model.Place(centre.Start, rotation, default);
                wall.Normal = rotation.Vector(); wall.Distance = (centre.Start | wall.Normal) + 1000f;
                Transform3D reference = model.Skeleton.GlobalTransform * model.Sampler.SampleBone(idle, fx, .5f);
                Vector3? mountedDirection = null;
                FVector? firstDirection = null;
                float sweep = 0f;
                for (int frame = 0; frame <= 30; frame++)
                {
                    Transform3D barrel = model.Skeleton.GlobalTransform * model.Sampler.SampleBone(reload, fx, frame / 30f);
                    var beam = MercLaserView.TraceMountedBeam(weapon, barrel, reference, rotation);
                    FVector direction = (beam.End - beam.Start).SafeNormal();
                    Vector3 local = barrel.Basis.Orthonormalized().Inverse() * UeSpace.DirectionToGodot(direction);
                    if (mountedDirection is { } expected && local.DistanceTo(expected) > .001f)
                        return SmokeResult.Fail(asset + " laser slid relative to barrel during reload");
                    mountedDirection ??= local;
                    firstDirection ??= direction;
                    sweep = MathF.Max(sweep, FVector.Dist(firstDirection.Value, direction));
                    float pullBack = weapon is MercSidearm ? 0f : OpenScct.Game.Equipment.K.LaserFireStartPullBack;
                    if (FVector.Dist(beam.Start + direction * pullBack, UeSpace.ToUe(barrel.Origin)) > .01f)
                        return SmokeResult.Fail(asset + " laser detached from muzzle");
                    reloadFrames++;
                }
                if (sweep < .05f) return SmokeResult.Fail(asset + " reload laser stayed static");
                if (CheckDrawnFrame(context, pawn, pc, weapon, wall) is { } frameFailure)
                    return SmokeResult.Fail(asset + " " + frameFailure);
                GD.Print($"SVM_LASER {asset}: 18 neutral aim cases, 54 idle frames, 31 reload frames passed; sweep {sweep:F3}");
            }
            finally { model.Root.Free(); }
        }
        var spy = level.SpawnActor(new SPawnAttaque_1Mesh(), new FVector(100, 200, 300), default)!;
        var spyPc = level.SpawnActor(new SPlayerController(), spy.Location, default)!;
        spyPc.Possess(spy);
        var shocker = level.SpawnActor(new SStickyShocker(), spy.Location, default)!;
        shocker.GiveTo(spy);
        foreach (bool crouched in new[] { false, true })
        foreach (int pitch in new[] { -3500, 0, 2500 })
        foreach (float distance in new[] { 150f, 500f, 5000f })
        foreach (bool otherHand in new[] { false, true })
        {
            spy.bIsCrouched = crouched;
            spy.WeaponLoaded = (byte)(otherHand ? 7 : 2);
            spyPc.Rotation = new FRotator(pitch, 9000, 0);
            FCoords frame = CombatServices.Ports(shocker).BoneCoords(shocker, SWeaponDefense.FxBone);
            wall.Normal = frame.XAxis;
            wall.Distance = (frame.Origin | wall.Normal) + distance;
            wall.Shots.Clear();
            shocker.TraceFireWithPos(frame.Origin, frame.XAxis.Rotation(), 0f, 0f, 0f);
            if (wall.Shots.Count == 0) return SmokeResult.Fail("shocker did not trace a shot");
            // Include the independently animated hand's position error, including the left-hand offset.
            FVector drawnStart = SWeaponAttaque.LaserStartOn(frame, otherHand) + new FVector(2, -3, 1);
            var beam = OpenScctGame.Fx.SpyLaserView.TraceReadyBeam(shocker, drawnStart);
            float error = FVector.Dist(wall.Shots[0], beam.Hit);
            worst = MathF.Max(worst, error);
            if (error > .1f) return SmokeResult.Fail($"shocker aim mismatch {error:F3} uu");
            cases++;
        }
        return SmokeResult.Pass($"{weapons.Count + 1} guns, {cases} shot comparisons, {reloadFrames} reload frames; worst aim error {worst:F4} uu");
    }

    private static string? CheckBeamAnimation()
    {
        using var thermal = SpyLaserView.ThermalBeamPass(null);
        using var body = new StandardMaterial3D { NextPass = thermal };
        ulong bodyId = body.GetInstanceId(), thermalId = thermal.GetInstanceId();
        foreach ((float time, float expected) in new[] { (0f, 0f), (5f, 0.75f), (19.9f, 0.005f), (20f, 0f), (20.1f, 0.995f), (25f, 0.75f) })
        {
            SpyLaserView.AnimateBeam(body, time);
            Vector2 mask = thermal.GetShaderParameter("uv_offset").AsVector2();
            if (MathF.Abs(body.Uv1Offset.X - expected) > 0.00001f || body.Uv1Offset.Y != 0f ||
                mask.DistanceTo(new Vector2(body.Uv1Offset.X, 0f)) > 0.00001f)
                return $"laser colour and thermal mask do not share the original 20-second U pan at {time}";
            if (body.GetInstanceId() != bodyId || body.NextPass.GetInstanceId() != thermalId)
                return "laser animation recreated a material";
        }
        GD.Print("SVM_LASER pan: negative U, 20-second wrap, thermal mask in phase, material instances retained");
        return null;
    }

    private static string? CheckDrawnFrame(SmokeContext context, SBasePawn pawn, SPlayerController pc, SWeaponDefense weapon, Wall wall)
    {
        var firstPerson = new FirstPersonView();
        var lasers = new MercLaserView { FirstPerson = firstPerson };
        context.Tree.Root.AddChild(firstPerson);
        context.Tree.Root.AddChild(lasers);
        var laser = pawn.XLevel.SpawnActor(new SGadTorcheLaser(), pawn.Location, default)!;
        laser.Instigator = pawn;
        laser.bInUse = true;
        pawn.WeaponLoaded = 2;
        weapon.bDrawWeapon = true;
        pc.bBehindView = false;
        pc.ViewTarget = pawn;
        for (int c = 0; c < ViewModelAnim.ChannelCount; c++) weapon.ViewAnim.SetAlpha(c, 0f);
        weapon.ViewAnim.Loop(0, ViewModelClips.FirstWaitStFd2);
        weapon.ViewAnim.Loop(SWeaponDefense.MVTCHANNEL, ViewModelClips.FirstJumpStFd2);
        weapon.ViewAnim.SetAlpha(SWeaponDefense.MVTCHANNEL, 1f);
        FVector origin = pawn.Location + new FVector(0, 0, 60);
        var rotation = new FRotator(0, 9000, 0);
        wall.Normal = rotation.Vector();
        wall.Distance = (origin | wall.Normal) + 1000f;
        float jumpAngle = 0f;
        try
        {
            for (int frame = 0; frame < 24; frame++)
            {
                FVector eye = origin + new FVector(frame * 2f, frame * 3f, frame);
                var turn = new FRotator(0, rotation.Yaw + frame * 20, 0);
                firstPerson.Draw(pc, eye, turn, 1f / 60f);
                lasers.Draw(pawn.XLevel, pawn, (eye, turn));
                if (!firstPerson.Shown || !lasers.LocalVisible || firstPerson.Model is not { } drawn)
                    return "drawn laser fixture did not show weapon/beam";
                int fx = drawn.Skeleton.FindBone("FX");
                FVector muzzle = UeSpace.ToUe((drawn.Skeleton.GlobalTransform * drawn.CurrentBonePose(fx)).Origin);
                FVector direction = (lasers.LocalEnd - lasers.LocalStart).SafeNormal();
                float pullBack = weapon is MercSidearm ? 0f : OpenScct.Game.Equipment.K.LaserFireStartPullBack;
                if (FVector.Dist(lasers.LocalStart + direction * pullBack, muzzle) > .01f)
                    return "beam sampled a stale muzzle during jump";
                jumpAngle = MathF.Max(jumpAngle, FVector.Dist(direction, turn.Vector()));
                FVector start = lasers.LocalStart, end = lasers.LocalEnd;
                FVector saved = pawn.Location;
                FRotator savedAim = pc.MyDesiredRotation;
                // Between simulation ticks the drawn camera/pose are authoritative for the visual beam.
                pawn.Location += new FVector(45, -30, 15);
                pc.MyDesiredRotation = new FRotator(1600, turn.Yaw + 2000, 0);
                lasers.Draw(pawn.XLevel, pawn, (eye, turn));
                pawn.Location = saved;
                pc.MyDesiredRotation = savedAim;
                if (FVector.Dist(start, lasers.LocalStart) > .01f || FVector.Dist(end, lasers.LocalEnd) > .01f)
                    return "beam mixed simulation eye/aim with the drawn camera";
            }
            if (weapon is not MercSidearm && jumpAngle < .025f) return "jump animation left the laser pinned to camera centre";
            GD.Print($"SVM_LASER drawn-frame jump sweep={jumpAngle:F3}; stale simulation eye/aim has no effect");
            return null;
        }
        finally
        {
            laser.bInUse = false;
            firstPerson.Free();
            lasers.Free();
        }
    }
}


