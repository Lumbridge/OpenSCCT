using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>
/// OpenSCCT: the mover a player's position is told and compared in (docs/NETCODE.md, "Movers on clients"). Every
/// machine runs its own copy of a mover and a client's copy is not where the server's is: a position on a moving
/// mover means the same on both only as an offset from the mover.
/// </summary>
public static class MoverFrame
{
    /// <summary>
    /// The frame of a pawn on <paramref name="base"/> with <paramref name="physics"/>, given the frame it had before
    /// (<paramref name="kept"/>): the mover it stands on, or while it falls the mover it left (a jump inside a moving
    /// car), until it lands on something else.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: the original sends world positions; its only mover rule drops Z from the error for 3 s (move-protocol.md rule 12)")]
    public static Actor? Of(Actor? @base, EPhysics physics, Actor? kept) =>
        @base is { bIsAMover: true, bDeleteMe: false } ? @base
        : physics == EPhysics.PHYS_Falling && @base is null && kept is { bDeleteMe: false } ? kept
        : null;

    /// <summary>A world position as an offset along <paramref name="frame"/>'s axes.</summary>
    public static FVector ToFrame(FVector world, Actor frame)
    {
        FVector d = world - frame.Location;
        FCoords axes = FCoords.FromRotator(frame.Rotation);
        return new FVector(d | axes.XAxis, d | axes.YAxis, d | axes.ZAxis);
    }

    /// <summary>The world position of an offset along <paramref name="frame"/>'s axes.</summary>
    public static FVector FromFrame(FVector offset, Actor frame)
    {
        FCoords axes = FCoords.FromRotator(frame.Rotation);
        return frame.Location + axes.XAxis * offset.X + axes.YAxis * offset.Y + axes.ZAxis * offset.Z;
    }
}
