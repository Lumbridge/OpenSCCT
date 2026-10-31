using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// Light coronas (bCorona): one camera-facing additive quad per corona light, sized and faded each frame from the
/// view as the original does. With s = clamp(0.001 x distance, 0.01, 10): half size clamp(s x DrawScale x Fade x
/// 512 x DrawScale3D, MinCoronaSize, MaxCoronaSize) uu, roll s x CoronaRotation x 16384 + CoronaRotationOffset,
/// colour corona colour x light type x 0.5 x Fade. Fade moves at 6 x ScaleGlow per second towards the light being on and in
/// range and seen: a line from the camera to 16 uu short of the light that the world BSP does not block (or settles at
/// once with <see cref="Update"/>(settle: true)).
/// docs/RENDERING.md#coronas.
/// </summary>
[PortOf("eng:Render.Coronas", Spec = "docs/RENDERING.md#coronas")]
public sealed class Coronas
{
    public const string ShaderPath = "res://Shaders/svm_corona.gdshader";

    private static readonly StringName SizeParam = "svm_corona";
    private static readonly StringName ColourParam = "svm_corona_colour";

    private readonly RenderMap _map;
    private readonly List<(CoronaEntry Entry, MeshInstance3D Node)> _coronas = [];
    private readonly float[] _fade;

    // The light of each corona (for its light type), or null when the light list does not hold it.
    private readonly LightEntry?[] _lights;

    public Coronas(RenderMap map, Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parent);
        _map = map;
        var root = new Node3D { Name = "Coronas" };
        parent.AddChild(root);
        var shader = GD.Load<Shader>(ShaderPath);
        var materials = new Dictionary<string, ShaderMaterial>(StringComparer.Ordinal);
        var quad = new QuadMesh { Size = Vector2.One };
        foreach (CoronaEntry c in map.Lighting.Coronas)
        {
            if (c.Texture is null || map.Textures.Get(c.Texture) is not { } texture)
            {
                continue;
            }

            if (!materials.TryGetValue(c.Texture, out ShaderMaterial? material))
            {
                material = new ShaderMaterial { Shader = shader };
                material.SetShaderParameter("corona_tex", texture);
                materials[c.Texture] = material;
            }

            var node = new MeshInstance3D
            {
                Name = "Corona_" + c.Light,
                Mesh = quad,
                MaterialOverride = material,
                Position = UeSpace.ToGodot(c.Location),
                ExtraCullMargin = UeSpace.LengthToGodot(c.MaxSize * 2f),
                Visible = false,
                Layers = RenderLayers.Effects,
            };
            root.AddChild(node);
            _coronas.Add((c, node));
        }

        _fade = new float[_coronas.Count];
        var byName = new Dictionary<string, LightEntry>(StringComparer.Ordinal);
        foreach (LightEntry l in map.Lighting.Lights)
        {
            byName.TryAdd(l.Name, l);
        }

        _lights = new LightEntry?[_coronas.Count];
        for (int i = 0; i < _coronas.Count; i++)
        {
            _lights[i] = byName.GetValueOrDefault(_coronas[i].Entry.Light);
        }
    }

    public int Count => _coronas.Count;

    /// <summary>False hides every corona (thermal and EMF vision draw none).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sizes, rolls and fades every corona for a camera at <paramref name="camera"/> (uu).</summary>
    [Provenance(ProvenanceKind.Decoded, "corona draw maths, game disassembly (bridge docs/EFFECTS.md, Coronas)")]
    public void Update(FVector camera, float delta, bool settle)
    {
        for (int i = 0; i < _coronas.Count; i++)
        {
            (CoronaEntry c, MeshInstance3D node) = _coronas[i];
            if (!Enabled)
            {
                node.Visible = false;
                continue;
            }

            c = Placed(c, node);
            bool visible = _map.IsLightOn(c.Light, c.On) && Visible(c, camera);
            float target = visible ? 1f : 0f;
            _fade[i] = settle ? target : Mathf.MoveToward(_fade[i], target, 6f * c.ScaleGlow * delta);
            float fade = _fade[i];
            if (fade <= 0f)
            {
                node.Visible = false;
                continue;
            }

            float dist = FVector.Dist(camera, c.Location);
            float s = Math.Clamp(0.001f * dist, 0.01f, 10f);
            float baseSize = s * c.DrawScale * fade * 512f;
            float half = Math.Clamp(baseSize * MathF.Max(c.Scale3D.X, c.Scale3D.Y), c.MinSize, c.MaxSize);
            float roll = ((s * c.Rotation01 * 16384f) + c.RotationOffset) * (MathF.Tau / 65536f);
            node.Visible = true;
            node.SetInstanceShaderParameter(SizeParam, new Vector4(UeSpace.LengthToGodot(half), roll, 0f, 0f));
            // Estimated (the corona colour's source is not decoded): the corona follows the light's FDynamicLight colour,
            // which carries its light type, so a pulsing or flickering light's corona pulses and flickers with it.
            float m = _lights[i] is { } light ? WorldLights.Modulation(light, _map.Time) : 1f;
            Color k = c.Colour * (0.5f * fade * m * RenderK.CoronaStageGain);
            node.SetInstanceShaderParameter(ColourParam, new Vector4(k.R, k.G, k.B, 1f));
        }
    }

    /// <summary>
    /// The corona where its light is now: a light riding a mover or hung on a bone (<see cref="RenderMap.MoveLight"/>)
    /// takes its corona along.
    /// </summary>
    private CoronaEntry Placed(CoronaEntry c, MeshInstance3D node)
    {
        if (!_map.TryGetLightPlace(c.Light, out FVector location, out FRotator rotation))
        {
            return c;
        }

        node.Position = UeSpace.ToGodot(location);
        return c with { Location = location, Rotation = rotation };
    }

    /// <summary>One line per corona in front of the camera (light, distance, on, trace result, fade), for calibration logs.</summary>
    public IEnumerable<string> Describe(FVector camera, FRotator view)
    {
        FVector forward = view.Vector();
        for (int i = 0; i < _coronas.Count; i++)
        {
            CoronaEntry c = _coronas[i].Entry;
            FVector to = c.Location - camera;
            float dist = to.Size();
            if (dist <= 0f || (to | forward) / dist < 0.5f)
            {
                continue;
            }

            yield return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{c.Light} dist={dist:F0} on={c.On} seen={Visible(c, camera)} fade={_fade[i]:F2} size={c.MinSize:F0}..{c.MaxSize:F0}");
        }
    }

    private bool Visible(CoronaEntry c, FVector camera)
    {
        FVector to = camera - c.Location;
        float dist = to.Size();
        if (c.Radius > 0f && dist > RenderK.LightRadiusUnits * (c.Radius + 1f))
        {
            return false;
        }

        if (c.Orient)
        {
            // Oriented coronas use the cone test instead of a trace.
            return dist > 0f && ((to / dist) | c.Rotation.Vector()) > 1f - (c.Cone / 256f);
        }

        if (dist <= 16f)
        {
            return true;
        }

        // A trace against the world BSP only, stopping 16 uu short of the light: static meshes and volumes do not hide
        // a corona (lamp heads, cables and blocking volumes would).
        return _map.BspLineOfSight(camera, c.Location + ((to / dist) * 16f));
    }
}
