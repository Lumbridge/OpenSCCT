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

// The abandoned projections of the client world (Projector.AbandonProjector): the flare's burn mark, the shotgun's
// pellet marks and the footprints.
public partial class CombatFxView
{
    private readonly Dictionary<int, MeshInstance3D> _projections = new();
    private readonly List<int> _projectionsGone = new();
    private readonly Dictionary<string, StandardMaterial3D?> _projectionMaterials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderMaterial> _tintedProjectionMaterials = new(StringComparer.Ordinal);
    private Shader? _tintedProjectionShader;
    private AbandonedProjectors? _projectionList;
    private int _lastProjection;
    private readonly Dictionary<MeshInstance3D, (Actor Surface, Transform3D Local)> _decalAttachments = new();

    private void AttachDecal(MeshInstance3D node, Actor? surface, FVector location, FRotator rotation)
    {
        _decalAttachments.Remove(node);
        if (surface is null or LevelInfo)
        {
            return;
        }

        var pose = new Transform3D(UeSpace.RotationToGodot(rotation), UeSpace.ToGodot(location));
        _decalAttachments[node] = (surface, pose.AffineInverse() * node.GlobalTransform);
        UpdateDecalAttachment(node);
    }

    private void UpdateDecalAttachment(MeshInstance3D node)
    {
        if (_decalAttachments.TryGetValue(node, out var attachment))
        {
            Actor surface = attachment.Surface;
            node.GlobalTransform = new Transform3D(UeSpace.RotationToGodot(surface.Rotation), UeSpace.ToGodot(surface.Location)) * attachment.Local;
            node.Visible = ImpactMark.IsDrawn(surface);
        }
    }

    /// <summary>Abandoned projections drawn so far.</summary>
    public int Projections { get; private set; }

    /// <summary>
    /// New projections get a quad on the first surface along their forward axis within MaxTraceDistance, facing out of
    /// it; each goes at its time.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The projection is not decoded: a quad of the texture's size x DrawScale (an orthographic projector's frustum) on the surface hit along the projector's axis, alpha blended")]
    private void TrackProjections(Level level)
    {
        float now = level.Info.TimeSeconds;
        _projectionsGone.Clear();
        foreach ((int serial, MeshInstance3D node) in _projections)
        {
            if (!IsLive(level, serial, now))
            {
                node.QueueFree();
                _decalAttachments.Remove(node);
                _projectionsGone.Add(serial);
            }
            else
            {
                UpdateDecalAttachment(node);
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
                _decalAttachments.Remove(old);
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
        if (ProjectionTexture(p.ProjTexture.Path) is not { } texture || GadgetWorld.Collision(level.Info) is not { } world)
        {
            return null;
        }

        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector end = p.Location + (x * p.MaxTraceDistance);
        if (!world.LineCheck(out FCheckResult hit, null, end, p.Location, FVector.Zero, (ETraceFlags)ParticleK.TraceFlags) || !hit.bHit)
        {
            return null;
        }

        // The shotgun's scripted scale grows with range. Our flat quad approximation cannot clip the
        // projection like the original projector, so cap each mark at its class-default scale instead
        // of stretching eight overlapping textures across the wall. Keep the range-dependent spread.
        float drawScale = p.Class == new Name("SShotGunProjector") || p.Class == new Name("SBase.SShotGunProjector")
            ? Math.Clamp(p.DrawScale, 0f, 0.3f)
            : p.DrawScale;
        // A projector of FOV near 0 is orthographic: its frustum is the texture's size times DrawScale.
        var node = new MeshInstance3D
        {
            Name = p.Class.ToString(),
            Mesh = new QuadMesh
            {
                Size = new Vector2(UeSpace.LengthToGodot(texture.GetWidth() * drawScale), UeSpace.LengthToGodot(texture.GetHeight() * drawScale)),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (p.Tint is { } tint)
        {
            node.MaterialOverride = TintedProjectionMaterial(p.ProjTexture.Path!, texture);
            node.SetInstanceShaderParameter("svm_tint", new Color(tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f));
            node.SetInstanceShaderParameter("svm_flip_v", p.FlipV ? 1f : 0f);
        }
        else if (ProjectionMaterial(p.ProjTexture.Path) is { } material)
        {
            node.MaterialOverride = material;
        }

        AddChild(node);
        node.GlobalTransform = new Transform3D(ProjectionBasis(hit.Normal, y, z), UeSpace.ToGodot(hit.Location + (hit.Normal * 0.4f)));
        AttachDecal(node, hit.Actor, hit.Actor?.Location ?? default, hit.Actor?.Rotation ?? default);
        return node;
    }

    /// <summary>
    /// The quad's frame on the surface: facing out of it (the quad's +Z), with the texture's U (the quad's +X) running
    /// against the projector's Z axis laid onto the surface (its Y axis when Z is along the normal). For a footprint
    /// cast straight down under the pawn's yaw the print's length runs along the walk, toes (U = 0) forward.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The projector's texture axes are not decoded: U against the projector's Z (the footprint texture's toes are at U = 0)")]
    private static Basis ProjectionBasis(FVector surfaceNormal, FVector projectorY, FVector projectorZ)
    {
        Vector3 n = UeSpace.DirectionToGodot(surfaceNormal).Normalized();
        Vector3 u = OnSurface(UeSpace.DirectionToGodot(projectorZ * -1f), n);
        if (u.LengthSquared() < 1e-6f)
        {
            u = OnSurface(UeSpace.DirectionToGodot(projectorY), n);
        }

        if (u.LengthSquared() < 1e-6f)
        {
            u = OnSurface(MathF.Abs(n.Y) > 0.99f ? Vector3.Right : Vector3.Up, n);
        }

        u = u.Normalized();
        return new Basis(u, n.Cross(u).Normalized(), n);
    }

    private static Vector3 OnSurface(Vector3 d, Vector3 n) => d - (n * d.Dot(n));

    private Texture2D? ProjectionTexture(string? texture) =>
        texture is not null && _library is { Available: true } lib ? lib.Texture(lib.TexturePng(texture)) : null;

    /// <summary>
    /// A projection whose material was built at run time (SFootPrint's Combiner): the colour is the tint's, the
    /// opacity the texture's alpha times the tint's (CO_Use_Color_From_Material2, AO_Multiply), alpha blended; the
    /// right foot's TexScaler turns the texture upside down.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SFootPrint.PostBeginPlay: Combiner CO_Use_Color_From_Material2, AO_Multiply; TexScaler VScale -1, VOffset 32; FrameBufferBlendingOp PB_AlphaBlend")]
    private ShaderMaterial TintedProjectionMaterial(string path, Texture2D texture)
    {
        if (!_tintedProjectionMaterials.TryGetValue(path, out ShaderMaterial? material))
        {
            _tintedProjectionShader ??= new Shader { Code = TintedProjectionShaderCode };
            material = new ShaderMaterial { Shader = _tintedProjectionShader };
            material.SetShaderParameter("svm_tex", texture);
            _tintedProjectionMaterials[path] = material;
        }

        return material;
    }

    // The tint is an sRGB colour; the unshaded albedo is linear.
    private const string TintedProjectionShaderCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;

uniform sampler2D svm_tex : filter_linear_mipmap, repeat_disable;
instance uniform vec4 svm_tint = vec4(1.0);
instance uniform float svm_flip_v = 0.0;

void fragment() {
    vec2 uv = UV;
    if (svm_flip_v > 0.5) {
        uv.y = 1.0 - uv.y;
    }

    ALBEDO = pow(svm_tint.rgb, vec3(2.2));
    ALPHA = texture(svm_tex, uv).a * svm_tint.a;
}
";

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
