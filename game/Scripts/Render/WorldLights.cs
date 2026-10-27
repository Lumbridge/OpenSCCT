using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The dynamic world lights of a map: lights the original does not bake (triggered STriggerLights, pulsing and
/// other dynamic lights) that still light the world (bApplyToStaticLighting). The template adds them per pixel to
/// the baked light term of BSP and static mesh surfaces, with the same falloff, colour and 2x gain as actor
/// lighting. They live in a float texture (one row per light: position, colour, spot, zone), a global shader parameter,
/// so switching a light or animating its brightness is one texture update. A bAffectOwnZoneOnly light lights only
/// surfaces of its zone (each vertex carries its zone in the colour alpha). docs/RENDERING.md#dynamic-lights.
/// </summary>
[PortOf("eng:Render.DynamicLights", Spec = "docs/RENDERING.md#dynamic-lights")]
public sealed class WorldLights
{
    public const string TextureParameter = "svm_dyn_lights";
    public const string CountParameter = "svm_dyn_light_count";
    public const string TuneParameter = "svm_dyn_tune";

    // The names Update writes every frame, made once (a name passed as a string makes a StringName per call).
    private static readonly StringName s_textureName = TextureParameter;
    private static readonly StringName s_countName = CountParameter;
    private static readonly StringName s_tuneName = TuneParameter;
    private bool _textureSet;
    public const int MaxLights = 256;

    /// <summary>Rows kept for lights spawned during play (flash lights of explosions and shots).</summary>
    public const int MaxSpawned = 16;

    // The RenderingServer's global parameters outlive any map; this remembers that they were added.
    private static bool s_globalsAdded;

    private readonly List<LightEntry> _lights = [];
    private readonly List<LightEntry> _spawned = [];
    private float _time;
    private readonly bool[] _on;
    private readonly Image _image;
    private readonly ImageTexture _texture;

    public WorldLights(LightingDoc lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        foreach (LightEntry l in lighting.Lights)
        {
            if (IsDynamicWorldLight(l) && _lights.Count < MaxLights)
            {
                _lights.Add(l);
            }
        }

        _on = new bool[_lights.Count];
        for (int i = 0; i < _on.Length; i++)
        {
            _on[i] = _lights[i].InitiallyOn && _lights[i].LightType != 0;
        }

        _image = Image.CreateEmpty(4, _lights.Count + MaxSpawned, false, Image.Format.Rgbaf);
        _texture = ImageTexture.CreateFromImage(_image);
        EnsureGlobals();
    }

    public int Count => _lights.Count;

    /// <summary>
    /// Gain, radius scale and falloff of the dynamic world lights. The colour, gain and radius are the actor model's;
    /// the falloff over the world is 1 - (d / R)^2, which matched the original's lit rooms far better than the actor
    /// falloff 1 - 3x^2 + 2x^3 (SteeE shop: SSIM 0.70 to 0.81, luma ratio 0.54 to 1.00).
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "SteeE golden sweep of gain, radius and falloff (tools/golden, shop and yard-north views)")]
    public static readonly Vector4 DefaultTune = new(1f, 1f, 1f, 1f);

    /// <summary>Overrides the calibration (gain, radius scale, falloff mode) for calibration runs.</summary>
    public static void SetTune(Vector4 tune)
    {
        EnsureGlobals();
        Tune = tune;
        RenderingServer.GlobalShaderParameterSet(TuneParameter, tune);
    }

    /// <summary>The calibration in use (process wide, like the global shader parameter it feeds).</summary>
    public static Vector4 Tune { get; private set; } = DefaultTune;

    public IReadOnlyList<LightEntry> Lights => _lights;

    /// <summary>Lights the original keeps out of the baked light but applies to the world at run time.</summary>
    public static bool IsDynamicWorldLight(LightEntry l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.Dynamic && l.ApplyStatic && l.Effect != RenderK.LE_ESpotShadow && l.Brightness > 0f;
    }

    /// <summary>Switches a light (STriggerLight on and off); takes effect at the next <see cref="Update"/>.</summary>
    public void SetOn(string name, bool on)
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            if (string.Equals(_lights[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _on[i] = on;
            }
        }
    }

    /// <summary>Moves a light (one riding a mover or a bone); takes effect at the next <see cref="Update"/>.</summary>
    public void SetPlace(string name, FVector location, FRotator rotation)
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            if (string.Equals(_lights[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _lights[i] = _lights[i] with { Location = location, Rotation = rotation };
            }
        }
    }

    /// <summary>Lights spawned during play now drawn (after the map's own).</summary>
    public int SpawnedCount => _spawned.Count;

    /// <summary>
    /// The dynamic lights spawned during play this frame (SFlashLight and kin: the frag explosion's flash, muzzle
    /// flashes), at most <see cref="MaxSpawned"/>, each at the brightness it has now; they light the world as the map's
    /// dynamic lights do. Written at once.
    /// </summary>
    public void SetSpawned(IEnumerable<LightEntry> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        _spawned.Clear();
        foreach (LightEntry l in lights)
        {
            if (_spawned.Count < MaxSpawned)
            {
                _spawned.Add(l);
            }
        }

        Update(_time);
    }

    /// <summary>Writes every light at scene time <paramref name="time"/> (Level.TimeSeconds) and makes them current.</summary>
    public void Update(float time)
    {
        _time = time;
        for (int i = 0; i < _lights.Count; i++)
        {
            LightEntry l = _lights[i];
            Write(i, l, _on[i] ? Modulation(l, time) : 0f);
        }

        for (int i = 0; i < _spawned.Count; i++)
        {
            Write(_lights.Count + i, _spawned[i], 1f);
        }

        _texture.Update(_image);
        RenderingServer.GlobalShaderParameterSet(s_tuneName, Tune);
        if (!_textureSet)
        {
            // The same texture every frame (only its pixels change): set once, a texture variant is a finalizable wrapper.
            _textureSet = true;
            RenderingServer.GlobalShaderParameterSet(s_textureName, _texture);
        }

        RenderingServer.GlobalShaderParameterSet(s_countName, _lights.Count + _spawned.Count);
    }

    private void Write(int row, LightEntry l, float m)
    {
        PackedLight p = ActorLighting.PackWorld(l, m);
        _image.SetPixel(0, row, new Color(p.Pos.X, p.Pos.Y, p.Pos.Z, p.Pos.W));
        _image.SetPixel(1, row, new Color(p.Col.X, p.Col.Y, p.Col.Z, p.Col.W));
        _image.SetPixel(2, row, new Color(p.Spot.X, p.Spot.Y, p.Spot.Z, p.Spot.W));
        _image.SetPixel(3, row, new Color(l.OwnZoneOnly ? l.ZoneIndex : -1f, 0f, 0f, 0f));
    }

    /// <summary>
    /// Brightness factor of a light type at a time (FDynamicLight::Update): steady 1; pulse 0.6 + 0.39 sin;
    /// subtle pulse 0.9 + 0.09 sin; blink on for half of each period. The phase runs at 35 / LightPeriod turns a
    /// second from LightPhase / 256 of a turn. Flicker and strobe are drawn steady for now.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FDynamicLight::Update light types, editor 0x111af860 (bridge docs/LIGHTING_FEATURES.md)")]
    public static float Modulation(LightEntry l, float time)
    {
        ArgumentNullException.ThrowIfNull(l);
        float period = MathF.Max(1f, l.Period);
        float turns = (time * 35f / period) + (l.Phase / 256f);
        float sin = MathF.Sin(turns * MathF.Tau);
        return l.LightType switch
        {
            2 => 0.6f + (0.39f * sin),
            3 => (turns - MathF.Floor(turns)) < 0.5f ? 1f : 0f,
            7 => 0.9f + (0.09f * sin),
            _ => 1f,
        };
    }

    public static void EnsureGlobals()
    {
        if (s_globalsAdded)
        {
            return;
        }

        Image empty = Image.CreateEmpty(4, 1, false, Image.Format.Rgbaf);
        RenderingServer.GlobalShaderParameterAdd(TextureParameter, RenderingServer.GlobalShaderParameterType.Sampler2D,
            ImageTexture.CreateFromImage(empty));
        RenderingServer.GlobalShaderParameterAdd(CountParameter, RenderingServer.GlobalShaderParameterType.Int, 0);
        RenderingServer.GlobalShaderParameterAdd(TuneParameter, RenderingServer.GlobalShaderParameterType.Vec4, DefaultTune);
        s_globalsAdded = true;
    }
}
