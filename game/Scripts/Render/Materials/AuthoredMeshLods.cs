using System;
using System.Linq;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>Render-only simplification of generated material geometry. Collision always keeps the full shape.</summary>
public static class AuthoredMeshLods
{
    public static Godot.Collections.Dictionary Create(Godot.Collections.Array arrays, ulong flags = 0)
    {
        var result = new Godot.Collections.Dictionary();
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (positions.Length < 96) return result;
        using var importer = new ImporterMesh();
        using var copy = arrays.Duplicate();
        if (copy[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil) copy[(int)Mesh.ArrayType.Index] = Enumerable.Range(0, positions.Length).ToArray();
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        using var empty = new Godot.Collections.Dictionary();
        using var bones = new Godot.Collections.Array();
        importer.AddSurface(Mesh.PrimitiveType.Triangles, copy, blendShapes, empty, null, "", flags);
        importer.GenerateLods(60, 25, bones);
        for (int i = 0; i < importer.GetSurfaceLodCount(0); i++) result[importer.GetSurfaceLodSize(0, i)] = importer.GetSurfaceLodIndices(0, i);
        GC.KeepAlive(blendShapes);
        return result;
    }
}
