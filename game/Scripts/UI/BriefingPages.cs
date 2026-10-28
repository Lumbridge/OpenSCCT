using System;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;

namespace OpenScctGame.UI;

/// <summary>
/// The two briefing pages of a map for the local player's side, as GUI.Briefing and GUI.BriefingDeux fill them from the
/// map's SMapSettings strings (converted to the map's <c>ui/mapinfo.json</c>): page 1 "MISSION MAIN OBJECTIVE" with the
/// mode's explanation (ModesStrings ExplicationModeSpy / ExplicationModeSentry, for the rules mode), the mode's
/// objective name (GUIController TextSpy / TextSentry, when the text table has them) and the map's briefing
/// (ExpliMapInBriefSpy / ExpliMapInBriefSentry); page 2 the mode's briefing for the side (ExpliModeInBriefSpy /
/// ExpliModeInBriefSentry, for the mode itself) and the map legend (LegendMapInBrief, lines split by "|").
/// </summary>
public sealed record BriefingPages(string Title, string Objective, string ObjectiveName, string MapBrief, string ModeBrief, string Legend)
{
    /// <summary>Page 1's title (GUI.Briefing Titre).</summary>
    public const string MainObjectiveTitle = "MISSION MAIN OBJECTIVE";

    /// <summary>Nothing to show: the map has no briefing words for this mode and side.</summary>
    public bool IsEmpty => Objective.Length == 0 && MapBrief.Length == 0 && ModeBrief.Length == 0 && Legend.Length == 0;

    /// <summary>The pages of <paramref name="settings"/> for <paramref name="mode"/>, for the spies or the mercs.</summary>
    [OpenScct.Provenance.PortOf("uc:GUI.Briefing:InitComponent", Spec = "spec/game/lobby-and-match-creation.md#briefing")]
    [OpenScct.Provenance.PortOf("uc:GUI.Briefing:RefreshText", Spec = "spec/game/lobby-and-match-creation.md#briefing")]
    [OpenScct.Provenance.PortOf("uc:GUI.BriefingDeux:InitComponent", Spec = "spec/game/lobby-and-match-creation.md#briefing")]
    public static BriefingPages For(USMapSettings? settings, EGameMode mode, bool spy)
    {
        if (settings is null)
        {
            return new BriefingPages(MainObjectiveTitle, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        string objective = string.Empty;
        int rules = settings.SearchForGameMode(USMatchInfo.RulesModeFor(mode));
        if (rules >= 0 && rules < settings.ModesStrings.Count)
        {
            FGameModeInfoStrings w = settings.ModesStrings[rules];
            objective = spy ? w.ExplicationModeSpy : w.ExplicationModeSentry;
        }

        // BriefingDeux looks the mode itself up (not its rules mode), so Merc Deathmatch finds no entry there.
        string modeBrief = string.Empty;
        int own = settings.SearchForGameMode(mode);
        if (own >= 0 && own < settings.ModesStrings.Count)
        {
            FGameModeInfoStrings w = settings.ModesStrings[own];
            modeBrief = spy ? w.ExpliModeInBriefSpy : w.ExpliModeInBriefSentry;
        }

        string name = UcText.Get("GUI.GUIController", spy ? "TextSpy" : "TextSentry", (int)mode, string.Empty);
        return new BriefingPages(MainObjectiveTitle, Lines(objective), name, Lines(spy ? settings.ExpliMapInBriefSpy : settings.ExpliMapInBriefSentry),
            Lines(modeBrief), Lines(settings.LegendMapInBrief));
    }

    /// <summary>The original's multi-line labels break at "|".</summary>
    private static string Lines(string? text) => (text ?? string.Empty).Replace("|", "\n", StringComparison.Ordinal).Trim();
}
