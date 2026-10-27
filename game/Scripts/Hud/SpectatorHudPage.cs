using System;
using System.Globalization;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// P_SpectatorMode, the page of a dead player or a spectator (spec/hud/spectator-page.md): objectives, remaining time,
/// lives, the watched player, the respawn countdown, a tip and the connected players. It replaces the spy or merc
/// page while the controller is spectating.
/// </summary>
public sealed class SpectatorHudPage : HudPage
{
    public const string PageName = "P_SpectatorMode";

    private bool _watchingShown;
    private bool _switchShown;
    private string _lives = "";
    private string _watched = "";

    public SpectatorHudPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options)
    {
        if (Panel is { } panel)
        {
            // The init: the respawn texts, Watching and Switch player start hidden; the button art is never shown.
            foreach (string w in new[] { "textNoRespawnLeft", "textRespawnInTime", "textRespawnTime", "textWatching", "Element11",
                         "Element10", "Element", "bouton01", "bouton02", "textSpyLivesSD", "textMercLivesSD" })
            {
                panel.Widget(w).Hidden = true;
            }
        }
    }

    /// <summary>
    /// Key tokens of the page's texts and the commands they stand for: "Hide with \ACTION2" hides the HUD
    /// (GadTorcheSwitch while spectating), "Switch player, press \PRIMARYFIRE" fires (Fire). The stock layouts bind
    /// them to E and the left mouse button.
    /// </summary>
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Estimated, "Magma.int [A_Spectator] tokens; the token-to-command table is inferred from the spectating rules (spec/game/spectating.md rules 6, 10, 13)")]
    public static readonly (string Token, string Command, string StockKey)[] KeyTokens =
    [
        (@"\ACTION2", "GadTorcheSwitch", "E"),
        (@"\PRIMARYFIRE", "Fire", "LeftMouse"),
    ];

    /// <summary>A page text with its key tokens replaced by key names.</summary>
    public static string ReplaceKeys(string text, Func<string, string?>? keyName)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach ((string token, string command, string stock) in KeyTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                text = text.Replace(token, keyName?.Invoke(command) ?? stock, StringComparison.Ordinal);
            }
        }

        return text;
    }

    /// <summary>The side the page's connected players and colours follow (the spectator page has its own look).</summary>
    public override HudSide Side => HudSide.Spy;

    /// <summary>The A_Spectator instance.</summary>
    public MagmaNode? Panel => Area("Spectator");

    protected override void Drive(HudSnapshot s, float dt)
    {
        HudSpectator v = s.Spectator;
        MagmaNode? panel = Panel;
        if (panel is null)
        {
            return;
        }

        SetShown(panel, v.Active && !v.GameEnded, restart: false);
        HideAll("GameInvite");
        DrivePlayers(s);
        if (!v.Active || v.GameEnded)
        {
            return;
        }

        panel.Widget("bouton01").Hidden = true;
        panel.Widget("bouton02").Hidden = true;
        foreach (string w in new[] { "textHide", "Element11" })
        {
            int i = panel.Area.WidgetIndex(w);
            if (i >= 0)
            {
                SetText(panel, w, ReplaceKeys(panel.Area.Widgets[i].Text, Options.KeyName));
            }
        }

        SetText(panel, "textNbObjectives", v.Objectives);
        SetText(panel, "textNbRemainingTime", v.RemainingTime);

        // Texts keep their last value when nothing replaces them (a visitor without a target).
        if (v.Lives.Length > 0)
        {
            _lives = v.Lives;
        }

        if (v.Watched.Length > 0 || (!v.Visitor && !v.ShowWatching))
        {
            _watched = v.Watched;
        }

        SetText(panel, "textNbDeaths", _lives);
        SetText(panel, "textPlayerWatched", _watched);
        panel.Widget("textTeamLives").Hidden = !v.ShowTeamLives;
        panel.Widget("textNbTeamLives").Hidden = !v.ShowTeamLives;
        if (v.ShowTeamLives)
        {
            SetText(panel, "textNbTeamLives", v.TeamLives);
        }

        // Watching and Switch player are never hidden again once shown.
        _watchingShown |= v.ShowWatching;
        _switchShown |= v.ShowSwitchPlayer;
        panel.Widget("textWatching").Hidden = !_watchingShown;
        panel.Widget("Element11").Hidden = !_switchShown;

        SetText(panel, "textTip", v.Tip);
        bool respawn = v.RespawnIn > 0;
        panel.Widget("textRespawnInTime").Hidden = !respawn;
        panel.Widget("textRespawnTime").Hidden = !respawn;
        SetText(panel, "textRespawnTime", respawn ? v.RespawnIn.ToString(CultureInfo.InvariantCulture) : "");
        panel.Widget("textNoRespawnLeft").Hidden = !v.NoRespawn;
    }

    /// <summary>Forgets what the page showed (a new spectating spell).</summary>
    public void Reset()
    {
        _watchingShown = _switchShown = false;
        _lives = _watched = "";
    }
}
