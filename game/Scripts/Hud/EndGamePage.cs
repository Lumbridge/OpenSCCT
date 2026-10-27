using System;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// P_EndGame: the end-of-match banner (docs/HUD.md "End banner"). <c>GameEnded.BeginState</c> opens it on the player's
/// machine the moment the round ends (spec/game/end-of-round.md rule 17) and nothing closes it: the end menu opens over
/// it 10 s after the winner is known (rule 18). Its four areas start hidden; each frame the page shows You Win or You
/// Lose once the winner has replicated (Chapter Completed on a tutorial), restarting an area only when it was hidden.
/// The area's own keyframes play it once and then loop its end.
/// </summary>
public sealed class EndGamePage
{
    public const string PageName = "P_EndGame";

    public const string YouWin = "YouWin";

    public const string YouLose = "YouLose";

    /// <summary>The tutorial's area (the package's own spelling).</summary>
    public const string Finish = "YouFinsih";

    public const string FinishSpect = "YouFinishSpect";

    /// <summary>The page with every area hidden, as its init (0x10b26f40) leaves it.</summary>
    public EndGamePage(HudAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        MagmaPackage package = assets.Hud;
        if (!package.Pages.TryGetValue(PageName, out int area))
        {
            throw new InvalidOperationException($"page {PageName} is not in the Hud package");
        }

        Root = new MagmaNode(package, area);
        // A full-screen page: stretched over the whole screen as the game draws its other full-screen pages (the
        // binocular pages, settled on a live capture), so a wide screen has no strip outside the 4:3 page.
        Renderer = new MagmaRenderer { TextureSize = assets.TextureSize, StretchAll = true };
        foreach (MagmaNode child in Root.Children)
        {
            child.SetVisible(false);
        }
    }

    /// <summary>The page showing You Win or You Lose from its first frame (the results screen's copy).</summary>
    public EndGamePage(HudAssets assets, bool won)
        : this(assets)
    {
        Show(won ? YouWin : YouLose);
    }

    public MagmaNode Root { get; }

    public MagmaRenderer Renderer { get; }

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The area shown, or null while the page is empty.</summary>
    public string? Shown { get; private set; }

    /// <summary>
    /// The area the page shows for the local controller this frame (update 0x10b26ce0): Chapter Completed on a tutorial,
    /// nothing until <c>GRI.WinningTeam</c> replicates or for a player without a team, else You Win when the player's
    /// team is the winner and You Lose otherwise.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "P_EndGame update 0x10b26ce0: tutorial -> A_Finish; WinningTeam 255 or PRI.Team None -> nothing; PRI.Team.TeamIndex == WinningTeam -> A_YouWin, else A_YouLose")]
    public static string? AreaFor(PlayerController? pc, bool tutorial)
    {
        if (pc is null)
        {
            return FinishSpect;
        }

        if (tutorial)
        {
            return Finish;
        }

        if (pc.GameReplicationInfo is not { WinningTeam: not 255 } gri || pc.PlayerReplicationInfo?.Team is not { } team)
        {
            return null;
        }

        return team.TeamIndex == gri.WinningTeam ? YouWin : YouLose;
    }

    /// <summary>Shows <paramref name="areaName"/> if it is hidden (from frame 0, playing) and hides the other areas.</summary>
    public void Show(string? areaName)
    {
        if (areaName is null)
        {
            return;
        }

        foreach (MagmaNode child in Root.Children)
        {
            bool show = string.Equals(child.Name, areaName, StringComparison.OrdinalIgnoreCase);
            if (show && !child.Visible)
            {
                child.SetVisible(true);
                child.Restart();
            }
            else if (!show && child.Visible)
            {
                child.SetVisible(false);
            }
        }

        Shown = areaName;
    }

    public void Update(float dt, Vector2 screen)
    {
        Root.Tick(dt);
        DrawList.Clear();
        Renderer.DrawPage(Root, screen, DrawList);
    }
}
