using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Fx;

// The abandoned projections of the client world (Projector.AbandonProjector): the flare's burn mark.
public partial class CombatFxView
{
    private readonly Dictionary<int, MeshInstance3D> _projections = new();
    private readonly List<int> _projectionsGone = new();
    private readonly Dictionary<string, StandardMaterial3D?> _projectionMaterials = new(StringComparer.Ordinal);
    private AbandonedProjectors? _projectionList;
    private int _lastProjection;

    /// <summary>Abandoned projections drawn so far.</summary>
    public int Projections { get; private set; }

    /// <summary>
    /// New projections get a quad on the first surface along their forward axis within MaxTraceDistance, facing out of
    /// it; each goes at its time.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The projection is not decoded: a square of the texture's width x DrawScale on the surface hit along the projector's axis, alpha blended")]
    private void TrackProjections(Level level)
    {
        float now = level.Info.TimeSeconds;
        _projectionsGone.Clear();
        foreach ((int serial, MeshInstance3D node) in _projections)
        {
            if (!IsLive(level, serial, now))
            {
                node.QueueFree();
                _projectionsGone.Add(serial);
            }
        }

        foreach (int serial in _projectionsGone)
        {
            _projections.Remove(serial);
        }

        if (level.GetService<AbandonedProjectors>() is not { } list)
        {
            return;
        }

        if (!ReferenceEquals(list, _projectionList))
        {
            // A new level: its serials start again.
            foreach (MeshInstance3D old in _projections.Values)
            {
                old.QueueFree();
            }

            _projections.Clear();
            _projectionList = list;
            _lastProjection = 0;
        }

        foreach (AbandonedProjection p in list.Live)
        {
            if (p.Serial <= _lastProjection)
            {
                continue;
            }

            _lastProjection = p.Serial;
            if (p.Expires > now && ProjectionNode(level, p) is { } node)
            {
                _projections[p.Serial] = node;
                Projections++;
            }
        }
    }

    private static bool IsLive(Level level, int serial, float now)
    {
        if (level.GetService<AbandonedProjectors>() is not { } list)
        {
            return false;
        }

        foreach (AbandonedProjection p in list.Live)
        {
            if (p.Serial == serial)
            {
                return p.Expires > now;
            }
        }

        return false;
    }

    private MeshInstance3D? ProjectionNode(Level level, AbandonedProjection p)
    {
        if (ProjectionMaterial(p.ProjTexture.Path) is not { } material || GadgetWorld.Collision(level.Info) is not { } world)
        {
            return null;
        }

        p.Rotation.GetAxes(out FVector x, out _, out _);
        FVector end = p.Location + (x * p.MaxTraceDistance);
        if (!world.LineCheck(out FCheckResult hit, null, end, p.Location, FVector.Zero, (ETraceFlags)ParticleK.TraceFlags) || !hit.bHit)
        {
            return null;
        }

        float size = material.AlbedoTexture.GetWidth() * p.DrawScale;
        var node = new MeshInstance3D
        {
            Name = p.Class.ToString(),
            Mesh = new QuadMesh { Size = Vector2.One * UeSpace.LengthToGodot(size) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        node.GlobalPosition = UeSpace.ToGodot(hit.Location + (hit.Normal * 0.4f));

        // The quad faces +Z; turn it to face out of the surface.
        Vector3 n = UeSpace.DirectionToGodot(hit.Normal);
        Vector3 up = MathF.Abs(n.Y) > 0.99f ? Vector3.Right : Vector3.Up;
        node.LookAt(node.GlobalPosition - n, up);
        return node;
    }

    private StandardMaterial3D? ProjectionMaterial(string? texture)
    {
        if (texture is null || _library is not { Available: true } lib)
        {
            return null;
        }

        if (!_projectionMaterials.TryGetValue(texture, out StandardMaterial3D? material))
        {
            material = lib.Texture(lib.TexturePng(texture)) is { } t
                ? new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoTexture = t,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                }
                : null;
            _projectionMaterials[texture] = material;
        }

        return material;
    }
}
