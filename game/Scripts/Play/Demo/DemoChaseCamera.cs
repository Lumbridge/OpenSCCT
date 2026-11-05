using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Play.Demo;

/// <summary>A recorded player's following view, with a temporary mouse orbit that eases back when released.</summary>
public sealed class DemoChaseCamera
{
    private const float MouseSensitivity = 20f;
    private const int PitchLimit = 16000;
    // Returns more than 99% of the way behind the player in half a second of real time.
    private const float ReturnRate = 12f;
    private float _yawOffset;
    private float _pitchOffset;

    public bool Orbiting { get; set; }

    /// <summary>The recorded view plus the temporary orbit; a packed zero is a valid level, forward-facing view.</summary>
    public FRotator RotationFor(SBasePawn pawn)
    {
        FRotator recorded = ReplicatedView(pawn);
        return new FRotator(
            Math.Clamp(recorded.Pitch + (int)MathF.Round(_pitchOffset), -PitchLimit, PitchLimit),
            FRotator.NormalizeAxis(recorded.Yaw + (int)MathF.Round(_yawOffset)), 0);
    }

    /// <summary>Follows the player's eye position from the current orbit distance.</summary>
    public ViewSample ViewFor(SBasePawn pawn, long tick, float fov, float distance)
    {
        FRotator rotation = RotationFor(pawn);
        FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
        return new ViewSample(tick, eye - rotation.Vector() * distance, rotation, fov, true);
    }

    /// <summary>Orbits relative to the player's recorded view while the look button is held.</summary>
    public void Turn(SBasePawn pawn, float dx, float dy)
    {
        if (!Orbiting) return;
        float yaw = _yawOffset + dx * MouseSensitivity;
        _yawOffset = yaw - MathF.Floor((yaw + 32768f) / 65536f) * 65536f;
        int pitch = ReplicatedView(pawn).Pitch;
        _pitchOffset = Math.Clamp(pitch + _pitchOffset - dy * MouseSensitivity, -PitchLimit, PitchLimit) - pitch;
    }

    /// <summary>Returns toward the recorded view using real frame time, including when playback is paused.</summary>
    public void Advance(float seconds)
    {
        if (Orbiting || seconds <= 0f) return;
        float remaining = MathF.Exp(-ReturnRate * seconds);
        _yawOffset = Decay(_yawOffset, remaining);
        _pitchOffset = Decay(_pitchOffset, remaining);
    }

    /// <summary>Starts directly behind a newly watched player or after a camera mode change.</summary>
    public void Reset()
    {
        _yawOffset = 0f;
        _pitchOffset = 0f;
        Orbiting = false;
    }

    private static float Decay(float offset, float remaining)
    {
        float next = offset * remaining;
        return MathF.Abs(next) < 1f ? 0f : next;
    }

    internal static FRotator ReplicatedView(SBasePawn pawn) => new(
        FRotator.NormalizeAxis(pawn.iYawPitchFromView >> 16), pawn.iYawPitchFromView & 0xFFFF, 0);
}
