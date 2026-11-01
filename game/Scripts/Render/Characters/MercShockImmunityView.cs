using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Game.Combat;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// OpenSCCT's merc shock-protection cue. Separate skinned body draws preserve cosmetic, wet and sticky materials;
/// the depth-tested amber edge does not illuminate the world, cast shadows, or touch the first-person arms.
/// </summary>
public sealed class MercShockImmunityView
{
    public const string ShaderPath = "res://Shaders/svm_merc_shock_immunity.gdshader";
    public const float PulseSeconds = 1.5f;
    public const float MinimumCoverage = 0.035f;

    private readonly SkeletalModel _model;
    private readonly ShaderMaterial _material;
    private readonly Dictionary<MeshInstance3D, MeshInstance3D> _shells = [];
    private float _phase;
    private bool _firstPerson;

    public MercShockImmunityView(SkeletalModel model)
    {
        _model = model;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
    }

    public bool Shown { get; private set; }
    public float Coverage { get; private set; }
    public float FeetY { get; private set; }
    public float HeadY { get; private set; }
    public IReadOnlyCollection<MeshInstance3D> Shells => _shells.Values;

    public void SetFirstPerson(bool on)
    {
        _firstPerson = on;
        if (!on) return;
        Shown = false;
        foreach (MeshInstance3D shell in _shells.Values) shell.Visible = false;
    }

    /// <summary>Read after posing, so standing and crouching use their current head-to-foot height.</summary>
    public void Draw(ShockImmunityView state, float dt, bool visible, float visionOpacity = 1f)
    {
        _phase = (_phase + Math.Max(0f, dt)) % PulseSeconds;
        Shown = state.Active && visible && !_firstPerson && visionOpacity > 0f;
        Coverage = !Shown ? 0f : state.StateProtected ? 1f : Math.Clamp(state.Fraction, MinimumCoverage, 1f);
        if (Shown)
        {
            UpdateBounds();
            _material.SetShaderParameter("coverage", Coverage);
            _material.SetShaderParameter("feet_y", FeetY);
            _material.SetShaderParameter("head_y", HeadY);
            _material.SetShaderParameter("pulse", 0.72f + (0.28f * MathF.Cos(_phase * MathF.Tau / PulseSeconds)));
            _material.SetShaderParameter("vision_opacity", Math.Clamp(visionOpacity, 0f, 1f));
            // Cosmetics may split a material into another mesh after Bind. Share its mesh, skin and skeleton,
            // keeping the second draw alongside the source so its relative skeleton path stays valid.
            foreach (MeshInstance3D source in _model.Meshes)
            {
                if (!_shells.TryGetValue(source, out MeshInstance3D? shell))
                {
                    shell = new MeshInstance3D
                    {
                        Name = "ShockProtection",
                        MaterialOverride = _material,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                        Layers = RenderLayers.Effects,
                    };
                    source.GetParent().AddChild(shell);
                    _shells.Add(source, shell);
                }

                if (shell.Mesh != source.Mesh) shell.Mesh = source.Mesh;
                if (shell.Skin != source.Skin) shell.Skin = source.Skin;
                if (shell.Skeleton != source.Skeleton) shell.Skeleton = source.Skeleton;
                if (shell.Transform != source.Transform) shell.Transform = source.Transform;
                if (shell.CustomAabb != source.CustomAabb) shell.CustomAabb = source.CustomAabb;
                if (shell.ExtraCullMargin != source.ExtraCullMargin) shell.ExtraCullMargin = source.ExtraCullMargin;
                // Custom map/flare shadow cameras use layer masks, independently of Godot's CastShadow flag.
                shell.Layers = RenderLayers.Effects;
                shell.Visible = source.Visible;
            }
        }
        else
        {
            foreach (MeshInstance3D shell in _shells.Values) shell.Visible = false;
        }
    }

    private void UpdateBounds()
    {
        // Bone positions are read from the submitted pose, rather than the previous deferred skeleton update.
        float head = Height("B Head");
        float feet = Math.Min(Math.Min(Height("B L Foot"), Height("B R Foot")),
            Math.Min(Height("B L Toe0"), Height("B R Toe0")));
        FeetY = feet - 0.035f;
        HeadY = Math.Max(FeetY + 0.1f, head + 0.14f);
    }

    private float Height(string name)
    {
        int bone = _model.Skeleton.FindBone(name);
        return bone < 0 ? _model.Root.GlobalPosition.Y :
            (_model.Skeleton.GlobalTransform * _model.CurrentBonePose(bone)).Origin.Y;
    }
}
