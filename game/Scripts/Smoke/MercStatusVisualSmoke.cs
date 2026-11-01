using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScctGame.Fx;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Real skinned meshes and pixel comparisons for the merc's protection edge and worn mask.</summary>
public sealed class MercStatusVisualSmoke : SmokeTest
{
    public const string TestName = "merc-status-visual";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private Camera3D _camera = null!;
    private DisplaySpaceEffect _display = null!;
    private SkeletalModel _model = null!;
    private SimPawnView _view = null!;
    private MercShockImmunityView _cue = null!;
    private EffectViews _effects = null!;
    private SBasePawn _pawn = null!;
    private SGasMask _mask = null!;
    private MeshInstance3D _wall = null!;
    private int _stage, _frames;
    private string _folder = "";
    private Image? _standing, _crouched, _maskOff, _occluded;
    private float _standingHeight;
    private SmokeResult? _final;
    private int _teardown;
    private readonly string[] _names = ["stand-off", "stand-full", "stand-half", "stand-quarter", "stand-final-sliver",
        "crouch-off", "crouch-full", "crouch-half", "walk-half", "thermal-half", "emf-half", "mask-off", "mask-on",
        "first-person", "dead", "wall-off", "wall-full"];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_final is { } done) return ++_teardown < 6 ? SmokeResult.Running : done;
        if (_viewport is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a real renderer");
            string? glb = SkeletalModel.FindGlb("SPerso.DEF_01");
            if (glb is null) return SmokeResult.Fail("converted merc character is required");
            Build(context);
            Configure();
        }
        if (++_frames < 10) return SmokeResult.Running;
        // The draw uses the same pose as the rendered skeleton and the actual attachment path.
        _effects.Draw(0f, default, default, MaterialCompiler.Vision);
        if (_effects.MeshOf(_mask) is { } maskMesh)
            ActorLighting.Apply(maskMesh, new ActorLightSet { Ambient = new Vector3(0.5f, 0.5f, 0.5f) });
        if (_frames < 14) return SmokeResult.Running;
        Image image = _viewport!.GetTexture().GetImage();
        image.SavePng(Path.Combine(_folder, _names[_stage] + ".png"));
        string? failure = Check(image);
        if (failure is not null) return Finish(SmokeResult.Fail(_names[_stage] + ": " + failure));
        GD.Print($"SVM_MERC_STATUS {_names[_stage]} coverage={_cue.Coverage:F3} feet={_cue.FeetY:F3} head={_cue.HeadY:F3} mask={_effects.MeshOf(_mask)?.Visible}");
        if (++_stage == _names.Length)
            return Finish(SmokeResult.Pass($"{_names.Length} rendered cases: head-to-feet drain, posed crouch/walk, vision, mask, first-person and wall occlusion; {_folder}"));
        _frames = 0;
        Configure();
        return SmokeResult.Running;
    }

    private void Build(SmokeContext context)
    {
        _folder = Path.GetFullPath(System.Environment.GetEnvironmentVariable("SVM_MERC_STATUS_SHOTS") ??
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "local", "merc-status-visual"));
        Directory.CreateDirectory(_folder);
        _viewport = new SubViewport { Size = new Vector2I(480, 640), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
        context.Tree.Root.AddChild(_viewport);
        _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.5f, Current = true,
            Near = 0.01f, Far = 100, CullMask = RenderLayers.MainView, Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(), Compositor = new Compositor
            { CompositorEffects = [_display] } };
        _viewport.AddChild(_camera);
        var world = new World(1);
        _pawn = world.Level.SpawnActor(new SPawnDefense_1Mesh(), default, default)!;
        _pawn.Health = 100;
        var pawns = new PawnViews { SharedRoot = SkeletalModel.DefaultSharedRoot() };
        _viewport.AddChild(pawns);
        pawns.OnTick(world.Level);
        _view = pawns.ViewOf(_pawn)!;
        _model = _view.Model!;
        _cue = _view.ShockImmunity!;
        _model.Place(default, default, default);
        _model.Sampler.Play("WaitStSpFd2");
        _model.Pose();
        _model.Light(new ActorLightSet { Ambient = new Vector3(0.5f, 0.5f, 0.5f) });
        _cue.Draw(new ShockImmunityView(true, false, 3f, 1f), 0f, true);
        _standingHeight = _cue.HeadY - _cue.FeetY;
        Vector3 target = new(0, (_cue.FeetY + _cue.HeadY) * 0.5f, 0);
        _camera.Position = target + new Vector3(3f, 0, 3f);
        _camera.LookAt(target);
        _mask = world.Level.SpawnActor(new SGasMask(), default, default, _pawn, _pawn)!;
        ObjectiveNatives.AttachToBone(_pawn, _mask, new Name("B Head"));
        _effects = new EffectViews { Library = FxLibrary.Load(SkeletalModel.DefaultSharedRoot()), Pawns = pawns };
        _viewport.AddChild(_effects);
        _effects.OnTick(world.Level);
        _wall = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4, 4, 0.1f) }, Visible = false,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.11f, 0.13f, 0.16f) }, Position = target + new Vector3(0.7f, 0, 0.7f) };
        _viewport.AddChild(_wall);
        _wall.LookAt(_camera.Position);
    }

    private void Configure()
    {
        MaterialCompiler.SetVision(_stage == 9 ? RenderK.RendMapThermal : _stage == 10 ? RenderK.RendMapEmf : RenderK.RendMapNormal);
        _pawn.Health = _stage == 14 ? 0 : 100;
        _pawn.bGasMask = _stage is 12 or 13 or 14;
        _view.SetShadowOnly(_stage == 13);
        _wall.Visible = _stage >= 15;
        string clip = _stage is >= 5 and <= 7 ? "WalkCrSpFd2" : _stage == 8 ? "WalkStSpFd2" : "WaitStSpFd2";
        if (!_model.Sampler.Play(clip, frame: 0.3f)) throw new InvalidOperationException("Missing fixture clip " + clip);
        _model.Pose();
        float fraction = _stage switch { 2 or 7 or 8 or 9 or 10 => 0.5f, 3 => 0.25f, 4 => 0f, _ => 1f };
        bool active = _stage is not (0 or 5 or 11 or 12 or 14 or 15);
        _cue.Draw(new ShockImmunityView(active, _stage == 6, fraction * 3f, fraction), 0f, true);
        _effects.Draw(0f, default, default, MaterialCompiler.Vision);
    }

    private string? Check(Image image)
    {
        if (_cue.Shells.Any(s => s.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off ||
            (s.Layers & (RenderLayers.SpotShadow | RenderLayers.FlareShadow)) != 0)) return "cue participates in shadow cameras";
        if (_stage == 0) _standing = image;
        if (_stage == 1 && Changed(image, _standing!) < 300) return "full cue produced too few changed pixels";
        if (_stage == 2)
        {
            float cut = _camera.UnprojectPosition(new Vector3(0, (_cue.HeadY + _cue.FeetY) * 0.5f, 0)).Y;
            int upper = Changed(image, _standing!, 0, (int)cut - 10);
            int lower = Changed(image, _standing!, (int)cut + 10, image.GetHeight());
            if (upper > 30 || lower < 100) return $"half coverage must leave only lower body: upper={upper}, lower={lower}";
        }
        if (_stage == 4 && (!_cue.Shown || _cue.Coverage != MercShockImmunityView.MinimumCoverage || Changed(image, _standing!) < 3))
            return "authoritative-pending zero timer lost its final sliver";
        if (_stage == 5) _crouched = image;
        if (_stage == 6 && (_cue.HeadY - _cue.FeetY >= _standingHeight * 0.9f || Changed(image, _crouched!) < 200))
            return "cue bounds did not follow the crouched skeleton";
        if (_stage == 7)
        {
            int cut = (int)_camera.UnprojectPosition(new Vector3(0, (_cue.HeadY + _cue.FeetY) * 0.5f, 0)).Y;
            if (Changed(image, _crouched!, 0, cut - 10) > 30 || Changed(image, _crouched!, cut + 10, image.GetHeight()) < 40)
                return "crouched half coverage did not drain from head toward feet";
        }
        if (_stage == 11) _maskOff = image;
        if (_stage == 12 && (_effects.MeshOf(_mask) is not { Visible: true } || Changed(image, _maskOff!) < 10))
            return "worn mask did not appear on the rendered head";
        if (_stage == 13 && (_cue.Shown || _effects.MeshOf(_mask)?.Layers != RenderLayers.ShadowOnly))
            return "first-person body cue or mask leaked into main view";
        if (_stage == 14 && _effects.MeshOf(_mask)?.Visible != false) return "dead wearer's mask remained visible";
        if (_stage == 15) _occluded = image;
        if (_stage == 16 && Changed(image, _occluded!) > 2) return "cue showed through opaque wall";
        return null;
    }

    private static int Changed(Image image, Image baseline, int firstRow = 0, int lastRow = int.MaxValue)
    {
        int count = 0;
        for (int y = Math.Max(0, firstRow); y < Math.Min(image.GetHeight(), lastRow); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color a = image.GetPixel(x, y), b = baseline.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 0.025f) count++;
            }
        return count;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        _display.Release();
        _viewport!.QueueFree();
        _final = result;
        return SmokeResult.Running;
    }
}
