using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Smoke;

/// <summary>The authored SteeE gas-cylinder, can and Molotov particles: trigger, motion, atlas animation, floor collision and burn lifetime.</summary>
public sealed class DestructibleFxSmoke : SmokeTest
{
    public const string TestName = "destructible-fx";

    public override string Name => TestName;

    private OffscreenView? _preview;
    private string _previewFolder = "";
    private string _checks = "";
    private int _shot;
    private int _frames;
    private static readonly (string Name, string Emitter, float Time, FVector Camera, FVector Target)[] Shots =
    [
        ("gas-blast", "Emitter251", .22f, new(-780, 1000, -990), new(-1080, 1034, -1085)),
        ("gas-smoke", "Emitter251", .8f, new(-780, 1000, -990), new(-1080, 1034, -1085)),
        ("can-blast", "Emitter748", .15f, new(-1350, 560, -760), new(-1502, 771, -850)),
        ("can-debris", "Emitter748", .6f, new(-1350, 560, -760), new(-1502, 771, -850)),
        ("bottle-blast", "Emitter169", .2f, new(-1550, -1280, -600), new(-1750, -1110, -760)),
        ("bottle-fire", "Emitter169", 3f, new(-1550, -1280, -600), new(-1750, -1110, -760)),
    ];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_preview is not null) return PreviewStep();
        string? folder = MapBuilder.FindMapFolder("SteeE", context.App.Options.MapRoots);
        if (folder is null)
        {
            return SmokeResult.Fail("SteeE content missing");
        }

        RenderMap map = MapBuilder.Build(folder);
        try
        {
            if (map.Emitters is not { } emitters || emitters.SimulationOf("Emitter251") is not { } gas ||
                emitters.SimulationOf("Emitter169") is not { } bottle || emitters.SimulationOf("Emitter748") is not { } can)
            {
                return SmokeResult.Fail("gas-cylinder, can or Molotov emitter missing");
            }

            emitters.SetTime(0.25f);
            if (gas.Alive != 0 || bottle.Alive != 0 || can.Alive != 0)
            {
                return SmokeResult.Fail("destruction effects ran before the props broke");
            }

            emitters.TriggerActor("Emitter251");
            emitters.TriggerActor("Emitter169");
            emitters.TriggerActor("Emitter748");
            emitters.SetTime(0.5f);
            if (gas.Alive == 0 || bottle.Alive == 0 || bottle.Emitters[0].Alive != 0 || can.Emitters[2].Alive != 1)
            {
                return SmokeResult.Fail("blast or delayed fire timing incorrect");
            }

            Particle shrapnel = gas.Emitters[0].Particles[0];
            Particle canDebris = can.Emitters[2].Particles[0];
            if (can.Emitters[2].Params.Kind != ParticleKind.Mesh ||
                !map.ParticleMeshes.ContainsKey(can.Emitters[2].Params.StaticMesh ?? ""))
            {
                return SmokeResult.Fail("can debris mesh missing");
            }
            var firstCell = new SpriteQuad();
            ParticleGeometry.Cells(gas.Emitters[1].Params, gas.Emitters[1].Particles[0], ref firstCell);
            emitters.SetTime(0.8f);
            var laterCell = new SpriteQuad();
            ParticleGeometry.Cells(gas.Emitters[1].Params, gas.Emitters[1].Particles[0], ref laterCell);
            if (FVector.Dist(shrapnel.Location, gas.Emitters[0].Particles[0].Location) < 1f ||
                FVector.Dist(canDebris.Location, can.Emitters[2].Particles[0].Location) < 1f ||
                (firstCell.U0 == laterCell.U0 && firstCell.V0 == laterCell.V0))
            {
                return SmokeResult.Fail("blast particles or atlas frames did not animate");
            }

            emitters.SetTime(1.8f);
            int collisions = 0;
            foreach (Particle particle in bottle.Emitters[2].Particles)
            {
                collisions += particle.HitCount;
            }

            if (collisions == 0)
            {
                return SmokeResult.Fail("Molotov particles passed through the floor");
            }

            emitters.SetTime(15f);
            if (gas.Alive != 0 || can.Alive != 0 || bottle.Emitters[0].Alive == 0)
            {
                return SmokeResult.Fail("gas blast and persistent Molotov fire lost their distinct lifetimes");
            }

            emitters.SetTime(34f);
            if (bottle.Alive != 0)
            {
                return SmokeResult.Fail("Molotov fire did not expire");
            }

            _checks = $"gas motion/atlas=ok can mesh/motion/expiry=ok Molotov delay/burn/expiry=ok floor collisions={collisions}";
            if (System.Environment.GetEnvironmentVariable("SVM_DESTRUCTIBLE_FX_PREVIEW") is { Length: > 0 } output &&
                DisplayServer.GetName() != "headless")
            {
                _previewFolder = Path.GetFullPath(output);
                Directory.CreateDirectory(_previewFolder);
                foreach (string id in new[] { "SGamePlayObject368", "SGamePlayObject747", "SGamePlayObject215" })
                    if (map.Gameplay.TryGetValue(id, out GameplayMesh? prop)) prop.Node.Visible = false;
                _preview = new OffscreenView(context.Tree.Root, map, new Vector2I(1280, 720));
                return SmokeResult.Running;
            }

            return SmokeResult.Pass(_checks);
        }
        finally
        {
            if (_preview is null) map.Root.Free();
        }
    }

    private SmokeResult PreviewStep()
    {
        OffscreenView preview = _preview!;
        var shot = Shots[_shot];
        if (_frames++ == 0)
        {
            preview.Map.SetTime(0f);
            preview.View.SetUeCamera(shot.Camera, (shot.Target - shot.Camera).Rotation(), 90f, cut: true);
            preview.Map.Emitters!.TriggerActor(shot.Emitter);
            preview.Map.SetTime(shot.Time);
            return SmokeResult.Running;
        }

        if (_frames < 6) return SmokeResult.Running;
        using Image image = preview.Capture();
        Error saved = image.SavePng(Path.Combine(_previewFolder, shot.Name + ".png"));
        if (saved != Error.Ok) return SmokeResult.Fail("preview save: " + saved);
        _frames = 0;
        if (++_shot < Shots.Length) return SmokeResult.Running;
        preview.Dispose();
        _preview = null;
        return SmokeResult.Pass(_checks + "; previews=" + _previewFolder);
    }
}
