using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Converted mine mesh primitives for zero-extent traces against spawned actors. Map-placed meshes already have
/// colliders; spawned mines otherwise fall back to their small placement cylinder. Camera actors retain their
/// explicit bUseCylinderCollision cylinder. Geometry comes from the player's converted content.
/// </summary>
public sealed class GadgetCollision
{
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.OrdinalIgnoreCase);

    public static GadgetCollision Load(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = doc.RootElement;
        if (root.GetProperty("format").GetString() != "svm.gadgetcollision/1")
            throw new InvalidDataException($"Unsupported gadget collision format: {path}");

        var content = new GadgetCollision();
        foreach (JsonProperty entry in root.GetProperty("meshes").EnumerateObject())
        {
            var vertices = new List<FVector>();
            foreach (JsonElement v in entry.Value.GetProperty("vertices").EnumerateArray())
                vertices.Add(new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()));
            var triangles = new List<int>();
            foreach (JsonElement t in entry.Value.GetProperty("triangles").EnumerateArray())
            {
                for (int i = 0; i < 3; i++)
                {
                    int index = t[i].GetInt32();
                    if ((uint)index >= (uint)vertices.Count)
                        throw new InvalidDataException($"Invalid gadget collision triangle: {entry.Name}");
                    triangles.Add(index);
                }
            }
            content._meshes.Add(entry.Name, new Mesh([.. vertices], [.. triangles]));
        }
        return content;
    }

    internal bool TryGet(Actor actor, out Mesh? mesh)
    {
        mesh = null;
        return !actor.bUseCylinderCollision && actor.DrawType == EDrawType.DT_StaticMesh &&
            actor.StaticMesh.Path is { } asset && _meshes.TryGetValue(asset, out mesh);
    }

    internal sealed class Mesh(FVector[] vertices, int[] triangles)
    {
        // The radius is only a conservative broad phase around Location; detailed hits use the original triangles.
        private readonly float _radius = Radius(vertices);

        private static float Radius(FVector[] points)
        {
            float r = 0f;
            foreach (FVector v in points) r = SvmMath.Max(r, v.Size());
            return r;
        }

        [PortOf("eng:UStaticMesh::LineCheck", Spec = "spec/native/collision-primitives.md#ustaticmeshlinecheck")]
        [Provenance(ProvenanceKind.Decoded, "UStaticMesh::LineCheck uses mesh collision hull triangles for zero-extent fire; actor transform includes PrePivot and DrawScale3D")]
        internal bool LineCheck(Actor actor, FVector start, FVector delta, float limit, out float time, out FVector normal)
        {
            time = limit;
            normal = default;
            FVector scale = actor.DrawScale3D * actor.DrawScale;
            float largestScale = SvmMath.Max(SvmMath.Abs(scale.X), SvmMath.Max(SvmMath.Abs(scale.Y), SvmMath.Abs(scale.Z)));
            float reach = (_radius + actor.PrePivot.Size()) * largestScale;
            var half = new FVector(reach, reach, reach);
            if (!CollisionMath.BoxesTouch(actor.Location - half, actor.Location + half,
                    CollisionMath.MinV(start, start + delta), CollisionMath.MaxV(start, start + delta)))
                return false;

            var transform = new ColliderTransform(actor.Location, actor.Rotation, scale, actor.PrePivot);
            if (!transform.Valid) return false;
            bool hit = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var triangle = new TriPrim();
                if (triangle.Set(transform.TransformPoint(vertices[triangles[i]]),
                        transform.TransformPoint(vertices[triangles[i + 1]]), transform.TransformPoint(vertices[triangles[i + 2]])) &&
                    StaticMeshSweep.LineTriangle(triangle, start, delta, time, out float t, out FVector n))
                {
                    time = t;
                    normal = n;
                    hit = true;
                }
            }
            return hit;
        }
    }
}
