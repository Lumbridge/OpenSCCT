using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core.Engine;

namespace OpenScctGame.Render;

/// <summary>Draw real scene layouts while the browser loading cover is still up.</summary>
internal sealed class BrowserSceneWarmup : IDisposable
{
    private const int BatchSize = 64;
    private static readonly int[] Visions =
    [RenderK.RendMapNormal, RenderK.RendMapNight, RenderK.RendMapThermal, RenderK.RendMapMotion, RenderK.RendMapEmf];
    private readonly List<Node3D> _sources = [];
    private readonly Node3D _samples;
    private readonly FaithfulView? _view;
    private readonly int _restoreVision;
    private readonly List<Node3D> _batch = [];
    private int _offset, _vision;
    private bool _disposed;

    internal int SourceCount => _sources.Count;
    internal bool Complete { get; private set; }

    internal BrowserSceneWarmup(Node scene, Node3D samples, FaithfulView? view)
    {
        _samples = samples;
        _view = view;
        _restoreVision = view?.RendMap ?? RenderK.RendMapNormal;
        var seen = new HashSet<(Mesh.ArrayFormat, Mesh.PrimitiveType, ulong, ulong, ulong, GeometryInstance3D.ShadowCastingSetting, bool)>();
        var roots = new HashSet<ulong>();
        var pending = new Stack<Node>();
        pending.Push(scene);
        while (pending.TryPop(out Node? node))
        {
            // Do not copy loading samples, sky viewports or their worlds back into the sample viewport.
            if (node is SubViewport) continue;
            foreach (Node child in node.GetChildren()) pending.Push(child);
            if (node is not MeshInstance3D { Mesh: { } mesh } instance) continue;
            bool wanted = false;
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Material? material = instance.MaterialOverride ?? instance.GetSurfaceOverrideMaterial(surface) ?? mesh.SurfaceGetMaterial(surface);
                if (material is null) continue;
                var format = mesh is ArrayMesh arrays ? arrays.SurfaceGetFormat(surface) : 0;
                var primitive = mesh is ArrayMesh arrayMesh ? arrayMesh.SurfaceGetPrimitiveType(surface) : Mesh.PrimitiveType.Triangles;
                wanted |= seen.Add((format, primitive, mesh is ArrayMesh ? 0 : mesh.GetInstanceId(),
                    material.GetInstanceId(), instance.MaterialOverlay?.GetInstanceId() ?? 0, instance.CastShadow,
                    instance.GlobalBasis.Determinant() < 0));
            }
            if (!wanted) continue;
            Node3D root = ModelRoot(instance, scene);
            if (roots.Add(root.GetInstanceId())) _sources.Add(root);
        }
        PresentBatch();
    }

    // Keep a skinned mesh and its skeleton together, including their relative NodePaths.
    private static Node3D ModelRoot(MeshInstance3D mesh, Node scene)
    {
        if (mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) is not { } skeleton) return mesh;
        Node? parent = mesh;
        for (int i = 0; i < 8 && parent is not null && parent != scene; i++, parent = parent.GetParent())
            if (parent is Node3D root && (root == skeleton || root.IsAncestorOf(skeleton))) return root;
        return mesh;
    }

    /// <summary>The preceding batch has actually drawn; only then prepare the next one.</summary>
    internal void AfterDraw()
    {
        if (Complete || _disposed) return;
        _offset += BatchSize;
        if (_offset >= _sources.Count)
        {
            _offset = 0;
            if (++_vision >= Visions.Length)
            {
                Complete = true;
                ClearBatch();
                RestoreVision();
                return;
            }
        }
        PresentBatch();
    }

    private void PresentBatch()
    {
        ClearBatch();
        if (_view is not null) _view.RendMap = Visions[_vision];
        else Materials.MaterialCompiler.SetVision(Visions[_vision]);
        for (int i = _offset; i < Math.Min(_offset + BatchSize, _sources.Count); i++)
        {
            Node3D source = _sources[i];
            var sample = (Node3D)source.Duplicate();
            sample.Transform = source.GlobalTransform;
            // Shader/pipeline creation requires a draw, even for rooms and hidden device states the player
            // has not seen. Large bounds prevent frustum/occlusion rejection without reading GPU mesh arrays.
            var pending = new Stack<Node>();
            pending.Push(sample);
            while (pending.TryPop(out Node? node))
            {
                if (node is Node3D spatial) spatial.Visible = true;
                if (node is GeometryInstance3D geometry)
                {
                    geometry.ExtraCullMargin = 100000;
                    geometry.IgnoreOcclusionCulling = true;
                }
                foreach (Node child in node.GetChildren()) pending.Push(child);
            }
            _samples.AddChild(sample);
            _batch.Add(sample);
        }
    }

    private void ClearBatch()
    {
        foreach (Node3D sample in _batch)
        {
            _samples.RemoveChild(sample);
            sample.Free();
        }
        _batch.Clear();
    }

    private void RestoreVision()
    {
        if (_view is not null && GodotObject.IsInstanceValid(_view)) _view.RendMap = _restoreVision;
        else Materials.MaterialCompiler.SetVision(_restoreVision);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearBatch();
        RestoreVision();
    }
}
