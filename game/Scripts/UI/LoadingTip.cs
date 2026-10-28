using System;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;

namespace OpenScctGame.UI;

/// <summary>
/// A tip on the loading pages (enh:ui.tips): one of the side's tips, the team class's aTipStrings, which Enhanced 4.0
/// rewrote with its own advice (silent movement, proxy mine speed, EMF ghosting; SBase.int, converted into
/// shared/classes.json). The original shows them only on the death page (spec/hud/spectator-page.md); the rewrite also
/// shows one while a map loads. Key tokens (\USE, \GADGET1) name the player's keys for that side.
/// </summary>
public static class LoadingTip
{
    /// <summary>A random tip of the side, or an empty string without the converted text.</summary>
    public static string Pick(bool merc)
    {
        STeamInfo team = merc ? new STeamDefenseur() : new STeamAttaquant();
        if (team.aTipStrings.Count == 0)
        {
            return string.Empty;
        }

        string tip = team.aTipStrings[Random.Shared.Next(team.aTipStrings.Count)];
        return OpenScctGame.Tutorials.TutorialText.WithKeys(tip, merc ? PlaySide.Merc : PlaySide.Spy);
    }

    /// <summary>The tip's label, under the loading page's words.</summary>
    public static Label Label(string tip) => new()
    {
        Name = "Tip", Text = tip.Length > 0 ? "Tip: " + tip : string.Empty, Modulate = UiStyle.MenuFocus,
        AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(740f, 0f), HorizontalAlignment = HorizontalAlignment.Center,
    };
}
