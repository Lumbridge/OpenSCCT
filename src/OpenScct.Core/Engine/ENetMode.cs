using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Network mode of the running level (Engine.LevelInfo.ENetMode).</summary>
[Provenance(ProvenanceKind.Decoded, "Engine.u enum LevelInfo.ENetMode")]
public enum ENetMode : byte
{
    NM_Standalone = 0,
    NM_DedicatedServer = 1,
    NM_ListenServer = 2,
    NM_Client = 3,
}
