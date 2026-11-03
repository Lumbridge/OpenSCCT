using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>Draws synthetic timed materials through the actual compiler and checks the GPU pixels over a full cycle.</summary>
public sealed class MaterialSequenceSmoke : SmokeTest
{
    public const string TestName = "material-sequence";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private MaterialCompiler? _compiler;
    private int _phase, _settle;
    private readonly float[] _times = [1f, 1.5f, 2.1f, 3.1f];
    private string _output = "";
    private bool _states;
    private ShaderMaterial? _stateMaterial;
    private MeshInstance3D? _reference;
    private MaterialSequenceEntry? _stateSequence;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_states) return CheckStates(context);
        if (_viewport is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("a renderer is required for sequence pixels");
            _output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "content", "sequence-smoke");
            Directory.CreateDirectory(_output);
            foreach ((string name, Color color) in new[] { ("red", Colors.Red), ("green", Colors.Green), ("blue", Colors.Blue), ("white", Colors.White) })
            {
                using Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
                image.Fill(color);
                image.SavePng(Path.Combine(_output, name + ".png"));
            }
            MaterialSequenceFrame Frame(string texture, bool fade) => new(new(1f, fade), new(texture + ".png", null, null), null);
            var sequence = new MaterialSequenceEntry("synthetic-cycle", true, false, 0, [Frame("red", true), Frame("green", false), Frame("blue", false)]);
            var mask = new MaterialSequenceEntry("synthetic-mask", true, false, 0, [Frame("white", true), Frame("white", false), Frame("white", false)]);
            _compiler = new(new MaterialsDoc(), new TextureCache(_output));
            var entry = new MaterialEntry { Name = "cycle", Texture = "red.png", Unlit = true, TwoSided = true,
                SelfIllum = "red.png", SelfIllumMask = "white.png", Sequences = new Dictionary<string, MaterialSequenceEntry>
                { ["diffuse"] = sequence, ["self_illum"] = sequence, ["self_illum_mask"] = mask } };
            _viewport = new SubViewport { Size = new Vector2I(128, 128), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            _viewport.AddChild(new Camera3D { Position = new Vector3(0f, 0f, 3f), Current = true, CullMask = 1 });
            _viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(2f, 2f) }, MaterialOverride = _compiler.Build(entry, LightSource.None) });
        }
        MaterialCompiler.SetTime(_times[_phase]);
        if (++_settle < 5) return SmokeResult.Running;
        using Image shot = _viewport.GetTexture().GetImage();
        Color c = shot.GetPixel(64, 64);
        shot.SavePng(Path.Combine(_output, $"phase-{_phase}.png"));
        bool good = _phase switch
        {
            0 or 3 => c.B > 0.5f && c.R < 0.1f && c.G < 0.1f,
            1 => c.R > 0.2f && c.B > 0.2f && MathF.Abs(c.R - c.B) < 0.08f && c.G < 0.1f,
            _ => c.G > 0.3f && c.R < 0.1f && c.B < 0.1f,
        };
        if (!good) return SmokeResult.Fail($"phase={_phase}, time={_times[_phase]}, centre={c}");
        if (++_phase < _times.Length) { _settle = 0; return SmokeResult.Running; }
        _viewport.QueueFree();
        _viewport = null;
        _phase = _settle = 0;
        _states = true;
        return SmokeResult.Running;
    }

    private SmokeResult CheckStates(SmokeContext context)
    {
        if (_viewport is null)
        {
            using JsonDocument json = JsonDocument.Parse("""
                {"texture":"red.png","translucent":true,"unlit":true,"sequences":{"diffuse":{
                  "source":"state-cycle","loop":true,"switch_material":true,"items":[
                    {"texture":"red.png","time":0.5,"action":0,"material":{"texture":"red.png",
                      "blend":"translucent","two_sided":true,"self_illum":"green.png","self_illum_mask":"white.png",
                      "opacity":"quarter.png","opacity_matrix":[1,0,0,1,0,0]}},
                    {"texture":"blue.png","time":1,"action":0,"material":{"texture":"blue.png","blend":"additive"}}
                ]}}}
                """);
            using Image alpha = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            alpha.Fill(new Color(1f, 1f, 1f, .25f));
            alpha.SavePng(Path.Combine(_output, "quarter.png"));
            MaterialEntry entry = MaterialsDoc.FromFlags("state-cycle", json.RootElement);
            entry.Lightmap = "white.png";
            _stateSequence = entry.Sequences["diffuse"];
            _stateMaterial = (ShaderMaterial)_compiler!.Build(entry, LightSource.Lightmap);
            _viewport = new SubViewport { Size = new Vector2I(256, 128), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            _viewport.AddChild(new Camera3D { Position = new Vector3(0f, 0f, 3f), Current = true, CullMask = 1,
                Projection = Camera3D.ProjectionType.Orthogonal, Size = 2f });
            _viewport.AddChild(new MeshInstance3D { Position = new Vector3(-1f, 0f, 0f), Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f) }, MaterialOverride = _stateMaterial });
            _reference = new MeshInstance3D { Position = new Vector3(1f, 0f, 0f), Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f) } };
            _viewport.AddChild(_reference);
        }
        float[] times = [1f, 1.25f, 1.75f, 2.6f];
        int frame = _phase == 2 ? 1 : 0;
        if (_settle == 0)
        {
            MaterialEntry expected = _stateSequence!.Items[frame].Material!.WithLightmap("white.png");
            _reference!.MaterialOverride = _compiler!.Build(expected, LightSource.Lightmap);
        }
        MaterialCompiler.SetTime(times[_phase]);
        if (++_settle < 5) return SmokeResult.Running;
        string shader = _stateMaterial!.Shader.Code;
        if (shader.Contains("BLEND_ADDITIVE", StringComparison.Ordinal) != (frame == 1)
            || shader.Contains("#define SELF_ILLUM\n", StringComparison.Ordinal) != (frame == 0)
            || !shader.Contains("LIGHT_LIGHTMAP", StringComparison.Ordinal))
            return SmokeResult.Fail($"Show state {frame} did not switch blend/illumination or preserve the lightmap");
        using Image shot = _viewport.GetTexture().GetImage();
        Color actual = shot.GetPixel(64, 64), reference = shot.GetPixel(192, 64);
        shot.SavePng(Path.Combine(_output, $"state-{_phase}.png"));
        if (MathF.Abs(actual.R - reference.R) + MathF.Abs(actual.G - reference.G) + MathF.Abs(actual.B - reference.B) > .03f
            || MathF.Max(actual.R, MathF.Max(actual.G, actual.B)) < .15f)
            return SmokeResult.Fail($"Show state {frame}: sequence={actual}, static reference={reference}");
        if (++_phase < times.Length) { _settle = 0; return SmokeResult.Running; }
        _viewport.QueueFree();
        return SmokeResult.Pass("timed diffuse/glow/mask fade plus complete Show shader/opacity/blend states match GPU references over a loop");
    }
}
