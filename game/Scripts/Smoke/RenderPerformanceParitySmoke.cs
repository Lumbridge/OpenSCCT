using System;
using System.Collections.Generic;
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
                        RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                    };
                    context.Tree.Root.AddChild(_views[i]);
                    _views[i].AddChild(new Camera3D
                    {
                        Position = new Vector3(0, 0, 5), Projection = Camera3D.ProjectionType.Orthogonal,
                        Size = 4, Current = true,
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
                Prepare(context.Frame);
                return SmokeResult.Running;
            }

            if (context.Frame <= _changedAt + 1) return SmokeResult.Running;
            using Image expected = _views[0].GetTexture().GetImage();
            using Image actual = _views[1].GetTexture().GetImage();
            if (!expected.GetData().AsSpan().SequenceEqual(actual.GetData()))
            {
                string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "performance-overnight", "render-parity");
                Directory.CreateDirectory(output);
                expected.SavePng(Path.Combine(output, $"case-{_case}-expected.png"));
                actual.SavePng(Path.Combine(output, $"case-{_case}-actual.png"));
                Cleanup();
                return SmokeResult.Fail($"particle pixels differ at case {_case}; images in {output}");
            }

            if (++_case == Counts.Length * 7 * Visions.Length)
            {
                Cleanup();
                return SmokeResult.Pass($"{_lightChecks} exact actor-light comparisons, {_mapCandidates}/{_allMapCandidates} map candidates; {_case} exact particle image comparisons (all draw styles, normal/thermal/EMF, count growth/shrink/empty/reappear)");
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
        RenderingServer.GlobalShaderParameterSet("svm_vision", vision);
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
                        || actual.Lights[i] != ActorLighting.PackWorld(ranked[i].Light, WorldLights.Modulation(ranked[i].Light, time)))
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
