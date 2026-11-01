using System;
using Godot;
using OpenScct.Game.SBase;

namespace OpenScctGame.Fx;

/// <summary>A persistent, depth-tested fault indicator on a disabled Camnet lens, with no light or shocker halo.</summary>
public partial class CamnetDisabledView : Node3D
{
    private readonly StandardMaterial3D _marker;
    private readonly CpuParticles3D _sparks;
    private readonly CpuParticles3D _smoke;
    private SCamNetwork? _camera;
    private float _age;
    private float _nextSpark;

    public CamnetDisabledView()
    {
        Name = "CamnetDisabled";
        _marker = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.42f, 0.055f),
        };
        // The authored Globe lens faces local +X and ends at 19.96 uu, with a radius of 11.64 uu.
        // Two narrow opaque bars stay on that lens rather than creating a billboard or wall glow.
        foreach (float angle in new[] { -MathF.PI / 4f, MathF.PI / 4f })
        {
            AddChild(new MeshInstance3D
            {
                Name = angle < 0 ? "FaultCrossA" : "FaultCrossB",
                Mesh = new BoxMesh { Size = new Vector3(0.006f, 0.016f, 0.18f) },
                Position = new Vector3(0.205f, 0f, 0f),
                Rotation = new Vector3(angle, 0f, 0f),
                MaterialOverride = _marker,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        _sparks = new CpuParticles3D
        {
            Name = "FaultSparks", Position = new Vector3(0.21f, 0f, 0f),
            Amount = 7, Lifetime = 0.38, OneShot = true, Explosiveness = 0.9f,
            Direction = Vector3.Right, Spread = 55f,
            InitialVelocityMin = 0.25f, InitialVelocityMax = 0.7f,
            Gravity = new Vector3(0f, -1.7f, 0f),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.1f,
            ColorRamp = new Gradient { Offsets = [0f, 0.7f, 1f],
                Colors = [new Color(1f, 0.58f, 0.12f), new Color(0.95f, 0.3f, 0.04f, 0.8f), new Color(0.6f, 0.12f, 0.01f, 0f)] },
            Mesh = new QuadMesh { Size = new Vector2(0.008f, 0.022f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
            },
            Emitting = false,
        };
        AddChild(_sparks);

        var smokeTexture = new GradientTexture2D
        {
            Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Offsets = [0f, 0.3f, 1f], Colors = [Colors.White, Colors.White, new Color(1f, 1f, 1f, 0f)] },
        };
        _smoke = new CpuParticles3D
        {
            Name = "FaultSmoke", Position = new Vector3(0.14f, 0.12f, 0f),
            Amount = 5, Lifetime = 1.6, Direction = Vector3.Up, Spread = 15f,
            InitialVelocityMin = 0.08f, InitialVelocityMax = 0.16f, Gravity = new Vector3(0f, 0.04f, 0f),
            ScaleAmountMin = 0.45f, ScaleAmountMax = 1.1f,
            ColorRamp = new Gradient { Offsets = [0f, 0.25f, 1f],
                Colors = [new Color(0.3f, 0.3f, 0.32f, 0f), new Color(0.3f, 0.3f, 0.32f, 0.36f), new Color(0.4f, 0.4f, 0.42f, 0f)] },
            Mesh = new QuadMesh { Size = Vector2.One * 0.12f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = smokeTexture,
            },
            Emitting = false,
        };
        AddChild(_smoke);
        Visible = false;
    }

    public void SetCamera(SCamNetwork camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Update(0f);
    }

    /// <summary>Reads the replicated disabled state; never changes gameplay or lights nearby surfaces.</summary>
    public void Update(float dt)
    {
        Visible = _camera is { IsShotDisabled: true, bHidden: false, bDeleteMe: false };
        _smoke.Emitting = Visible;
        if (!Visible)
        {
            _sparks.Emitting = false;
            return;
        }

        SCamNetwork camera = _camera!;
        Transform = UeSpace.TransformToGodot(camera.Location, camera.Rotation, camera.DrawScale3D * camera.DrawScale, camera.PrePivot);
        _age += Math.Max(0f, dt);
        float phase = _age % 1.6f;
        float strength = phase < 0.16f || phase is > 0.28f and < 0.44f ? 1f : 0.48f;
        _marker.AlbedoColor = new Color(strength, 0.42f * strength, 0.055f * strength);
        if (_age >= _nextSpark)
        {
            _sparks.Restart();
            _sparks.Emitting = true;
            _nextSpark = _age + 1.6f;
        }
    }
}
