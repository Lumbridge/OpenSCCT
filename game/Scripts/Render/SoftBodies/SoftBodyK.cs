using OpenScct.Provenance;

namespace OpenScctGame.Render.SoftBodies;

/// <summary>Soft-body constants read from the game's UESoftBody code (docs/RENDERING.md "Soft bodies").</summary>
internal static class SoftBodyK
{
    /// <summary>UESoftBody::Update clamps its step to this range.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a6f0: dt clamped to 0.005..0.05")]
    public const float MinStep = 0.005f;

    [Provenance(ProvenanceKind.Decoded, "0x1099a6f0: dt clamped to 0.005..0.05")]
    public const float MaxStep = 0.05f;

    /// <summary>The client steps a soft body at most this often (the step is banked between frames).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded editor SoftBodyStepModel: the game client steps soft bodies at most 120 times a second")]
    public const float StepInterval = 1f / 120f;

    /// <summary>Weight of the new step in the smoothed step (smoothed = new * w + smoothed * (1 - w)).</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a6f0: weight 0.05 at 0x10c4da08")]
    public const float StepSmoothing = 0.05f;

    /// <summary>Particle positions are kept within this many uu of the actor on each local axis.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099e130 called with 1000.0 after integration, collision and in the bounds update")]
    public const float PositionClamp = 1000f;

    /// <summary>Wind noise coordinates wrap at this value.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099aa00: fmod by 256.0 on the noise coordinates and the wind timers")]
    public const float WindWrap = 256f;

    /// <summary>Noise coordinate offsets of the wind's Y and Z components (X uses the plain coordinates).</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099aa00: +12.34 / +56.78 for Y, a further +90.12 / +34.56 for Z")]
    public const float WindYOffsetU = 12.34f;

    [Provenance(ProvenanceKind.Decoded, "0x1099aa00")]
    public const float WindYOffsetV = 56.78f;

    [Provenance(ProvenanceKind.Decoded, "0x1099aa00")]
    public const float WindZOffsetU = 90.12f;

    [Provenance(ProvenanceKind.Decoded, "0x1099aa00")]
    public const float WindZOffsetV = 34.56f;

    /// <summary>A pawn is tested where it will be this many seconds ahead (Location + Velocity * t).</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a850: actor location plus velocity * 0.08")]
    public const float ColliderLookAhead = 0.08f;

    /// <summary>A link of zero length pushes its free particle this far along X.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099bd80: +0.1 on X")]
    public const float ZeroLinkNudge = 0.1f;

    /// <summary>A bullet's push is held to BulletHitFactor times this (UESBPatch, also used by strip doors).</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10b00c50: BulletHitFactor * 100 as the push's length cap")]
    public const float BulletHitScale = 100f;

    /// <summary>Bodies not drawn for this long are not stepped.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a6b0: steps only while the last render time + 5.0 >= Level.TimeSeconds")]
    public const float DrawnRecently = 5f;

    /// <summary>collSound plays at most once per this many seconds (ESoftBodyActor.CollidedBy).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.ESoftBodyActor.CollidedBy: Level.TimeSeconds > fLastSoundTime + 0.5")]
    public const float CollSoundInterval = 0.5f;

    /// <summary>PlaySound(collSound / shotSound, SLOT_None, 1.0, , 128.0).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.ESoftBodyActor: PlaySound(..., 1.0, , 128.0)")]
    public const float SoundRadius = 128f;
}
