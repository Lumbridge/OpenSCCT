using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render;

/// <summary>
/// Laser detector beams (SLaserBeam, owned by an SLaserTrigger; the mine laser). Each beam is the class mesh
/// (SGameplayObjects LasDetectBeam, 1 uu along X) from the render pack, unlit, stretched to the beam's length
/// (DrawScale3D.X) and pointed from the start to the end. In thermal (36) and EMF (38) every beam draws as a vision-only
/// node of the map with its own vision material (svm_laser.gdshader): the beam's HeatIntensity in thermal, a full EMF
/// glow in EMF. A beam the map does not hide in normal vision (SLaserTrigger.bHideLaserInNormalVision false, so the
/// beam's bHideInNormalVision is false) also draws in the other modes (normal, night, motion) as the red additive
/// LaserBeamShader streak. docs/RENDERING.md#vision-modes, spec/devices/laser-trigger.md#drawing.
/// </summary>
[PortOf("eng:Render.LaserBeams", Spec = "docs/RENDERING.md#vision-modes")]
public sealed class Lasers
{
    /// <summary>The detector beam mesh (SLaserBeam's class mesh is LaserBeam; SLaserTrigger spawns LasDetectBeam beams).</summary>
    [Provenance(ProvenanceKind.Decoded, "bridge docs/EFFECTS.md: LasDetectBeam is 1 uu long, the game sets DrawScale3D.X = LaserLength - 1")]
    public const string BeamMesh = "SGameplayObjects.Objects.LasDetectBeam";

    public const string ShaderPath = "res://Shaders/svm_laser.gdshader";

    private readonly RenderMap _map;
    private readonly Node3D _root = new() { Name = "Lasers" };
    private readonly Dictionary<string, MeshInstance3D> _beams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MeshInstance3D> _normal = new(StringComparer.Ordinal);
    private readonly Dictionary<float, ShaderMaterial> _heatMaterials = new();
    private readonly ArrayMesh? _mesh;
    private readonly ShaderMaterial _material;
    private StandardMaterial3D? _normalMaterial;
    private bool _normalMaterialTried;

    public Lasers(RenderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        map.Root.AddChild(_root);
        _mesh = GameplayObjects.MeshFor(map, BeamMesh, unlit: true);
        Materials.MaterialCompiler.EnsureGlobals();
        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _material.SetShaderParameter("heat", Vision.X);
    }

    /// <summary>False when the render pack has no beam mesh.</summary>
    public bool Available => _mesh is not null;

    public int Count => _beams.Count;

    /// <summary>The ids of the beams placed now.</summary>
    public IEnumerable<string> Ids => _beams.Keys;

    /// <summary>Places (or moves) the vision-only beam <paramref name="id"/> from <paramref name="start"/> to <paramref name="end"/> (uu).</summary>
    public void Set(string id, FVector start, FVector end) => Set(id, start, end, hideInNormalVision: true, heat: Vision.X);

    /// <summary>
    /// Places (or moves) the beam <paramref name="id"/> from <paramref name="start"/> to <paramref name="end"/> (uu), with
    /// the beam actor's bHideInNormalVision and HeatIntensity.
    /// </summary>
    public void Set(string id, FVector start, FVector end, bool hideInNormalVision, float heat)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_mesh is null)
        {
            return;
        }

        FVector d = end - start;
        float length = d.Size();
        FRotator rotation = length > 0f ? d.Rotation() : default;
        Transform3D transform = UeSpace.TransformToGodot(start, rotation, new FVector(MathF.Max(length, 0.01f), 1f, 1f), default);
        ShaderMaterial heatMaterial = HeatMaterial(heat);
        if (!_beams.TryGetValue(id, out MeshInstance3D? node))
        {
            node = new MeshInstance3D { Name = id, Mesh = _mesh, MaterialOverride = heatMaterial };
            _root.AddChild(node);
            _beams[id] = node;
            _map.AddVisionOnly(node);
        }
        else if (!ReferenceEquals(node.MaterialOverride, heatMaterial))
        {
            node.MaterialOverride = heatMaterial;
        }

        node.Transform = transform;
        if (hideInNormalVision)
        {
            RemoveNormal(id);
            return;
        }

        if (!_normal.TryGetValue(id, out MeshInstance3D? normal))
        {
            normal = new MeshInstance3D
            {
                Name = id + "_normal",
                Mesh = _mesh,
                MaterialOverride = NormalMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _root.AddChild(normal);
            _normal[id] = normal;
        }

        normal.Transform = transform;

        // The scene-vision modes draw the beam from its vision node only.
        normal.Visible = !_map.VisionOnlyShown;
    }

    /// <summary>Removes the beam <paramref name="id"/>.</summary>
    public void Remove(string id)
    {
        if (_beams.Remove(id, out MeshInstance3D? node))
        {
            _map.RemoveVisionOnly(node);
            node.QueueFree();
        }

        RemoveNormal(id);
    }

    /// <summary>SLaserBeam's vision values: HeatIntensity 1 (class default), HeatOpacity 1, no EMF glow, EMFOpacity 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SLaserBeam defaults HeatIntensity 1.0")]
    public static readonly Vector4 Vision = new(1f, 1f, 0f, 1f);

    private void RemoveNormal(string id)
    {
        if (_normal.Remove(id, out MeshInstance3D? normal))
        {
            normal.QueueFree();
        }
    }

    private ShaderMaterial HeatMaterial(float heat)
    {
        if (heat == Vision.X)
        {
            return _material;
        }

        if (!_heatMaterials.TryGetValue(heat, out ShaderMaterial? material))
        {
            material = new ShaderMaterial { Shader = _material.Shader };
            material.SetShaderParameter("heat", heat);
            _heatMaterials[heat] = material;
        }

        return material;
    }

    /// <summary>The normal-vision look: the LaserBeamShader texture added to the picture (the spy laser's material).</summary>
    [Provenance(ProvenanceKind.Estimated, "The detector beam's normal look reuses the spy beam's additive LaserBeamShader texture on the LasDetectBeam mesh; the TexScaler U scale is not applied")]
    private StandardMaterial3D? NormalMaterial()
    {
        if (!_normalMaterialTried)
        {
            _normalMaterialTried = true;
            _normalMaterial = OpenScctGame.Fx.SpyLaserView.AdditiveBeamMaterial(_map);
        }

        return _normalMaterial;
    }
}
