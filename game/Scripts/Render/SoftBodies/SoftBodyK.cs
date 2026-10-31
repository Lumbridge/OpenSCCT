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

    /// <summary>Fixed cosmetic physics step; solver iterations must not speed up with the render frame rate.</summary>
    [Provenance(ProvenanceKind.Measured, "Aquarium original at 60 FPS: UESoftBody+0x12c settles at 0.01665 s; this is the reference cadence")]
    [Provenance(ProvenanceKind.Estimated, "Hold the reference cadence at higher FPS: distance-constraint stiffness depends on the number of solver passes per second")]
    public const float StepInterval = 1f / 60f;

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

    /// <summary>The reach of another merc's rounds (his third-person gun does not say which rifle he holds).</summary>
    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense default ShootingRange 15000")]
    public const float MercShootingRange = 15000f;

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
