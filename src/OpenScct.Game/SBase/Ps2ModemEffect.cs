using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Game.SBase;

/// <summary>The original PS2 modem destruction preset (class686176, COM0xC464D..C4EB4).</summary>
public sealed class Ps2ModemEffect : Emitter
{
    public AssetRef SmokeTexture;
    public AssetRef FireTexture;
    private USpriteEmitter[]? _sourceEmitters;

    /// <summary>Exact source instance tags over the native SpriteEmitter defaults, in source order.</summary>
    public IReadOnlyList<USpriteEmitter> SourceEmitters
    {
        get
        {
            _sourceEmitters ??= [SmokeLong(), SmokeShort(), Flash()];
            _sourceEmitters[0].Texture = _sourceEmitters[1].Texture = SmokeTexture;
            _sourceEmitters[2].Texture = FireTexture;
            return _sourceEmitters;
        }
    }

    protected override void SetDefaults()
    {
        base.SetDefaults();
        bNoDelete = false;
        LifeSpan = 23f;
        Mass = 16f;
        RemoteRole = ENetRole.ROLE_SimulatedProxy;
    }

    private static FRange Range(float min, float max) => new() { Min = min, Max = max };
    private static FRangeVector Ranges(float xmin, float xmax, float ymin, float ymax, float zmin, float zmax) =>
        new() { X = Range(xmin, xmax), Y = Range(ymin, ymax), Z = Range(zmin, zmax) };

    private static USpriteEmitter SmokeLong() => new()
    {
        Acceleration = new FVector(150f, 0f, 30f), UseRotationFrom = EParticleRotationSource.PTRS_Actor,
        DrawStyle = EParticleDrawStyle.PTDS_AlphaBlend,
        ColorScale = [new() { RelativeTime = 0f, Color = new FColor { R = 93, G = 93, B = 93, A = 255 } }, new() { RelativeTime = 2f, Color = new FColor { R = 255, G = 255, B = 255, A = 0 } }],
        MaxParticles = 8, UseColorScale = true, FadeOut = true, ResetAfterChange = true,
        RespawnDeadParticles = false, SpinParticles = true, UseSizeScale = true, UseRegularSizeScale = false,
        UniformSize = true, AutomaticInitialSpawning = false, BlendBetweenSubdivisions = true, UseRandomSubdivision = true,
        SpinsPerSecondRange = Ranges(-.1f, .1f, 0f, 0f, 0f, 0f),
        SizeScale = [new() { RelativeTime = 0f, RelativeSize = 1.5f }, new() { RelativeTime = 1f, RelativeSize = 7f }],
        StartSizeRange = Ranges(3f, 7f, 100f, 100f, 100f, 100f), InitialParticlesPerSecond = 1000f,
        TextureUSubdivisions = 4, TextureVSubdivisions = 4, LifetimeRange = Range(6f, 8f),
        StartVelocityRange = Ranges(100f, 300f, -70f, 70f, -70f, 70f),
        VelocityLossRange = Ranges(10f, 10f, 5f, 5f, 5f, 5f),
    };

    private static USpriteEmitter SmokeShort() => new()
    {
        Acceleration = new FVector(0f, 0f, 30f), DrawStyle = EParticleDrawStyle.PTDS_AlphaBlend,
        ColorScale = [new() { RelativeTime = 0f, Color = new FColor { R = 0, G = 0, B = 0, A = 255 } }, new() { RelativeTime = 1f, Color = new FColor { R = 255, G = 255, B = 255, A = 0 } }],
        FadeOutStartTime = .1f, UseColorScale = true, FadeOut = true, RespawnDeadParticles = false,
        SpinParticles = true, UseSizeScale = true, UseRegularSizeScale = false, UniformSize = true,
        AutomaticInitialSpawning = false, UseRandomSubdivision = true,
        SpinsPerSecondRange = Ranges(-.1f, .1f, 0f, 0f, 0f, 0f),
        SizeScale = [new() { RelativeTime = 0f, RelativeSize = 1.5f }, new() { RelativeTime = 1f, RelativeSize = 10f }],
        StartSizeRange = Ranges(1.5f, 3f, 100f, 100f, 100f, 100f), InitialParticlesPerSecond = 1000f,
        TextureUSubdivisions = 4, TextureVSubdivisions = 4, LifetimeRange = Range(1f, 1f),
        StartVelocityRange = Ranges(30f, 100f, -70f, 70f, -70f, 70f),
        VelocityLossRange = Ranges(5f, 5f, 5f, 5f, 1f, 1f),
    };

    private static USpriteEmitter Flash() => new()
    {
        Acceleration = new FVector(50f, 0f, 30f), UseRotationFrom = EParticleRotationSource.PTRS_Actor,
        DrawStyle = EParticleDrawStyle.PTDS_Brighten, MaxParticles = 2,
        RespawnDeadParticles = false, UniformSize = true, AutomaticInitialSpawning = false,
        BlendBetweenSubdivisions = true, StartSizeRange = Ranges(15f, 30f, 100f, 100f, 100f, 100f),
        InitialParticlesPerSecond = 10000f, TextureUSubdivisions = 4, TextureVSubdivisions = 4,
        LifetimeRange = Range(.5f, .7f), StartVelocityRange = Ranges(15f, 70f, 0f, 0f, 0f, 0f),
    };
}
