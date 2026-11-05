using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

public sealed class DestructibleSkinSmoke : SmokeTest
{
    public const string TestName = "destructible-skin";
    public override string Name => TestName;
    private OffscreenView? _view;
    private MeshFollower? _follower;
    private SGamePlayObject? _screen;
    private int _frame;
    private SpotShadowLight? _torch;
    private FVector _eye;
    private FRotator _look;
    public override SmokeResult Step(SmokeContext context)
    {
        if (_view is null)
        {
            string? folder = MapBuilder.FindMapFolder("AquaE", context.App.Options.MapRoots);
            if (folder is null) return SmokeResult.Fail("AquaE missing");
            var map = MapBuilder.Build(folder);
            var entry = map.Gameplay["SGamePlayObject2123"];
            // Original runtime SGamePlayObject9302: the local cooked sphere grows by max(DrawScale3D),
            // not the diagonal of its transformed AABB. This kiosk has scale (1.2,1,1).
            var scaled = map.Gameplay["SGamePlayObject9302"];
            var sphere = ActorLighting.SphereOf(scaled.Node.Mesh, scaled.Node.Transform);
            if (MathF.Abs(sphere.Radius - 217.1249237f) > .01f ||
                FVector.Dist(sphere.Center, new FVector(7666.3671875f, 1168.8742676f, 274.5310059f)) > .01f)
                return SmokeResult.Fail("scaled kiosk light sphere differs from original runtime");
            var world = new World(1, ENetMode.NM_Standalone);
            world.Level.BeginLoad();
            _screen = world.Level.SpawnActor(new SGamePlayObject
            {
                bDamageable = true, HitPoints = 1, StaticMesh = new AssetRef("Aquarium_STM.Aquarium.aqua_guide01"),
                DamagedMeshes = { new() { Percent = 100, Skin = new AssetRef("Aquarium_TXT.Aquarium.aqua_bornescreen1") } },
            }, entry.Location, default)!;
            world.Level.EndLoad();
            _follower = new MeshFollower(_screen, entry, mapHidden: false);
            _view = new OffscreenView(context.Tree.Root, map, new Vector2I(885, 865));
            var target = entry.Location + new FVector(0, 0, 10);
            var camera = target + new FVector(-330, 0, 0);
            _eye = camera; _look = (target-camera).Rotation();
            _view.View.SetUeCamera(camera, _look, 65, cut: true);
            return SmokeResult.Running;
        }
        _frame++;
        if (_frame == 15 || _frame == 30 || _frame == 45)
        {
            string folder = Path.GetFullPath("../local/destructible-skin");
            Directory.CreateDirectory(folder);
            using var shot = _view.Capture();
            if (_frame == 15)
            {
                double light = 0;
                int count = 0;
                for (int y = 660; y < 780; y++)
                    for (int x = 300; x < 580; x++)
                    {
                        Color pixel = shot.GetPixel(x, y);
                        light += pixel.R + pixel.G + pixel.B;
                        count++;
                    }
                GD.Print($"CABINET mean={light / (3 * count):F5}");
                if (light / (3 * count) < .025)
                    return SmokeResult.Fail("cabinet lower panel lost native fitted actor lighting");
            }
            shot.SavePng(Path.Combine(folder, _frame == 15 ? "intact.png" : _frame == 30 ? "broken.png" : "broken-torch.png"));
        }
        if (_frame == 15)
        {
            var mesh = _follower!.Entry.Node.Mesh;
            _screen!.ProcessDamage(1);
            _follower.OnTick();
            _follower.DrawSkin(_view.Map.Pack, 0, _view.Map);
            if (_screen.HitPoints != 0 || _screen.DrawType == EDrawType.DT_None || !ReferenceEquals(mesh, _follower.Entry.Node.Mesh))
                return SmokeResult.Fail("skin-only damage removed kiosk geometry");
            if (_follower.Entry.Node.GetSurfaceOverrideMaterial(0) is null)
                return SmokeResult.Fail("cracked glass skin not applied to slot zero");
            for (int i=1; i<mesh!.GetSurfaceCount(); i++)
                if (_follower.Entry.Node.GetSurfaceOverrideMaterial(i) is not null)
                    return SmokeResult.Fail("cracked glass replaced cabinet material");
        }
        if (_frame == 30)
        {
            _torch = new SpotShadowLight(_view.Viewport, _view.Map.Pack, SpotShadowShape.Torch);
            _torch.Place(_eye, _look);
            _torch.On = true;
        }
        if (_frame < 47) return SmokeResult.Running;
        _torch?.Dispose();
        _view.Dispose();
        return SmokeResult.Pass("AquaE intact cabinet retained; cracked material applied only to screen section");
    }
}


