using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Lighting;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Smoke;

/// <summary>Opt-in differential checks: original actor-light scan and rebuilt particles versus retained buffers.</summary>
public sealed class RenderPerformanceParitySmoke : SmokeTest
{
    public const string TestName = "render-performance-parity";
    public override string Name => TestName;
    private readonly SubViewport[] _views = new SubViewport[2];
    private readonly MeshInstance3D[] _lineNodes = new MeshInstance3D[2];
    private readonly MeshInstance3D[] _quadNodes = new MeshInstance3D[2];
    private readonly ArrayMesh[] _lines = [new(), new()];
    private readonly ArrayMesh[] _quads = [new(), new()];
    private LineBuffer? _lineBuffer;
    private QuadBuffer? _quadBuffer;
    private int _capacity;
    private int _case;
    private int _changedAt;
    private int _lightChecks;
    private long _mapCandidates;
    private long _allMapCandidates;
    private ActorUploadFixture? _actorUploads;
    private string _uploadTiming = "";
    private Color _unlitActorPixel;
    private static readonly int[] Counts = [9, 9, 17, 5, 0, 9];
    private static readonly int[] Visions = [RenderK.RendMapNormal, 36, 38];

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_views[0] is null)
            {
                if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("particle parity needs a renderer");
                (_lightChecks, _mapCandidates, _allMapCandidates) = CheckActorLights(context);
                MaterialCompiler.EnsureGlobals();
                for (int i = 0; i < 2; i++)
                {
                    _views[i] = new SubViewport
                    {
                        Size = new Vector2I(320, 240), OwnWorld3D = true,
                        RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false,
                    };
                    context.Tree.Root.AddChild(_views[i]);
                    _views[i].AddChild(new Camera3D
                    {
                        Position = new Vector3(0, 0, 5), Projection = Camera3D.ProjectionType.Orthogonal,
                        // The default all-layer mask includes ShadowPass, which intentionally makes solid
                        // world materials output black depth-only pixels instead of evaluating their lights.
                        Size = 4, Current = true, CullMask = RenderLayers.MainView,
                        Environment = new Godot.Environment
                        {
                            BackgroundMode = Godot.Environment.BGMode.Color,
                            BackgroundColor = new Color(0.13f, 0.18f, 0.24f),
                        },
                    });
                    _lineNodes[i] = new MeshInstance3D { Mesh = _lines[i] };
                    _quadNodes[i] = new MeshInstance3D { Mesh = _quads[i] };
                    _views[i].AddChild(_lineNodes[i]);
                    _views[i].AddChild(_quadNodes[i]);
                }
                _actorUploads = new ActorUploadFixture(_views);
                _uploadTiming = _actorUploads.Measure();
                Prepare(context.Frame);
                return SmokeResult.Running;
            }

            if (context.Frame <= _changedAt + 1) return SmokeResult.Running;
            using Image expected = _views[0].GetTexture().GetImage();
            using Image actual = _views[1].GetTexture().GetImage();
            if (expected.IsEmpty() || actual.IsEmpty() || expected.GetSize() != new Vector2I(320, 240)
                || actual.GetSize() != expected.GetSize())
                throw new InvalidOperationException("empty or incorrectly sized render parity images");
            if (!expected.GetData().AsSpan().SequenceEqual(actual.GetData()))
            {
                string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "performance-overnight", "render-parity");
                Directory.CreateDirectory(output);
                expected.SavePng(Path.Combine(output, $"case-{_case}-expected.png"));
                actual.SavePng(Path.Combine(output, $"case-{_case}-actual.png"));
                Cleanup();
                return SmokeResult.Fail($"particle pixels differ at case {_case}; images in {output}");
            }

            Color actorPixel = expected.GetPixel(160, 120);
            if (actorPixel == expected.GetPixel(0, 0))
                throw new InvalidOperationException("actor upload fixture did not render at its centre");
            // The actor occupies the otherwise empty centre. With unchanged geometry and ambient, bringing
            // lights back must change its normal-view pixels: equal blank/default shaders cannot pass.
            int stage = _case % Counts.Length;
            if (stage == 4) _unlitActorPixel = actorPixel;
            if (stage == 5 && _case < Counts.Length * 7 && actorPixel == _unlitActorPixel)
                throw new InvalidOperationException($"normal-view actor pixels did not respond to restored lights in case {_case}: {actorPixel}");

            if (++_case == Counts.Length * 7 * Visions.Length)
            {
                Cleanup();
                return SmokeResult.Pass($"{_lightChecks} exact actor-light comparisons, {_mapCandidates}/{_allMapCandidates} map candidates; {_case} exact particle/actor-upload image comparisons (all draw styles, normal/thermal/EMF, count growth/shrink/empty/reappear, mesh/material replacement and node lifecycle); {_uploadTiming}");
            }
            Prepare(context.Frame);
            return SmokeResult.Running;
        }
        catch (Exception error)
        {
            Cleanup();
            return SmokeResult.Fail(error.ToString());
        }
    }

    private void Prepare(int frame)
    {
        int stage = _case % Counts.Length;
        int style = _case / Counts.Length % 7;
        int vision = Visions[_case / (Counts.Length * 7)];
        int count = Counts[stage];
        MaterialCompiler.SetVision(vision);
        _actorUploads!.Prepare(stage, style);
        if (stage == 0)
        {
            var material = new ShaderMaterial { Shader = ParticleSystemView.ShaderFor(style, true, true, false) };
            material.SetShaderParameter("alpha_ref", 0.1f);
            material.SetShaderParameter("heat_opacity", 0.7f);
            foreach (MeshInstance3D node in _lineNodes) node.MaterialOverride = material;
            foreach (MeshInstance3D node in _quadNodes) node.MaterialOverride = material;
        }

        var lines = new List<SparkSegment>();
        var quads = new List<SpriteQuad>();
        for (int i = 0; i < count; i++)
        {
            float x = -1.4f + i % 9 * 0.33f, y = 0.45f + i / 9 * 0.6f;
            float offset = stage * 0.018f;
            var color = new FxColor((byte)(i * 11 + stage * 7), (byte)(255 - i * 9), (byte)(60 + i * 10), (byte)(50 + i * 11));
            lines.Add(new SparkSegment(UeSpace.ToUe(new Vector3(x + offset, y, 0)),
                UeSpace.ToUe(new Vector3(x + 0.25f, y + 0.34f + offset, 0)), stage * 0.03f, 0.7f, color));
            quads.Add(new SpriteQuad
            {
                C0 = UeSpace.ToUe(new Vector3(x + offset, -y, 0)), C1 = UeSpace.ToUe(new Vector3(x + 0.2f, -y, 0)),
                C2 = UeSpace.ToUe(new Vector3(x + offset, -y - 0.25f, 0)), C3 = UeSpace.ToUe(new Vector3(x + 0.2f, -y - 0.25f, 0)),
                U0 = offset, U1 = 0.8f, V0 = 0.1f, V1 = 0.9f, BU0 = 0.2f, BU1 = 1f, BV0 = offset, BV1 = 1f, Color = color,
            });
        }

        foreach (MeshInstance3D node in _lineNodes) node.Visible = count > 0;
        foreach (MeshInstance3D node in _quadNodes) node.Visible = count > 0;
        if (count > 0)
        {
            BuildLines(_lines[0], lines);
            if (_lineBuffer is null || !_lineBuffer.Fill(lines))
            {
                BuildLines(_lines[1], lines);
                _lineBuffer = LineBuffer.For(_lines[1], count) ?? throw new InvalidOperationException("retained line layout unavailable");
            }
            _capacity = Math.Max(_capacity, QuadBuffer.CapacityFor(count));
            BuildQuads(_quads[0], quads, _capacity);
            Aabb bounds = QuadBuffer.Bounds(quads);
            if (_quadBuffer is null || !_quadBuffer.Fill(quads, bounds))
            {
                BuildQuads(_quads[1], quads, _capacity);
                _quadBuffer = QuadBuffer.For(_quads[1], _capacity, count) ?? throw new InvalidOperationException("retained quad layout unavailable");
            }
            _quads[0].CustomAabb = bounds;
            _quads[1].CustomAabb = bounds;
            CompareArrays(_lines[0], _lines[1], "lines");
            CompareArrays(_quads[0], _quads[1], "quads");
        }
        _changedAt = frame;
    }

    private static void BuildLines(ArrayMesh mesh, List<SparkSegment> lines)
    {
        var positions = new Vector3[lines.Count * 2];
        var uv = new Vector2[positions.Length];
        var colors = new Color[positions.Length];
        for (int i = 0; i < lines.Count; i++)
        {
            SparkSegment line = lines[i];
            positions[i * 2] = UeSpace.ToGodot(line.From);
            positions[i * 2 + 1] = UeSpace.ToGodot(line.To);
            uv[i * 2] = new Vector2(0, line.V0);
            uv[i * 2 + 1] = new Vector2(1, line.V1);
            colors[i * 2] = colors[i * 2 + 1] = Colour(line.Color);
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        mesh.ClearSurfaces();
        mesh.CustomAabb = default;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays, flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
    }

    private static void BuildQuads(ArrayMesh mesh, List<SpriteQuad> quads, int capacity)
    {
        var positions = new Vector3[capacity * 4];
        var uv = new Vector2[positions.Length];
        var uv2 = new Vector2[positions.Length];
        var colors = new Color[positions.Length];
        var indices = new int[capacity * 6];
        for (int i = 0; i < quads.Count; i++)
        {
            SpriteQuad q = quads[i];
            int at = i * 4;
            positions[at] = UeSpace.ToGodot(q.C0); positions[at + 1] = UeSpace.ToGodot(q.C1);
            positions[at + 2] = UeSpace.ToGodot(q.C2); positions[at + 3] = UeSpace.ToGodot(q.C3);
            uv[at] = new Vector2(q.U0, q.V1); uv[at + 1] = new Vector2(q.U1, q.V1);
            uv[at + 2] = new Vector2(q.U0, q.V0); uv[at + 3] = new Vector2(q.U1, q.V0);
            uv2[at] = new Vector2(q.BU0, q.BV1); uv2[at + 1] = new Vector2(q.BU1, q.BV1);
            uv2[at + 2] = new Vector2(q.BU0, q.BV0); uv2[at + 3] = new Vector2(q.BU1, q.BV0);
            for (int corner = 0; corner < 4; corner++) colors[at + corner] = Colour(q.Color);
        }
        for (int i = 0; i < capacity; i++)
        {
            int at = i * 4, j = i * 6;
            indices[j] = at; indices[j + 1] = at + 1; indices[j + 2] = at + 2;
            indices[j + 3] = at + 2; indices[j + 4] = at + 1; indices[j + 5] = at + 3;
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions; arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uv; arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
    }

    private static Color Colour(FxColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static void CompareArrays(ArrayMesh expected, ArrayMesh actual, string kind)
    {
        using var a = expected.SurfaceGetArrays(0);
        using var b = actual.SurfaceGetArrays(0);
        if (!a[(int)Mesh.ArrayType.Vertex].AsVector3Array().AsSpan().SequenceEqual(b[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            || !a[(int)Mesh.ArrayType.Color].AsColorArray().AsSpan().SequenceEqual(b[(int)Mesh.ArrayType.Color].AsColorArray())
            || !a[(int)Mesh.ArrayType.TexUV].AsVector2Array().AsSpan().SequenceEqual(b[(int)Mesh.ArrayType.TexUV].AsVector2Array())
            || !a[(int)Mesh.ArrayType.TexUV2].AsVector2Array().AsSpan().SequenceEqual(b[(int)Mesh.ArrayType.TexUV2].AsVector2Array()))
            throw new InvalidOperationException(kind + " positions, colours or UVs differ from rebuilt surface");
    }

    private static (int Checks, long Candidates, long FullScan) CheckActorLights(SmokeContext context)
    {
        var docs = new List<LightingDoc>();
        var synthetic = new LightingDoc();
        var random = new Random(74527);
        for (int i = 0; i < 256; i++)
        {
            synthetic.Lights.Add(new LightEntry("Parity" + i, i < 24 ? default : Point(), default, i % 43 == 0 ? -10 : 64,
                (byte)i, 128, 80, 128, i % 31 == 0 ? RenderK.LE_Sunlight : i % 7 == 0 ? 8 : 0,
                1 + i % 5, true, true, false, i % 11 != 0, null, 32, i % 7, i % 9 == 0) { ZoneIndex = i % 5 });
        }
        docs.Add(synthetic);
        foreach (string name in new[] { "AquaE", "SteeE", "TermD" })
        {
            string? folder = MapBuilder.FindMapFolder(name, context.App.Options.MapRoots);
            if (folder is not null) docs.Add(LightingDoc.Load(Path.Combine(folder, "render", "lighting.json")));
        }

        int checks = 0;
        long candidates = 0, fullScan = 0;
        foreach (LightingDoc doc in docs)
        {
            var selector = new ActorLighting(doc);
            var source = doc.Lights.Where(l => ActorLightRules.LightsActors(l.ApplyInGame, l.Dynamic, l.LightType, l.Effect)).ToList();
            var off = new HashSet<string>(source.Where(l => !l.InitiallyOn).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
            for (int query = 0; query < 256; query++)
            {
                float time = query / 60f;
                selector.Time = time;
                if (source.Count > 0 && query % 11 == 0)
                {
                    int index = query % source.Count;
                    LightEntry light = source[index];
                    FVector moved = Point();
                    selector.SetPlace(light.Name, moved, new FRotator(0, query * 37, 0));
                    for (int i = 0; i < source.Count; i++)
                        if (string.Equals(source[i].Name, light.Name, StringComparison.OrdinalIgnoreCase))
                            source[i] = source[i] with { Location = moved, Rotation = new FRotator(0, query * 37, 0) };
                    bool on = query % 22 == 0;
                    selector.SetOn(light.Name, on);
                    if (on) off.Remove(light.Name); else off.Add(light.Name);
                }
                LightEntry[] spawned = query % 3 == 0 ? [synthetic.Lights[0] with { Name = "Spawned", Location = Point() }] : [];
                selector.SetSpawned(spawned);
                FVector center = source.Count > 0 && query % 2 == 0 ? source[query % source.Count].Location : Point();
                float radius = query % 13 == 0 ? 500 : 50;
                int zone = query % 6 - 1;
                Vector3 ambient = new(0.1f, 0.2f, 0.3f);
                ActorLightSet actual = selector.Select(center, radius, ambient, zone);
                candidates += selector.LastMapCandidates;
                fullScan += source.Count;
                var ranked = new List<(float Score, LightEntry Light)>();
                foreach (LightEntry light in source.Where(l => !off.Contains(l.Name)).Concat(spawned))
                {
                    if (light.OwnZoneOnly && zone >= 0 && light.ZoneIndex >= 0 && light.ZoneIndex != zone) continue;
                    float score = ActorLighting.Score(light, center, radius);
                    if (score > 0) ranked.Add((score, light));
                }
                ranked.Sort((a, b) =>
                {
                    bool sa = a.Light.Effect == RenderK.LE_Sunlight, sb = b.Light.Effect == RenderK.LE_Sunlight;
                    return sa != sb ? (sa ? -1 : 1) : b.Score.CompareTo(a.Score);
                });
                int count = Math.Min(RenderK.MaxActorLights, ranked.Count);
                if (actual.Count != count || actual.Ambient != ambient) throw new InvalidOperationException("actor light count/ambient changed");
                for (int i = 0; i < count; i++)
                    if (actual.Names[i] != ranked[i].Light.Name
                        || actual.Lights[i] != ActorLighting.PackActor(ranked[i].Light, WorldLights.Modulation(ranked[i].Light, time), center, radius))
                        throw new InvalidOperationException($"actor light differs at query {query}, slot {i}");
                checks++;
            }
        }
        return (checks, candidates, fullScan);

        FVector Point() => new((random.NextSingle() * 2f - 1f) * 20000f,
            (random.NextSingle() * 2f - 1f) * 20000f, (random.NextSingle() * 2f - 1f) * 3000f);
    }

    private void Cleanup()
    {
        foreach (SubViewport? view in _views) view?.QueueFree();
    }
}

/// <summary>Unconditional pre-optimization uploads and retained uploads must draw identical actor-lit pixels.</summary>
internal sealed class ActorUploadFixture
{
    private static readonly StringName Ambient = "svm_ambient";
    private static readonly StringName Echelon = "svm_echelon_ignore";
    private static readonly StringName[] Positions = [new("svm_l0_pos"), new("svm_l1_pos"), new("svm_l2_pos"), new("svm_l3_pos")];
    private static readonly StringName[] Colours = [new("svm_l0_col"), new("svm_l1_col"), new("svm_l2_col"), new("svm_l3_col")];
    private static readonly StringName[] Spots = [new("svm_l0_spot"), new("svm_l1_spot"), new("svm_l2_spot"), new("svm_l3_spot")];
    private readonly SubViewport[] _views;
    private readonly MeshInstance3D[] _nodes = new MeshInstance3D[2];
    private readonly Material _material;

    public ActorUploadFixture(SubViewport[] views)
    {
        _views = views;
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
        _material = compiler.Build(new MaterialEntry { Name = "actor-upload-parity" }, LightSource.Actor);
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(new Color(.35f, .55f, .7f));
        ((ShaderMaterial)_material).SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(image));
        for (int i = 0; i < _nodes.Length; i++) Create(i);
    }

    private void Create(int index)
    {
        _nodes[index] = new MeshInstance3D
        {
            Position = new Vector3(0, 0, 1), Mesh = new SphereMesh { Radius = .3f, Height = .6f },
            MaterialOverride = _material,
        };
        _views[index].AddChild(_nodes[index]);
    }

    public void Prepare(int stage, int style)
    {
        // Stage 1 repeats all lighting values while replacing other instance state. Engine-side overrides must
        // survive replacement/reparenting; new nodes must receive the complete set regardless of old identities.
        if (stage == 1)
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (style % 3 == 0)
                {
                    _nodes[i].Mesh = new BoxMesh { Size = new Vector3(.5f, .5f, .5f) };
                    _nodes[i].MaterialOverride = (Material)_material.Duplicate();
                }
                else if (style % 3 == 1)
                {
                    var parent = new Node3D();
                    _views[i].AddChild(parent);
                    _nodes[i].Reparent(parent);
                }
                else
                {
                    _nodes[i].Free();
                    Create(i);
                }
            }

        ActorLightSet set = Set(stage == 1 ? 0 : stage, style);
        for (int i = 0; i < _nodes.Length; i++)
        {
            // Check both the public writer and a direct metadata change, which Apply has always observed.
            if (stage == 2) ActorLighting.SetEchelonIgnore(_nodes[i], style + 1);
            if (stage == 3) _nodes[i].SetMeta(Echelon, (float)(style + 8));
            if (stage == 4) ActorLighting.SetEchelonIgnore(_nodes[i], 0);
            if (stage == 5)
            {
                ActorLighting.SetEchelonIgnore(_nodes[i], style + 16);
                _nodes[i].RemoveMeta(Echelon);
            }
        }
        Original(_nodes[0], set);
        ActorLighting.Apply(_nodes[1], set);
        CheckUniforms();
        long writes = ActorLighting.UniformWrites;
        ActorLighting.Apply(_nodes[1], set);
        if (ActorLighting.UniformWrites != writes) throw new InvalidOperationException("unchanged actor lights were uploaded twice");

        // The caller owns its mutable set. Retaining the object/array instead of copying values loses this edit.
        if (stage == 5)
        {
            set.Lights[2] = set.Lights[2] with { Col = new Vector4(.31f, .12f, .05f, 2f) };
            Original(_nodes[0], set);
            ActorLighting.Apply(_nodes[1], set);
            if (ActorLighting.UniformWrites != writes + 1) throw new InvalidOperationException("one changed light colour did not cause exactly one upload");
            CheckUniforms();
        }
    }

    private void CheckUniforms()
    {
        Check(Ambient);
        for (int i = 0; i < 4; i++) { Check(Positions[i]); Check(Colours[i]); Check(Spots[i]); }
        void Check(StringName name)
        {
            if (_nodes[0].GetInstanceShaderParameter(name).AsVector4() != _nodes[1].GetInstanceShaderParameter(name).AsVector4())
                throw new InvalidOperationException("retained actor uniform differs: " + name);
        }
    }

    private static ActorLightSet Set(int stage, int style)
    {
        var set = new ActorLightSet { Ambient = new Vector3(.1f + style * .02f, .14f, .18f), Count = stage switch { 2 => 3, 3 => 1, 4 => 0, _ => 4 } };
        for (int i = 0; i < 4; i++)
            set.Lights[i] = new PackedLight(new Vector4(-.8f + i * .5f, 1f, 3f, .2f + stage * .03f),
                new Vector4(.12f + i * .03f, .1f, .07f + stage * .01f, 2f), new Vector4(0, 0, -1, i == 3 ? .7f : -2f));
        return set;
    }

    // Preserve the original path for timing and differential checks, including metadata lookup and cleared slots.
    private static void Original(GeometryInstance3D node, ActorLightSet set)
    {
        float code = node.HasMeta(Echelon) ? (float)node.GetMeta(Echelon) : 0f;
        OpenScctGame.Render.InstanceParams.Set(node, Ambient, new Vector4(set.Ambient.X, set.Ambient.Y, set.Ambient.Z, code));
        for (int i = 0; i < 4; i++)
        {
            PackedLight p = i < set.Count ? set.Lights[i] : default;
            OpenScctGame.Render.InstanceParams.Set(node, Positions[i], p.Pos);
            OpenScctGame.Render.InstanceParams.Set(node, Colours[i], p.Col);
            OpenScctGame.Render.InstanceParams.Set(node, Spots[i], i < set.Count ? p.Spot : new Vector4(0, 0, 0, -2));
        }
    }

    public string Measure()
    {
        const int iterations = 8192;
        ActorLightSet set = Set(0, 0);
        ActorLightSet first = set;
        var alternate = new ActorLightSet { Ambient = new Vector3(.13f, .15f, .19f), Count = 4 };
        for (int i = 0; i < 2048; i++) { Original(_nodes[0], set); ActorLighting.Apply(_nodes[1], set); }
        long writes = ActorLighting.UniformWrites, allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) ActorLighting.Apply(_nodes[1], set);
        double retained = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (ActorLighting.UniformWrites != writes || allocated > 4096)
            throw new InvalidOperationException($"steady actor uploads wrote {ActorLighting.UniformWrites - writes} uniforms and allocated {allocated} bytes");
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) Original(_nodes[0], set);
        double original = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        // Worst changing case: ambient plus every light's position, colour and spot vector changes on every call.
        for (int i = 0; i < 2048; i++) { Change(i); Original(_nodes[0], set); ActorLighting.Apply(_nodes[1], set); }
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) { Change(i); Original(_nodes[0], set); }
        double changingOriginal = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        writes = ActorLighting.UniformWrites;
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) { Change(i); ActorLighting.Apply(_nodes[1], set); }
        double changingRetained = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (ActorLighting.UniformWrites - writes != iterations * 13)
            throw new InvalidOperationException("fully changing benchmark did not upload all thirteen uniforms per call");
        CheckUniforms();
        return $"{iterations} static uploads: {original:F2} -> {retained:F2} ms, {iterations * 13} -> 0 native writes, {allocated} allocated bytes; fully changing {changingOriginal:F2} -> {changingRetained:F2} ms";

        void Change(int iteration)
        {
            set = (iteration & 1) == 0 ? first : alternate;
            float t = iteration * .00001f;
            for (int i = 0; i < 4; i++)
                set.Lights[i] = new PackedLight(new Vector4(i + t, t, 3, 1), new Vector4(t, .2f, .3f, 2), new Vector4(t, 0, -1, -2));
        }
    }
}
