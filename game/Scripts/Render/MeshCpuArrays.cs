using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace OpenScctGame.Render;

/// <summary>
/// Keeps authored mesh arrays on the CPU for browser effects that clone or inspect them.
/// WebGPU cannot provide the synchronous GPU readback SurfaceGetArrays expects.
/// Desktop meshes keep using Godot's own storage.
/// </summary>
public static class MeshCpuArrays
{
    private sealed record Surface(Godot.Collections.Array Arrays,
        Godot.Collections.Array<Godot.Collections.Array> BlendShapes);
    private static readonly ConditionalWeakTable<ArrayMesh, Dictionary<int, Surface>> s_meshes = new();

    /// <summary>glTF's importer retains the authored arrays before they are uploaded to WebGPU.</summary>
    public static void Remember(ImporterMesh source)
    {
        if (!OperatingSystem.IsBrowser()) return;
        ArrayMesh mesh = source.GetMesh();
        var surfaces = s_meshes.GetOrCreateValue(mesh);
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            var blends = new Godot.Collections.Array<Godot.Collections.Array>();
            for (int b = 0; b < source.GetBlendShapeCount(); b++) blends.Add(source.GetSurfaceBlendShapeArrays(s, b).Duplicate());
            surfaces[s] = new Surface(source.GetSurfaceArrays(s).Duplicate(), blends);
        }
    }

    public static void Add(ArrayMesh mesh, Mesh.PrimitiveType primitive, Godot.Collections.Array arrays,
        Godot.Collections.Array<Godot.Collections.Array>? blendShapes = null,
        Godot.Collections.Dictionary? lods = null, Mesh.ArrayFormat flags = 0)
    {
        int index = mesh.GetSurfaceCount();
        mesh.AddSurfaceFromArrays(primitive, arrays, blendShapes, lods, flags);
        if (OperatingSystem.IsBrowser() && mesh.GetSurfaceCount() > index)
        {
            s_meshes.GetOrCreateValue(mesh)[index] = new Surface(arrays.Duplicate(),
                blendShapes?.Duplicate() ?? new Godot.Collections.Array<Godot.Collections.Array>());
        }
    }

    public static Godot.Collections.Array Get(Mesh mesh, int surface)
    {
        if (OperatingSystem.IsBrowser())
        {
            if (mesh is PrimitiveMesh primitive) return primitive.GetMeshArrays();
            if (mesh is ArrayMesh array && s_meshes.TryGetValue(array, out var surfaces) && surfaces.TryGetValue(surface, out var data))
                return data.Arrays.Duplicate();
        }
        return mesh.SurfaceGetArrays(surface);
    }

    public static Godot.Collections.Array<Godot.Collections.Array> BlendShapes(ArrayMesh mesh, int surface)
    {
        if (OperatingSystem.IsBrowser() && s_meshes.TryGetValue(mesh, out var surfaces) && surfaces.TryGetValue(surface, out var data))
            return data.BlendShapes.Duplicate();
        return mesh.SurfaceGetBlendShapeArrays(surface);
    }
}
