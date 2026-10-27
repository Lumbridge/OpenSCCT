using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SMapSettings: a map's display name and per-mode defaults. Written from spec/game/match-options.md.
public partial class USMapSettings
{
    /// <summary>The index of the first Modes entry for <paramref name="GameModeId"/>, or -1.</summary>
    [Uc("SBase.SMapSettings.SearchForGameMode", Sha1 = "eda559a8af0e8f324780e4f315a4a87521ae5f5f")]
    [PortOf("uc:SBase.SMapSettings:SearchForGameMode", Spec = "spec/game/match-options.md#rules")]
    public int SearchForGameMode(EGameMode GameModeId)
    {
        for (int i = 0; i < Modes.Count; i++)
        {
            if (Modes[i].GameModeId == GameModeId)
            {
                return i;
            }
        }

        return -1;
    }
}
