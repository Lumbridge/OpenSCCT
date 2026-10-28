using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// The player profile's tutorial flags (SPlayerProfile: bTutorialAdvancement_ATT/DEF, bExamDone, the maps already
/// played) and the rules that change them (spec/game/tutorials.md). The game keeps them in a small file next to the
/// settings; this class holds the values and the rules only.
/// </summary>
public sealed class TutorialProgress
{
    /// <summary>Spy tutorials done, by index in <see cref="TutorialMaps.Spy"/> (bit i).</summary>
    public int SpyDone { get; set; }

    /// <summary>Merc tutorials done, by index in <see cref="TutorialMaps.Merc"/> (bit i).</summary>
    public int MercDone { get; set; }

    /// <summary>SPlayerProfile.bExamDone.</summary>
    public bool ExamDone { get; set; }

    /// <summary>SPlayerProfile.bExamJustCompleted: set with the first exam pass, not saved.</summary>
    [JsonIgnore]
    public bool ExamJustCompleted { get; set; }

    /// <summary>The maps the player confirmed the first-time prompt for (isMapAlreadyPlayed).</summary>
    public List<string> MapsPlayed { get; set; } = [];

    /// <summary>SPlayerProfile.GetMapTutorialDone.</summary>
    [PortOf("nat:SBase.SPlayerProfile.GetMapTutorialDone", Spec = "spec/game/tutorials.md#rules")]
    public bool GetMapTutorialDone(int NumTut, int Team) =>
        NumTut is >= 0 and < 31 && ((Team == 0 ? SpyDone : MercDone) & (1 << NumTut)) != 0;

    /// <summary>SPlayerProfile.SetMapTutorialDone.</summary>
    [PortOf("nat:SBase.SPlayerProfile.SetMapTutorialDone", Spec = "spec/game/tutorials.md#rules")]
    public void SetMapTutorialDone(int NumTut, int Team)
    {
        if (NumTut is < 0 or >= 31)
        {
            return;
        }

        if (Team == 0)
        {
            SpyDone |= 1 << NumTut;
        }
        else
        {
            MercDone |= 1 << NumTut;
        }
    }

    /// <summary>
    /// SPersistentData.MarkTutoAsDone: the map is done; the spy exam (the last spy map) also sets the exam flag the
    /// first time.
    /// </summary>
    [PortOf("uc:SBase.SPersistentData:MarkTutoAsDone", Spec = "spec/game/tutorials.md#rules")]
    public void MarkTutoAsDone(int NumTut, int NumTeam)
    {
        if (NumTut == TutorialMaps.ExamIndex && NumTeam == 0 && !ExamDone)
        {
            ExamDone = true;
            ExamJustCompleted = true;
        }

        SetMapTutorialDone(NumTut, NumTeam);
    }

    /// <summary>Every merc tutorial is done (the round end's test before the next merc tutorial).</summary>
    public bool AllMercDone()
    {
        for (int i = 0; i < TutorialMaps.Merc.Count; i++)
        {
            if (!GetMapTutorialDone(i, 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>SPlayerProfile.isMapAlreadyPlayed.</summary>
    [PortOf("nat:SBase.SPlayerProfile.isMapAlreadyPlayed", Spec = "spec/game/tutorials.md#rules")]
    public bool isMapAlreadyPlayed(string _map) => MapsPlayed.Exists(m => string.Equals(m, _map, StringComparison.OrdinalIgnoreCase));

    /// <summary>SPlayerProfile.setMapAlreadyPlayed: true when the map was not listed yet.</summary>
    [PortOf("nat:SBase.SPlayerProfile.setMapAlreadyPlayed", Spec = "spec/game/tutorials.md#rules")]
    public bool setMapAlreadyPlayed(string _map)
    {
        ArgumentNullException.ThrowIfNull(_map);
        if (_map.Length == 0 || isMapAlreadyPlayed(_map))
        {
            return false;
        }

        MapsPlayed.Add(_map);
        return true;
    }
}
