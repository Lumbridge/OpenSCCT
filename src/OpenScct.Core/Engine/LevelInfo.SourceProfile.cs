namespace OpenScct.Core.Engine;

public partial class LevelInfo
{
    /// <summary>
    /// Explicit source-profile metadata emitted by the PS2 converter. The PS2
    /// AActor::ShouldTrace world-geometry branch does not apply the PC pawn /
    /// bBlockPlayers gate (SC3_ON.ELF 0x179040..0x179058). False keeps PC rules.
    /// Stored in sim/gameplay.json, so it participates in the map simulation hash.
    /// </summary>
    public bool bPS2WorldGeometryTrace;
}
