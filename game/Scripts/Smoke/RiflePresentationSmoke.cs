using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScctGame.Fx;
using OpenScctGame.Render.Data;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Shot presentation across the input/tick boundary, with finite and infinite ammunition.</summary>
public sealed class RiflePresentationSmoke : SmokeTest
{
    public const string TestName = "rifle-presentation";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            int cases = 0;
            foreach (bool practice in new[] { false, true })
            foreach (SWeaponDefense weapon in new SWeaponDefense[] { new(), new SWeaponDefense_Uzi(), new SWeaponDefense_Shotgun() })
            {
                var world = new World(1, ENetMode.NM_Standalone);
                world.Level.SetService(new PracticeRules { On = practice });
                var pawn = world.Level.SpawnActor(new SPawnDefense_1Mesh(), new FVector(1000, 2000, 300), default)!;
                var pc = world.Level.SpawnActor(new SPlayerController(), pawn.Location, default)!;
                pc.Possess(pawn);
                world.Level.SpawnActor(weapon, pawn.Location, default);
                weapon.Instigator = pawn;
                weapon.SetOwner(pawn);
                weapon.GiveTo(pawn);
                var fx = new CombatFxView();
                context.Tree.Root.AddChild(fx);
                try
                {
                    int ammo = weapon.ClipAmmo;
                    // The first shot happens before the view has seen this weapon; later shots are batched.
                    foreach (int rounds in new[] { 1, 3 })
                    {
                        world.Step();
                        int sounds = 0;
                        for (int shot = 0; shot < rounds; shot++)
                        {
                            world.Step((int)MathF.Ceiling(weapon.RateOfFire * Sim.Hz) + 2); // No flash collection between shots.
                            weapon.FireWeapon();
                            foreach (SimEvent e in world.Events.Late)
                                if (e.Kind == SimEventKind.PlaySound && e.What.ToString() == weapon.FireSingleShotSound.Path)
                                    sounds++;
                        }
                        Require(sounds == rounds, $"{weapon.GetType().Name}/{practice}: {sounds} sounds for {rounds} rounds");
                        int before = fx.MuzzleFlashes;
                        world.Step(); // Drops the input command's SpawnSFX notify, as real play does.
                        fx.TrackRifleFlashes(weapon);
                        fx.Draw(0f, pawn.Location, default, pc);
                        Require(fx.MuzzleFlashes == before + rounds, "lost muzzle flash across tick boundary");
                        fx.TrackRifleFlashes(weapon);
                        fx.Draw(0f, pawn.Location, default, pc);
                        Require(fx.MuzzleFlashes == before + rounds, "duplicate muzzle flash on repeated collection");
                    }
                    Require(weapon.ClipAmmo == ammo - (practice ? 0 : 4), "incorrect ammunition consumption");
                    int flashes = fx.MuzzleFlashes;
                    fx.TrackRifleFlashes(null);
                    fx.TrackRifleFlashes(weapon); // Switching away and back must not replay old shots.
                    weapon.ClipAmmo = weapon.ClipMaxAmmo;
                    fx.TrackRifleFlashes(weapon);
                    world.IsReplaying = true;
                    weapon.SpawnSFX();
                    world.IsReplaying = false;
                    fx.TrackRifleFlashes(weapon);
                    fx.Draw(0f, pawn.Location, default, pc);
                    Require(fx.MuzzleFlashes == flashes, "reload or replay produced an extra flash");
                    cases++;
                }
                finally { fx.Free(); }
            }
            return SmokeResult.Pass($"{cases} weapon/ammo cases: first and batched shots each have one sound/flash; no duplicate, reload or replay flash");
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

/// <summary>Tracer flight alignment and near-impact hiding using the actual first-person muzzle and renderer.</summary>
public sealed class RifleTracerDiagnosticSmoke : SmokeTest
{
    public const string TestName = "rifle-tracer-diagnostic";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        var world = new World(1, ENetMode.NM_Standalone);
        var level = world.Level;
        var pawn = level.SpawnActor(new SPawnDefense_1Mesh(), new FVector(1000, 2000, 300), default)!;
        var pc = level.SpawnActor(new SPlayerController(), pawn.Location, default)!;
        pc.Possess(pawn);
        var weapon = level.SpawnActor(new SWeaponDefense(), pawn.Location, default)!;
        weapon.Instigator = pawn;
        weapon.SetOwner(pawn);
        weapon.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        pc.bBehindView = false;
        pc.ViewTarget = pawn;
        var view = new FirstPersonView();
        context.Tree.Root.AddChild(view);
        var rows = new List<string>();
        try
        {
            world.Step(6);
            FVector eye = pawn.Location + new FVector(0, 0, 60);
            foreach (float distance in new[] { 20f, 40f, 80f, 150f, 5000f })
            foreach (int yaw in new[] { 0, 16384 })
            foreach (float move in new[] { 0f, 30f })
            {
                var rotation = new FRotator(0, yaw, 0);
                view.OnTick(pc);
                view.Draw(pc, eye + new FVector(0, move, 0), rotation, 0f);
                if (view.Model is not { } model || !view.Shown) return SmokeResult.Fail("missing first-person rifle");
                FVector muzzle = UeSpace.ToUe((model.Skeleton.GlobalTransform * model.CurrentBonePose(model.Skeleton.FindBone("FX"))).Origin);
                var trace = level.SpawnActor(new SFireTrace(), eye, default)!;
                trace.Instigator = pawn;
                trace.ImpactLoc = eye + new FVector(distance, 0, 0);
                trace.eventPostNetBeginPlay();
                var fx = new CombatFxView { FirstPerson = view };
                context.Tree.Root.AddChild(fx);
                try
                {
                    fx.TrackTracers(level, pc);
                    MeshInstance3D node = fx.GetChildren().OfType<MeshInstance3D>().Last();
                    FVector direction = UeSpace.ToUe(-node.GlobalBasis.Z).SafeNormal();
                    FVector expected = (trace.ImpactLoc - muzzle).SafeNormal();
                    if (FVector.Dist(direction, expected) > .001f) return SmokeResult.Fail("tracer mesh is not aligned with its flight");
                    float length = FVector.Dist(trace.ImpactLoc, muzzle);
                    fx.Draw(0f, eye, rotation, pc);
                    if (node.Visible != (length >= 502f)) return SmokeResult.Fail("incorrect initial near-impact visibility");
                    bool visible = node.Visible;
                    if (length > 503f)
                    {
                        fx.Draw((length - 503f) / trace.FTSpeed, eye, rotation, pc);
                        if (!node.Visible) return SmokeResult.Fail("tracer hidden before the original cutoff");
                        fx.Draw(2f / trace.FTSpeed, eye, rotation, pc);
                        if (node.Visible) return SmokeResult.Fail("tracer visible inside the original cutoff");
                    }
                    float angle = MathF.Acos(Math.Clamp(expected.X, -1f, 1f)) * 180f / MathF.PI;
                    rows.Add($"range={distance}, turn={yaw * 360f / 65536f}, strafe={move}: flight-angle-to-shot={angle:F2} deg, visible={visible}, muzzle={muzzle}, impact={trace.ImpactLoc}");
                }
                finally { fx.Free(); trace.Destroy(); }
            }
            string path = System.IO.Path.GetFullPath("../local/rifle-tracer-diagnostic.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllLines(path, rows);
            return SmokeResult.Pass($"{rows.Count} real-muzzle cases: mesh aligned with flight, near hits hidden, far tracers hide at 502 uu; geometry: {path}");
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
        finally { view.Free(); }
    }
}
