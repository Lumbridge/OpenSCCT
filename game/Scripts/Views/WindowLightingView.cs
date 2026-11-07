using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Game.SBase;
using OpenScctGame.Render;

namespace OpenScctGame.Views;

/// <summary>Recombines the editor's cached direct and bounce lighting when replicated panes break or reset.</summary>
public sealed class WindowLightingView
{
    private readonly RenderMap map;
    private readonly WindowLightTransfer? data;
    private readonly Dictionary<string, SVitre> panes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> broken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ImageTexture Texture, byte[] Original, int Width, int Height, Image.Format Format)> textures = new();
    private readonly Dictionary<string, ArrayMesh> meshes = new();
    private bool first = true;
    private int revision, appliedRevision, pendingRevision;
    private Task<LightingChanges>? pending;
    private sealed record LightingChanges(
        Dictionary<(string Kind, string Name, int Surface), Dictionary<int, Vector3>> Values,
        Dictionary<(string Name, int Surface, int Element), (float[] Point, float[] Normal)> Positions);
    public int Updates { get; private set; }

    public WindowLightingView(RenderMap map, MapLoadResult loaded)
    {
        this.map = map;
        string file = Path.Combine(map.Folder, WindowLightTransfer.RelativePath);
        if (!File.Exists(file)) return;
        try
        {
            using var stream = File.OpenRead(file); data = WindowLightTransfer.Load(stream);
            foreach (var record in loaded.Gameplay.Actors)
                if (loaded.ActorOf(record) is SVitre pane) panes[record.Id] = pane;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        { GD.PushWarning("Window lighting could not load: " + e.Message); }
    }

    public void Update()
    {
        if (data is null) return;
        bool changed = first; first = false;
        foreach (var p in data.Panes)
        {
            bool gone = panes.TryGetValue(p.Actor, out SVitre? pane) && GlassBreakViews.IsBroken(pane);
            if (gone) changed |= broken.Add(p.Actor); else changed |= broken.Remove(p.Actor);
        }
        if (changed)
        {
            revision++;
            // The shipped bake already represents intact panes; avoid touching its GPU resources on initial load.
            if (broken.Count == 0 && Updates == 0) { Updates++; appliedRevision = revision; }
        }
        if (pending is { IsCompleted: true })
        {
            if (pending.IsCompletedSuccessfully)
            {
                // A second break/reset supersedes a calculation still in flight. Never apply the older state.
                if (pendingRevision == revision) { Apply(pending.Result); appliedRevision = revision; }
            }
            else
            {
                GD.PushWarning("Window lighting could not update: " + pending.Exception?.GetBaseException().Message);
                appliedRevision = revision;
            }
            pending = null;
        }
        if (pending is null && appliedRevision != revision)
        {
            pendingRevision = revision;
            var snapshot = new HashSet<string>(broken, StringComparer.OrdinalIgnoreCase);
            // Pure cached math runs off the render thread; all texture and mesh changes remain on this thread.
            pending = Task.Run(() => Calculate(snapshot));
        }
    }

    private LightingChanges Calculate(IReadOnlySet<string> state)
    {
        var changes = new Dictionary<(string Kind, string Name, int Surface), Dictionary<int, Vector3>>();
        var positions = new Dictionary<(string Name, int Surface, int Element), (float[] Point, float[] Normal)>();
        foreach (var batch in data!.Batches)
        {
            float[] values = data.EvaluateFlat(batch, state);
            for (int i = 0; i < batch.Samples.Count; i++)
            foreach (var dest in batch.Samples[i].Destinations)
            {
                var r = batch.Resources[dest.Resource]; var key = (r.Kind, r.Name, r.Surface);
                if (!changes.TryGetValue(key, out var pixels)) changes[key] = pixels = new();
                float[] initial = batch.Samples[i].Initial; int value = i * 3;
                var delta = new Vector3(WindowLightTransfer.Encode(values[value], dest.Dither) - WindowLightTransfer.Encode(initial[0], dest.Dither),
                    WindowLightTransfer.Encode(values[value+1], dest.Dither) - WindowLightTransfer.Encode(initial[1], dest.Dither),
                    WindowLightTransfer.Encode(values[value+2], dest.Dither) - WindowLightTransfer.Encode(initial[2], dest.Dither)) * dest.Weight;
                if (delta.LengthSquared() == 0) continue;
                pixels[dest.Element] = pixels.GetValueOrDefault(dest.Element) + delta;
                if (dest.Point is { } p && dest.Normal is { } n) positions[(r.Name, r.Surface, dest.Element)] = (p, n);
            }
        }
        return new(changes, positions);
    }

    private void Apply(LightingChanges response)
    {
        var changes = response.Values;
        var positions = response.Positions;
        foreach (var entry in changes)
            if (entry.Key.Kind == "texture") ApplyTexture(entry.Key.Name, entry.Value);
        var actors = new HashSet<string>();
        foreach (var key in changes.Keys) if (key.Kind == "mesh") actors.Add(key.Name);
        foreach (string actor in actors)
        {
            if (!map.Gameplay.TryGetValue(actor, out GameplayMesh? node) || node.Node.Mesh is not ArrayMesh current) continue;
            if (!meshes.TryGetValue(actor, out ArrayMesh? original)) meshes[actor] = original = current;
            var replacement = new ArrayMesh();
            for (int surface = 0; surface < original.GetSurfaceCount(); surface++)
            {
                var arrays = original.SurfaceGetArrays(surface);
                string name = original.SurfaceGetName(surface);
                int section = name.StartsWith("section", StringComparison.Ordinal) && int.TryParse(name.AsSpan(7), out int parsed) ? parsed : surface;
                if (changes.TryGetValue(("mesh", actor, section), out var edits))
                {
                    Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                    Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var byPoint = new Dictionary<(long, long, long, long, long, long), List<int>>();
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var key = VertexKey(vertices[v], normals.Length == vertices.Length ? normals[v] : default);
                        if (!byPoint.TryGetValue(key, out var indices)) byPoint[key] = indices = [];
                        indices.Add(v);
                    }
                    var written = new HashSet<int>();
                    foreach (var edit in edits)
                    {
                        if (!positions.TryGetValue((actor, section, edit.Key), out var p)) continue;
                        var key = VertexKey(new Vector3(p.Point[0], p.Point[1], p.Point[2]), new Vector3(p.Normal[0], p.Normal[1], p.Normal[2]));
                        if (!byPoint.TryGetValue(key, out var indices)) continue;
                        foreach (int v in indices) if (v < colors.Length && written.Add(v))
                        {
                            Color c = colors[v]; Vector3 d = edit.Value / 255f;
                            colors[v] = new Color(Math.Clamp(c.R + d.X, 0, 1), Math.Clamp(c.G + d.Y, 0, 1), Math.Clamp(c.B + d.Z, 0, 1), c.A);
                        }
                    }
                    arrays[(int)Mesh.ArrayType.Color] = colors;
                }
                replacement.AddSurfaceFromArrays(original.SurfaceGetPrimitiveType(surface), arrays);
                replacement.SurfaceSetMaterial(surface, original.SurfaceGetMaterial(surface)); replacement.SurfaceSetName(surface, name);
            }
            node.Node.Mesh = replacement;
        }
        Updates++;
    }
    private static (long, long, long, long, long, long) VertexKey(Vector3 p, Vector3 n) =>
        ((long)Math.Round(p.X * 100000), (long)Math.Round(p.Y * 100000), (long)Math.Round(p.Z * 100000),
         (long)Math.Round(n.X * 10000), (long)Math.Round(n.Y * 10000), (long)Math.Round(n.Z * 10000));
    private void ApplyTexture(string uri, Dictionary<int, Vector3> changes)
    {
        if (!textures.TryGetValue(uri, out var target))
        {
            if (map.Textures.Get(uri, mipmaps: false) is not ImageTexture texture || map.Textures.GetImage(uri) is not { } source) return;
            using var copy = Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, source.GetFormat(), source.GetData());
            copy.Convert(Image.Format.Rgba8);
            target = (texture, copy.GetData(), copy.GetWidth(), copy.GetHeight(), source.GetFormat()); textures[uri] = target;
        }
        byte[] bytes = (byte[])target.Original.Clone();
        foreach (var edit in changes)
        {
            int at = edit.Key * 4; if (at < 0 || at + 3 >= bytes.Length) continue;
            bytes[at] = (byte)Math.Clamp(Math.Round(bytes[at] + edit.Value.X), 0, 255);
            bytes[at + 1] = (byte)Math.Clamp(Math.Round(bytes[at + 1] + edit.Value.Y), 0, 255);
            bytes[at + 2] = (byte)Math.Clamp(Math.Round(bytes[at + 2] + edit.Value.Z), 0, 255);
        }
        using var image = Image.CreateFromData(target.Width, target.Height, false, Image.Format.Rgba8, bytes);
        image.Convert(target.Format); target.Texture.Update(image);
    }
}
