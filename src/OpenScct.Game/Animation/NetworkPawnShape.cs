using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Animation;

/// <summary>Archetype cylinder and mesh offset for a stance; authored overrides remain explicit on the wire.</summary>
public readonly record struct NetworkPawnShape(float Radius, float Height, FVector PrePivot)
{
    public static NetworkPawnShape Defaults(SBasePawn pawn, byte zone)
    {
        SBasePawn defaults = pawn.DefaultObject;
        float height = defaults.CollisionHeight;
        FVector pivot = defaults.PrePivot;
        switch (zone)
        {
            case 1: height = defaults.CrouchHeight; pivot.Z += defaults.CollisionHeight - height; break;
            case 2: height = defaults.ZipHeight; pivot.Z += height - defaults.CollisionHeight; break;
            case 3: height = defaults.Pipe4Height; pivot.Z -= defaults.Pipe4DecalCol; break;
        }
        return new(defaults.CollisionRadius, height, pivot);
    }
}
