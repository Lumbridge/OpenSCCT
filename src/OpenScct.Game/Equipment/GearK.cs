namespace OpenScct.Game.Equipment;

/// <summary>
/// The mid-round gear change's numbers. It is an OpenSCCT addition (spec/additions/gear-change.md): these are our own
/// choices, not values of the original, so they carry no provenance.
/// </summary>
public static class GearK
{
    /// <summary>A new kit goes on at once only this long (s) after the pawn spawned, and only while nothing was used.</summary>
    public const float SwapWindowSeconds = 10f;

    /// <summary>How long (s) after the server asks for a kit it accepts the owner's spawn requests for it.</summary>
    public const float GrantSeconds = 30f;
}
