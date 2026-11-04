using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Conveyors;

/// <summary>What a conveyor carries: walking pawns, or resting dropped objects.</summary>
public enum ConveyorCargo : byte
{
    Pawns,
    Objects,
}

/// <summary>A conveyor as the physics sees it (the Svm.Conveyor actor implements it).</summary>
public interface IConveyor
{
    /// <summary>The centre line on the riding surface, in world space.</summary>
    ConveyorPath WorldPath { get; }

    /// <summary>Half the width of the carried strip.</summary>
    float HalfWidth { get; }

    /// <summary>Speed along the path now, in uu a second: negative runs backwards, 0 when stopped.</summary>
    float SurfaceSpeed { get; }

    bool Carries(ConveyorCargo cargo);

    /// <summary>Server tick of the last start, stop or reversal (replicated).</summary>
    int ChangeTick { get; }
}

/// <summary>
/// The conveyors of a level (a level service, installed by the first conveyor). The walking physics asks it which
/// conveyor a pawn stands on; a level without conveyors has no field, so stock maps never take that branch.
/// Conveyors are kept in the order they began play, which is map order on every machine.
/// </summary>
public sealed class ConveyorField
{
    private readonly List<IConveyor> _conveyors = new();

    public IReadOnlyList<IConveyor> Conveyors => _conveyors;

    /// <summary>The field of <paramref name="level"/>, created and installed on first use.</summary>
    public static ConveyorField Install(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.GetService<ConveyorField>() is { } field)
        {
            return field;
        }

        field = new ConveyorField();
        level.SetService(field);
        return field;
    }

    public void Add(IConveyor conveyor)
    {
        ArgumentNullException.ThrowIfNull(conveyor);
        if (!_conveyors.Contains(conveyor))
        {
            _conveyors.Add(conveyor);
        }
    }

    public void Remove(IConveyor conveyor) => _conveyors.Remove(conveyor);

    /// <summary>
    /// Grows with every start, stop or reversal of a conveyor within <see cref="ConveyorK.RevisionReach"/> of
    /// <paramref name="location"/> (the sum of those conveyors' change ticks, which only grow). A movement correction
    /// carries the server's value at the location it puts the pawn; the client works its own out at the same place, so
    /// a lower value there means the client has not received a change that moved the pawn, while changes elsewhere on
    /// the map do not count.
    /// </summary>
    public uint RevisionNear(FVector location)
    {
        const float reach = ConveyorK.RevisionReach;
        uint sum = 0;
        for (int i = 0; i < _conveyors.Count; i++)
        {
            IConveyor c = _conveyors[i];
            ConveyorPath path = c.WorldPath;
            if (path.Length <= 0f)
            {
                continue;
            }

            path.Evaluate(0f, out FVector start, out _);
            path.Evaluate(path.Length, out FVector end, out _);
            if (path.Project(location, c.HalfWidth + reach, reach, reach, 0f, out _) ||
                (location - start).SizeSquared() <= reach * reach || (location - end).SizeSquared() <= reach * reach)
            {
                sum += (uint)c.ChangeTick;
            }
        }

        return sum;
    }

    /// <summary>
    /// The running conveyor that carries a body of <paramref name="radius"/> whose lowest point is
    /// <paramref name="feet"/>, and the surface velocity it moves the body at; null when none does
    /// (spec/additions/conveyors.md#who-rides). The body must be over the strip (its centre at most half its radius
    /// past an edge) and its feet in the height band above the surface. Overlapping conveyors: the one whose centre
    /// line is nearest wins, the earlier one on a tie.
    /// </summary>
    public IConveyor? FindCarrier(FVector feet, float radius, ConveyorCargo cargo, out FVector velocity)
    {
        velocity = default;
        IConveyor? best = null;
        float bestDistance = 0f;
        for (int i = 0; i < _conveyors.Count; i++)
        {
            IConveyor c = _conveyors[i];
            float speed = c.SurfaceSpeed;
            // A body on a slope rests on its cylinder's edge, so its feet stand higher by radius x slope.
            if (speed == 0f || !c.Carries(cargo) || !c.WorldPath.Project(feet, c.HalfWidth + radius * ConveyorK.EdgeOverhang,
                    ConveyorK.RideBelow, ConveyorK.RideAbove, radius, out ConveyorProjection at))
            {
                continue;
            }

            if (best is null || at.Distance < bestDistance)
            {
                best = c;
                bestDistance = at.Distance;
                velocity = at.Tangent * speed;
            }
        }

        return best;
    }
}

/// <summary>
/// The conveyor's own numbers (an OpenSCCT addition, spec/additions/conveyors.md). The carry step and the overhang
/// follow RE+'s ConveyorBelt so maps feel the same; the height band is tighter than RE+'s box test.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: design values, spec/additions/conveyors.md#constants")]
public static class ConveyorK
{
    /// <summary>How far a carried body lifts before it moves along, so joints and small steps do not stop it.</summary>
    public const float CarryStep = 18f;

    /// <summary>
    /// How near a conveyor (past its edges, ends, surface) a corrected pawn must be for that conveyor's last change to
    /// count in <see cref="ConveyorField.RevisionNear"/>: further than a fast belt carries a pawn in a round trip.
    /// </summary>
    public const float RevisionReach = 512f;

    /// <summary>A body still rides with its centre this fraction of its radius past the strip's edge.</summary>
    public const float EdgeOverhang = 0.5f;

    /// <summary>Feet may be this far below the riding surface (a driver mesh a little below its path).</summary>
    public const float RideBelow = 8f;

    /// <summary>Feet may be this far above the surface on the flat (more on a slope); higher bodies stand on something else.</summary>
    public const float RideAbove = 12f;

    /// <summary>A resting object starts to fall when no floor is this close below it after a carry.</summary>
    public const float ObjectFloorProbe = 8f;
}
