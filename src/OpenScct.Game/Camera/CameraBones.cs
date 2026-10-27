using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>
/// Where a pawn's bones are, for the views that hang on a bone (the first-person view with m_bAttachToBone, the
/// centred transition of mode 34). The simulation keeps no skeletal pose; the machine that draws can install one
/// with <see cref="Level.SetService{T}"/> from its posed meshes. Without it the views use the pawn's cylinder.
/// </summary>
public interface ICameraBones
{
    /// <summary>The world location and rotation of <paramref name="bone"/> of <paramref name="pawn"/>'s mesh; false when unknown.</summary>
    bool TryGetBone(Pawn pawn, Name bone, out FVector location, out FRotator rotation);

    /// <summary>The bone the centred camera starts from (mode 34), or false.</summary>
    bool TryGetCentre(Pawn pawn, out FVector location);
}

/// <summary>Lookups of the level's <see cref="ICameraBones"/>.</summary>
public static class CameraBones
{
    public static bool TryGet(Pawn pawn, Name bone, out FVector location, out FRotator rotation)
    {
        if (pawn.XLevel.GetService<ICameraBones>() is { } bones)
        {
            return bones.TryGetBone(pawn, bone, out location, out rotation);
        }

        location = default;
        rotation = default;
        return false;
    }

    /// <summary>The centred camera's bone, or the pawn's location when the level cannot say.</summary>
    [Provenance(ProvenanceKind.Estimated, "Which bone mode 34 starts from is not named by the spec; without a pose the pawn's centre is used")]
    public static FVector CenterOf(Pawn pawn) =>
        pawn.XLevel.GetService<ICameraBones>() is { } bones && bones.TryGetCentre(pawn, out FVector location) ? location : pawn.Location;
}
