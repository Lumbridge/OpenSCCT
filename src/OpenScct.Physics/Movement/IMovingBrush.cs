using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Movement;

/// <summary>
/// The Mover fields PHYS_MovingBrush reads and writes. A mover port implements this (explicitly, since the Mover
/// class has fields of the same names); the driver's physMovingBrush moves it along the current leg with
/// encroachment checks. The actor's own bInterpolating field says whether a leg is in progress.
/// </summary>
public interface IMovingBrush
{
    float PhysAlpha { get; set; }

    float PhysRate { get; }

    /// <summary>Target key of the current leg.</summary>
    int KeyNum { get; }

    FVector OldPos { get; }

    FRotator OldRot { get; }

    FVector BasePos { get; }

    FRotator BaseRot { get; }

    /// <summary>Mover.MoverGlideType: MV_GlideByTime eases the legs, MV_MoveByTime moves linearly.</summary>
    EMoverGlideType MoverGlideType { get; }

    FVector GetKeyPos(int Index);

    FRotator GetKeyRot(int Index);

    /// <summary>Called when a leg reaches its key (the mover's KeyFrameReached event).</summary>
    void eventKeyFrameReached();
}
