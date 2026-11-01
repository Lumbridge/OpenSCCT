using System;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScctGame.UI;

/// <summary>Copyable local-player coordinates and facing for reproducing map and traversal problems.</summary>
public static class PlayerLocationReport
{
    public static string Format(string map, SBasePawn pawn, SPlayerController controller)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(controller);
        string side = pawn is SPAWNDEFENSE ? "merc" : "spy";
        return $"map={map} side={side} state={controller.GetStateName()} physics={pawn.Physics} crouched={pawn.bIsCrouched.ToString().ToLowerInvariant()} fast={controller.bFastSpeed.ToString().ToLowerInvariant()}\n" +
            $"location_uu={Vector(pawn.Location)}\n" +
            $"pawn_rotation_ue={Rotation(pawn.Rotation)} view_rotation_ue={Rotation(controller.Rotation)}\n" +
            $"view_forward={Vector(controller.Rotation.Vector())}";
    }

    // Round-trip format preserves every coordinate bit and always uses a decimal point, irrespective of locale.
    private static string Vector(FVector value) => string.Create(CultureInfo.InvariantCulture, $"({value.X:R},{value.Y:R},{value.Z:R})");

    private static string Rotation(FRotator value) =>
        string.Create(CultureInfo.InvariantCulture, $"(pitch={value.Pitch},yaw={value.Yaw},roll={value.Roll})");
}
