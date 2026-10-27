using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>Fixed simulation constants shared by every sim library.</summary>
public static class Sim
{
    /// <summary>Simulation ticks per second.</summary>
    [Provenance(ProvenanceKind.Estimated, "Project rule: fixed 60 Hz tick (the original ran a variable frame time)")]
    public const int Hz = 60;

    /// <summary>Seconds per tick before time dilation, as a float (0.016666668).</summary>
    public const float DeltaTime = 1f / Hz;
}
