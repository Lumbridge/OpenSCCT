using System.Collections.Generic;
using Godot;
using OpenScct.Game.SBase;

namespace OpenScctGame.Views;

/// <summary>
/// A turn of one bone laid over a pawn's pose, about an axis in the pawn's own frame (X forward, Y right, Z up, as
/// it faces), by <see cref="Radians"/> (right-handed about the axis in Godot space).
/// </summary>
public readonly record struct PoseTurn(string Bone, Vector3 Axis, float Radians);

/// <summary>
/// Extra posing of a drawn pawn on top of its simulated animation (an OpenSCCT addition: the hub's emotes). The view
/// asks every frame; nothing is written to the simulation.
/// </summary>
public interface IPawnPoseOverlay
{
    /// <summary>
    /// Adds the turns <paramref name="pawn"/> wears now to <paramref name="turns"/> (in the order to apply, parents
    /// first), and returns the hips' drop in uu (0 for none).
    /// </summary>
    float Collect(SBasePawn pawn, List<PoseTurn> turns);
}
