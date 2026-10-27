namespace OpenScct.Provenance;

/// <summary>Where a constant or rule came from.</summary>
public enum ProvenanceKind
{
    /// <summary>Read directly from the original binaries or packages (decompiled script, class defaults, disassembly).</summary>
    Decoded,

    /// <summary>Measured by observing the original game (traces, live memory reads, captures).</summary>
    Measured,

    /// <summary>Best guess (engine family knowledge, tuning) that still needs confirming.</summary>
    Estimated,
}
