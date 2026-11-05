using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// Real skinned spy and shocker surfaces through the display pass: cloak must cover the entire silhouette at
/// every angle, including cosmetic parts split after activation, and return the original material on release.
/// A uniform opaque backdrop makes refraction's expected full-cloak result independent of surface normals.
/// </summary>
public sealed class CloakRenderSmoke : SmokeTest
{
    public const string TestName = "cloak-render";
    public override string Name => TestName;
    private static readonly string[] s_phases = ["background", "opaque", "half", "full", "full-split", "restored", "changed-reference"];
    private const int SpyViews = 8;
    private const int PatternedView = SpyViews + 2;
    private const int Views = PatternedView + 1;
    private SubViewport? _viewport;
    private Camera3D _camera = null!;
    private DisplaySpaceEffect _display = null!;
    private SkeletalModel _spy = null!, _pistol = null!, _cosmeticReference = null!;
    private ShaderMaterial _backdrop = null!;
    private Image? _background, _opaque, _changedRestored;
    private string _folder = "";
    private int _view, _phase, _frames, _teardown;
    private SmokeResult? _final;
    private enum VisionCheck { Background, Mask, Hidden, Revealed, NightOpaque, NightHalf, NightHidden, NightRestored }
    private sealed record VisionPhase(string Name, int Mode, byte Glass, byte Target, VisionCheck Check);
    private static readonly VisionPhase[] s_visionPhases =
    [
        new("normal-background", RenderK.RendMapNormal, 0, 0, VisionCheck.Background),
        new("normal-opaque", RenderK.RendMapNormal, 0, 0, VisionCheck.Mask),
        new("normal-cloaked", RenderK.RendMapNormal, 255, 255, VisionCheck.Hidden),
        new("night-background", RenderK.RendMapNight, 0, 0, VisionCheck.Background),
        new("night-opaque", RenderK.RendMapNight, 0, 0, VisionCheck.NightOpaque),
        new("night-half", RenderK.RendMapNight, 128, 255, VisionCheck.NightHalf),
        new("night-cloaked", RenderK.RendMapNight, 255, 255, VisionCheck.NightHidden),
        new("night-fading-out", RenderK.RendMapNight, 128, 0, VisionCheck.NightHalf),
        new("night-restored", RenderK.RendMapNight, 0, 0, VisionCheck.NightRestored),
        new("thermal-background", RenderK.RendMapThermal, 255, 255, VisionCheck.Background),
        new("thermal-cloaked", RenderK.RendMapThermal, 255, 255, VisionCheck.Revealed),
        new("emf-background", RenderK.RendMapEmf, 255, 255, VisionCheck.Background),
        new("emf-cloaked", RenderK.RendMapEmf, 255, 255, VisionCheck.Revealed),
        new("emf-fading-out", RenderK.RendMapEmf, 128, 0, VisionCheck.Revealed),
        new("night-return-background", RenderK.RendMapNight, 255, 255, VisionCheck.Background),
        new("night-return-cloaked", RenderK.RendMapNight, 255, 255, VisionCheck.NightHidden),
        new("normal-return-background", RenderK.RendMapNormal, 255, 255, VisionCheck.Background),
        new("normal-return-cloaked", RenderK.RendMapNormal, 255, 255, VisionCheck.Hidden),
    ];
    private SimPawnView? _pawnView;
    private SPawnAttaque _pawn = null!;
    private RenderPack? _pack;
    private int _visionPhase;
    private bool[,]? _visionMask;
    private int _visionPixels;
    private float _nightOpaqueContrast;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_final is { } result) return ++_teardown < 6 ? SmokeResult.Running : result;
        if (_view == Views) return StepVision();
        if (_viewport is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
            string? spy = SkeletalModel.FindGlb("SPerso.ATT_01");
            string? pistol = SkeletalModel.FindGlb("SWeaponFirst.p190");
            if (spy is null || pistol is null) return SmokeResult.Fail("converted spy and shocker content is required");
            Build(context, spy, pistol);
            Configure();
        }

        if (++_frames < 12) return SmokeResult.Running;
        using Image shot = _viewport!.GetTexture().GetImage();
        string name = $"{ViewName}-{s_phases[_phase]}";
        shot.SavePng(Path.Combine(_folder, name + ".png"));
        string? failure = Check(shot, name);
        if (failure is not null) return Finish(SmokeResult.Fail(failure + "; " + _folder));

        if (++_phase == (_view == 0 ? s_phases.Length : s_phases.Length - 1))
        {
            _phase = 0;
            if (++_view == Views)
            {
                BeginVision();
                ConfigureVision();
                return SmokeResult.Running;
            }
        }
        _frames = 0;
        Configure();
        return SmokeResult.Running;
    }

    private string ViewName => _view == PatternedView ? "spy-patterned" : _view < SpyViews ? $"spy-{_view * 45:D3}" : $"shocker-{(_view - SpyViews) * 90:D3}";
    private SkeletalModel Model => _view < SpyViews || _view == PatternedView ? _spy : _pistol;

    private void Build(SmokeContext context, string spyPath, string pistolPath)
    {
        _folder = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../local/cloak-render"));
        Directory.CreateDirectory(_folder);
        MaterialCompiler.EnsureGlobals();
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        _viewport = new SubViewport
        {
            Size = new Vector2I(320, 400), OwnWorld3D = true, UseDebanding = false,
            Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled, UseTaa = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        context.Tree.Root.AddChild(_viewport);
        _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Current = true, Near = 0.01f, Far = 100f,
            KeepAspect = Camera3D.KeepAspectEnum.Height, CullMask = RenderLayers.MainView,
            Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = [_display] },
        };
        _viewport.AddChild(_camera);
        _backdrop = new ShaderMaterial { Shader = new Shader
        {
            Code = "shader_type spatial; render_mode unshaded, fog_disabled; uniform bool patterned = false; " +
                "void fragment() { float tile = mod(floor(SCREEN_UV.x * 12.0) + floor(SCREEN_UV.y * 15.0), 2.0); " +
                "ALBEDO = patterned ? mix(vec3(0.18, 0.28, 0.46), vec3(0.62, 0.51, 0.27), tile) : vec3(0.28, 0.42, 0.58); }",
        } };
        _camera.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(100, 100) }, Position = new Vector3(0, 0, -20),
            MaterialOverride = _backdrop,
        });
        _spy = AddModel(spyPath, "WaitStAlFd0");
        _pistol = AddModel(pistolPath, "WaitP190");
        _cosmeticReference = AddModel(spyPath, "WaitStAlFd0");
        _cosmeticReference.SetCosmetic("gadgeteer");
    }

    private SkeletalModel AddModel(string path, string pose)
    {
        SkeletalModel model = SkeletalModel.Load(path);
        _viewport!.AddChild(model.Root);
        model.Place(default, default, default);
        model.Sampler.Play(pose);
        model.Pose();
        model.Light(new ActorLightSet { Ambient = new Vector3(0.6f, 0.6f, 0.6f) });
        model.Root.Visible = false;
        return model;
    }

    private void Configure()
    {
        _spy.Root.Visible = _pistol.Root.Visible = _cosmeticReference.Root.Visible = false;
        SkeletalModel model = Model;
        if (_phase == 0)
        {
            _background?.Dispose();
            _background = null;
            _opaque?.Dispose();
            _opaque = null;
            _backdrop.SetShaderParameter("patterned", _view == PatternedView);
            model.SetGlass(0f);
            // All later spy angles exercise the fitted finish on the now separate body and head.
            model.SetCosmetic(_view > 0 ? "gadgeteer" : null);
            Frame(model);
        }
        if (_phase == 4 && _view == 0)
        {
            // This is deliberately after activation: a newly split mesh must inherit the active cloak even
            // when the next SetGlass call repeats exactly the same amount.
            int before = model.Meshes.Count;
            foreach (string material in model.Slots.Distinct().ToArray()) model.SplitPart(material);
            GD.Print($"SVM_CLOAK_RENDER split meshes={before}->{model.Meshes.Count}");
            model.Light(new ActorLightSet { Ambient = new Vector3(0.6f, 0.6f, 0.6f) });
            // Changing a material while cloaked must not expose it, and releasing cloak must retain the new
            // finish. Its expected image is rendered independently by a model that has never been cloaked.
            model.SetCosmetic("gadgeteer");
        }
        if (_phase == 6) { _cosmeticReference.Root.Visible = true; return; }
        model.Root.Visible = _phase != 0;
        model.SetGlass(_phase is 3 or 4 ? 1f : _phase == 2 ? 0.5f : 0f);
    }

    private void Frame(SkeletalModel model, float? angleOverride = null)
    {
        Aabb? bounds = null;
        foreach (MeshInstance3D mesh in model.Meshes)
        {
            Aabb box = mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } all ? all.Merge(box) : box;
        }
        Aabb frame = bounds!.Value;
        Vector3 centre = frame.GetCenter();
        float across = new Vector2(frame.Size.X, frame.Size.Z).Length();
        _camera.Size = Math.Max(frame.Size.Y, across * 1.25f) * 1.15f;
        float angle = Mathf.DegToRad(angleOverride ?? (_view == PatternedView ? 180 : _view < SpyViews ? _view * 45 : (_view - SpyViews) * 90 + 45));
        _camera.Position = centre + new Vector3(Mathf.Cos(angle), 0.04f, Mathf.Sin(angle)) * 4f;
        _camera.LookAt(centre);
    }

    private string? Check(Image shot, string name)
    {
        if (_phase == 0) { _background = (Image)shot.Duplicate(); return null; }
        if (_phase == 1) { _opaque = (Image)shot.Duplicate(); return null; }
        if (_phase == 5 && _view == 0) { _changedRestored = (Image)shot.Duplicate(); return null; }
        if (_phase == 6)
        {
            int changed = 0, wrong = 0;
            for (int y = 0; y < shot.GetHeight(); y++)
                for (int x = 0; x < shot.GetWidth(); x++)
                {
                    if (Distance(shot.GetPixel(x, y), _opaque!.GetPixel(x, y)) > 0.025f) changed++;
                    if (Distance(shot.GetPixel(x, y), _changedRestored!.GetPixel(x, y)) > 0.025f) wrong++;
                }
            GD.Print($"SVM_CLOAK_RENDER changed-cosmetic changed={changed} restored-diff={wrong}");
            if (changed < 500) return $"{name}: replacement cosmetic did not visibly change the model ({changed} pixels)";
            return wrong > 8 ? $"{name}: {wrong} pixels did not retain the new cosmetic after uncloaking" : null;
        }
        // The patterned backdrop is a visual refraction fixture: screen-space offsets intentionally change
        // those pixels. Exact numerical fade checks use the flat backdrop above, where refraction is invariant.
        if (_view == PatternedView && _phase is 2 or 3 or 4) return null;
        int silhouette = 0, opaqueRemnants = 0, partialWrong = 0, restoredWrong = 0;
        float originalDistance = 0f, cloakDistance = 0f;
        for (int y = 1; y < shot.GetHeight() - 1; y++)
            for (int x = 1; x < shot.GetWidth() - 1; x++)
            {
                Color bg = _background!.GetPixel(x, y);
                float reference = Distance(_opaque!.GetPixel(x, y), bg);
                if (reference < 0.08f) continue;
                silhouette++;
                float distance = Distance(shot.GetPixel(x, y), bg);
                originalDistance += reference;
                cloakDistance += distance;
                if (distance > 0.025f) opaqueRemnants++;
                if (Distance(shot.GetPixel(x, y), _opaque.GetPixel(x, y).Lerp(bg, 0.5f)) > 0.025f) partialWrong++;
                if (Distance(shot.GetPixel(x, y), _opaque.GetPixel(x, y)) > 0.025f) restoredWrong++;
            }
        if (silhouette < 500) return $"{name}: fixture has only {silhouette} visible silhouette pixels";
        float ratio = cloakDistance / originalDistance;
        GD.Print($"SVM_CLOAK_RENDER {name} pixels={silhouette} remaining={opaqueRemnants} mean-opacity={ratio:F4} half-diff={partialWrong} restored-diff={restoredWrong}");
        if (_phase == 2 && (ratio < 0.2f || ratio > 0.8f))
            return $"{name}: partial cloak did not fade the body (relative contrast {ratio:F3})";
        if (_phase == 2 && partialWrong > Math.Max(8, silhouette / 200))
            return $"{name}: {partialWrong}/{silhouette} pixels do not share the same halfway fade";
        if (_phase is 3 or 4 && opaqueRemnants > Math.Max(8, silhouette / 200))
            return $"{name}: {opaqueRemnants}/{silhouette} pixels still show the model at full cloak";
        if (_phase == 5 && restoredWrong > Math.Max(8, silhouette / 200))
            return $"{name}: {restoredWrong}/{silhouette} pixels did not regain their original material";
        return null;
    }

    private static float Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private void BeginVision()
    {
        _spy.Root.Visible = _pistol.Root.Visible = _cosmeticReference.Root.Visible = false;
        _backdrop.SetShaderParameter("patterned", false);
        var world = new World(1, ENetMode.NM_Standalone);
        _pawn = world.Level.SpawnActor(new SPawnAttaque_1Mesh(), default, default)!;
        _pawn.WeaponLoaded = 0;
        _pawn.HeatIntensity = 1f;
        _pawn.HeatOpacity = 1f;
        _pawnView = new SimPawnView();
        _viewport!.AddChild(_pawnView);
        if (!_pawnView.Bind(_pawn, null)) throw new InvalidOperationException("vision fixture could not bind spy");
        _pawnView.OnTick();
        _pack = RenderPack.Load(Path.GetFullPath(ProjectSettings.GlobalizePath("res://../.content/shared")));
        _opaque?.Dispose();
        _opaque = null;
    }

    private void ConfigureVision()
    {
        _frames = 0;
        VisionPhase phase = s_visionPhases[_visionPhase];
        MaterialCompiler.SetVision(phase.Mode);
        _display.Settings = new DisplayPostSettings { GlowGate = false, RendMap = phase.Mode, Pack = _pack };
        _pawn.GlassEffect = phase.Glass;
        _pawn.DesiredGlassEffect = phase.Target;
        // Exercise the same visibility derivation and DrawGlass gate used by the live pawn view. The fade-out
        // case has no weapon or goggles supplying EMF visibility: only the still-visible cloak transition.
        PawnVision.TestAndSetEMFVisible(_pawn);
        _pawnView!.Draw(1f, 0f);
        _pawnView.Model!.Sampler.Play("WaitStAlFd0");
        _pawnView.Model.Pose();
        _pawnView.Model.Light(new ActorLightSet { Ambient = new Vector3(0.6f, 0.6f, 0.6f) });
        _pawnView.Visible = phase.Check != VisionCheck.Background;
        Frame(_pawnView.Model, 180f);
    }

    private SmokeResult StepVision()
    {
        if (++_frames < 16) return SmokeResult.Running;
        using Image shot = _viewport!.GetTexture().GetImage();
        VisionPhase phase = s_visionPhases[_visionPhase];
        string name = "vision-" + phase.Name;
        shot.SavePng(Path.Combine(_folder, name + ".png"));
        if (phase.Check == VisionCheck.Background)
        {
            _background?.Dispose();
            _background = (Image)shot.Duplicate();
        }
        else if (phase.Check == VisionCheck.Mask)
        {
            _visionMask = new bool[shot.GetWidth(), shot.GetHeight()];
            for (int y = 0; y < shot.GetHeight(); y++)
                for (int x = 0; x < shot.GetWidth(); x++)
                    if (Distance(shot.GetPixel(x, y), _background!.GetPixel(x, y)) > 0.08f)
                    {
                        _visionMask[x, y] = true;
                        _visionPixels++;
                    }
            if (_visionPixels < 3000) return Finish(SmokeResult.Fail("vision fixture has no usable pawn silhouette"));
        }
        else if (phase.Mode == RenderK.RendMapNight)
        {
            // Grain moves every frame. Its signed mean over the silhouette cancels out; red retains headroom
            // under night vision's gain, so both the fade and opaque restoration remain measurable.
            float difference = 0f;
            for (int y = 0; y < shot.GetHeight(); y++)
                for (int x = 0; x < shot.GetWidth(); x++)
                    if (_visionMask![x, y]) difference += shot.GetPixel(x, y).R - _background!.GetPixel(x, y).R;
            float contrast = difference / _visionPixels;
            GD.Print($"SVM_CLOAK_RENDER {name} mean-red-contrast={contrast:F4}");
            if (phase.Check == VisionCheck.NightOpaque)
            {
                _nightOpaqueContrast = contrast;
                if (Math.Abs(contrast) < 0.08f)
                    return Finish(SmokeResult.Fail($"{name}: night fixture has insufficient opaque contrast; " + _folder));
            }
            if (phase.Check == VisionCheck.NightHidden && Math.Abs(contrast) > 0.02f)
                return Finish(SmokeResult.Fail($"{name}: night vision exposes the cloaked spy (mean contrast {contrast:F4}); " + _folder));
            if (phase.Check == VisionCheck.NightHalf && contrast / _nightOpaqueContrast is < 0.2f or > 0.8f)
                return Finish(SmokeResult.Fail($"{name}: night vision did not retain the partial cloak fade; " + _folder));
            if (phase.Check == VisionCheck.NightRestored && Math.Abs(contrast - _nightOpaqueContrast) > 0.02f)
                return Finish(SmokeResult.Fail($"{name}: night vision did not restore the uncloaked body; " + _folder));
        }
        else
        {
            int changed = 0;
            float contrast = 0f;
            for (int y = 0; y < shot.GetHeight(); y++)
                for (int x = 0; x < shot.GetWidth(); x++)
                {
                    if (!_visionMask![x, y]) continue;
                    float distance = Distance(shot.GetPixel(x, y), _background!.GetPixel(x, y));
                    if (distance > 0.1f) changed++;
                    contrast += distance;
                }
            GD.Print($"SVM_CLOAK_RENDER {name} contrast-pixels={changed} contrast={contrast:F2} emf={_pawn.EMFOpacity:F2}");
            if (phase.Check == VisionCheck.Hidden && changed > 8)
                return Finish(SmokeResult.Fail($"{name}: switching vision left {changed} opaque pixels; " + _folder));
            if (phase.Check == VisionCheck.Revealed && changed < _visionPixels / 3)
                return Finish(SmokeResult.Fail($"{name}: cloaked spy is not visible ({changed} contrast pixels); " + _folder));
        }
        if (++_visionPhase == s_visionPhases.Length)
            return Finish(SmokeResult.Pass("8 spy angles and 2 shocker angles: uniform fade, full cloak, late split, live cosmetic restoration; night activation/fade/restoration, thermal/EMF reveal cloak and fade-out, returning to night/normal restores cloak; patterned captures; " + _folder));
        ConfigureVision();
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _background?.Dispose();
        _opaque?.Dispose();
        _changedRestored?.Dispose();
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        _display?.Release();
        _viewport?.QueueFree();
        _final = result;
        return SmokeResult.Running;
    }
}
