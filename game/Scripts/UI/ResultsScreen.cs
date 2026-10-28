using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.UI;

/// <summary>
/// The results of a match: first the game's own full-screen "You Win" / "You Lose" banner (P_EndGame) when the UI
/// content is there, for <see cref="BannerSeconds"/> or until a key or click; then the winner and the reason, the
/// players' scores, and Rematch / Main menu.
/// </summary>
public partial class ResultsScreen : Control
{
    /// <summary>How long the end banner plays before the table: its opening and the "You Win" text (f0 to f33).</summary>
    public const float BannerSeconds = 3.5f;

    private EndGamePage? _banner;
    private MagmaCanvas? _canvas;
    private VBoxContainer? _column;
    private float _bannerTime;

    public MatchResult? Result { get; set; }

    /// <summary>
    /// The online visit the results belong to, or null offline. Online, Rematch says the player is ready for the next
    /// round (the next game of the series, or the same match again), the host (or an admin) may send everyone back to
    /// the lobby, and Main menu leaves the server.
    /// </summary>
    public OpenScct.Server.Online.OnlineClient? Online { get; set; }

    /// <summary>Online: the host's Back to lobby.</summary>
    public Button? LobbyButton { get; private set; }

    /// <summary>Online: who the next round waits for.</summary>
    public Label? Waiting { get; private set; }

    public GridContainer? Table { get; private set; }

    public Label? Headline { get; private set; }

    public Button? RematchButton { get; private set; }

    public Button? MenuButton { get; private set; }

    /// <summary>The end banner page while it plays, else null.</summary>
    public EndGamePage? Banner => _banner;

    public bool BannerShowing => _banner is not null;

    public override void _Ready()
    {
        Theme = UiStyle.MenuTheme;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UiStyle.MenuBackdrop());
        MatchResult result = Result ?? new MatchResult { Reason = MatchEndReason.Aborted };

        // Online the banner already played over the world for the 10 s before the end menu (the HUD's P_EndGame,
        // spec/game/end-of-round.md rules 17 and 18), so the end menu opens on the table.
        if (UiStyle.Assets is { } assets && result.Winner is not null && Online is null)
        {
            _banner = new EndGamePage(assets, result.LocalWon);
            _canvas = new MagmaCanvas { Name = "Banner", Assets = assets };
            AddChild(_canvas);
        }

        VBoxContainer column = UiStyle.CentreColumn(this, 820f);
        _column = column;
        string headline = result.Winner switch
        {
            HudSide.Spy => "SPIES WIN",
            HudSide.Merc => "MERCENARIES WIN",
            _ => "NO WINNER",
        };

        // Merc Deathmatch has one side: Reloaded names the player with the most kills instead.
        string? deathmatch = result.DeathmatchLine();
        Headline = deathmatch is not null
            ? UiStyle.Title(deathmatch, UiStyle.SideColour(HudSide.Merc))
            : UiStyle.Title(headline, result.Winner is { } w ? UiStyle.SideColour(w) : UiStyle.Text);
        Headline.Name = "Headline";
        column.AddChild(Headline);
        string detail = MatchResult.ReasonText(result.Reason);
        if (result.ObjectivesTotal > 0)
        {
            detail += string.Create(CultureInfo.InvariantCulture, $" - objectives {result.ObjectivesTaken} / {result.ObjectivesTotal}");
        }

        column.AddChild(new Label { Name = "Reason", Text = detail, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.Dim });

        Table = new GridContainer { Name = "Table", Columns = 6 };
        Table.AddThemeConstantOverride("h_separation", 28);
        foreach (string head in new[] { "Player", "Side", "Score", "Kills", "Deaths", "Objectives" })
        {
            Table.AddChild(new Label { Text = head, Modulate = UiStyle.Dim });
        }

        foreach (MatchResultRow row in result.Rows)
        {
            Color c = row.IsLocal ? UiStyle.SideColour(row.Side) : UiStyle.Text;
            AddNameCell(row.Name, row.Rank, c);
            AddCell(row.Side == HudSide.Spy ? "Spy" : "Merc", c);
            AddCell(row.Score.ToString(CultureInfo.InvariantCulture), c);
            AddCell(row.Kills.ToString(CultureInfo.InvariantCulture), c);
            AddCell(row.Deaths.ToString(CultureInfo.InvariantCulture), c);
            AddCell(row.Objectives.ToString(CultureInfo.InvariantCulture), c);
        }

        column.AddChild(Table);
        // Online, the original's end menu: Next (ready for the next game of the series, or the lobby) and Leave.
        RematchButton = UiStyle.MenuButton(Online is null ? "Rematch" : "Next", Rematch, "Rematch");
        RematchButton.Disabled = result.Request is null && Online is null;
        MenuButton = UiStyle.MenuButton(Online is null ? "Main menu" : "Leave", ToMainMenu, "MainMenu");
        column.AddChild(RematchButton);
        if (Online is { } online)
        {
            Waiting = new Label { Name = "Waiting", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.Dim };
            column.AddChild(Waiting);
            LobbyButton = UiStyle.MenuButton("Back to lobby", online.BackToLobby, "BackToLobby");
            LobbyButton.Visible = online.Lobby.CanControl;
            column.AddChild(LobbyButton);
            online.Changed += RefreshWaiting;
            RefreshWaiting();
        }

        column.AddChild(MenuButton);
        if (Online is not null && AccountService.HasMaster && AccountSettings.Current.SignedIn && AccountService.Get(GetTree()).Account is { } known)
        {
            // OpenSCCT's own: the round reaches the master a moment after it ends; ask twice for what it earned.
            _earnedBefore = new HashSet<string>(known.Achievements.Where(a => a.Earned is not null).Select(a => a.Id), StringComparer.Ordinal);
            _xpBefore = known.Progress?.TotalXp;
            _awardBefore = known.Progress?.LastAward?.When;
            _askAt = [5f, 15f];
            Unlocks = new Label
            {
                Name = "Unlocks", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(780f, 0f), Visible = false,
            };
            column.AddChild(Unlocks);
        }

        if (_banner is not null)
        {
            column.Visible = false;
        }
        else
        {
            MenuButton.GrabFocus();
        }
    }

    /// <summary>Online and signed in: the achievements this round earned, once the master has them.</summary>
    public Label? Unlocks { get; private set; }

    private HashSet<string>? _earnedBefore;

    /// <summary>The account's XP and last award time before the round (null: the master had no levels then).</summary>
    private long? _xpBefore;
    private DateTimeOffset? _awardBefore;

    /// <summary>Online and signed in: the round's XP, level and new titles, once the master has them.</summary>
    public VBoxContainer? XpPanel { get; private set; }
    private float[] _askAt = [];
    private float _time;
    private Task<AccountInfo>? _ask;

    private void AskForAchievements(float delta)
    {
        if (_earnedBefore is null)
        {
            return;
        }

        _time += delta;
        if (_ask is null && _askAt.Length > 0 && _time >= _askAt[0])
        {
            _askAt = _askAt[1..];
            _ask = AccountService.Call(c => c.AccountAsync());
        }

        if (_ask is not { IsCompleted: true } ask)
        {
            return;
        }

        _ask = null;
        if (!ask.IsCompletedSuccessfully)
        {
            return;
        }

        AccountService.Get(GetTree()).Update(ask.Result);
        List<AchievementInfo> fresh = ask.Result.Achievements.Where(a => a.Earned is not null && !_earnedBefore.Contains(a.Id)).ToList();
        XpAward? award = ask.Result.Progress is { LastAward: { } last } p && (p.TotalXp != _xpBefore || last.When != _awardBefore) ? last : null;
        if (award is not null && ask.Result.Progress is { } progress)
        {
            ShowXp(award, progress);
        }

        if (fresh.Count == 0 || Unlocks is null)
        {
            if (award is not null)
            {
                _askAt = [];
            }

            return;
        }

        _askAt = [];
        Unlocks.Text = string.Join("\n", fresh.Select(a =>
        {
            string rewards = CosmeticCatalog.DescribeAll(a.Rewards);
            return "Achievement unlocked: " + a.Title + (rewards.Length > 0 ? " - " + rewards : string.Empty);
        }));
        Unlocks.Visible = true;
    }

    /// <summary>"+640 XP", the parts, the rank badge and bar, "Level up!", new titles and "Ready to prestige".</summary>
    private void ShowXp(XpAward award, ProgressInfo progress)
    {
        if (XpPanel is not null || _column is null)
        {
            return;
        }

        PlayerRank rank = RankBadge.OfAccount(AccountService.Get(GetTree()).Account);
        XpPanel = new VBoxContainer { Name = "Xp", Alignment = BoxContainer.AlignmentMode.Center };
        var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", 16);
        head.AddChild(new RankBadge(40, 18).Show(rank, withTitle: true));
        var total = new Label { Text = string.Create(CultureInfo.InvariantCulture, $"+{award.Total:N0} XP"), Modulate = UiStyle.MenuTitle };
        total.AddThemeFontSizeOverride("font_size", 26);
        head.AddChild(total);
        XpPanel.AddChild(head);

        var parts = new List<string>();
        void Part(string what, int xp)
        {
            if (xp > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{what} {xp:N0}"));
            }
        }

        Part("score", award.Score);
        Part("finish", award.Finish);
        Part("win", award.Win);
        Part("objectives", award.Objectives);
        Part("achievements", award.Achievements);
        XpPanel.AddChild(new Label { Text = string.Join("    ", parts), Modulate = UiStyle.Dim, HorizontalAlignment = HorizontalAlignment.Center });
        XpPanel.AddChild(new UiProgress
        {
            Value = rank.Fraction, Fill = RankIcons.ColourOf(rank), CustomMinimumSize = new Vector2(520f, 6f),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        });

        var notes = new List<string>();
        if (award.LevelAfter > award.LevelBefore)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Level up! Level {award.LevelAfter}"));
        }

        notes.AddRange(award.NewTitles.Select(id => TitleCatalog.Find(id) is { } t ? "Title unlocked: " + t.Text : null).OfType<string>());
        if (rank.CanPrestige)
        {
            notes.Add($"Level {ProgressionK.MaxLevel}: you can enter prestige {rank.Prestige + 1} on the Account page.");
        }

        if (notes.Count > 0)
        {
            XpPanel.AddChild(new Label
            {
                Text = string.Join("\n", notes), Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        _column.AddChild(XpPanel);
        if (Unlocks is not null)
        {
            _column.MoveChild(XpPanel, Unlocks.GetIndex());
        }
    }

    public override void _Process(double delta)
    {
        AskForAchievements((float)delta);
        if (_banner is null || _canvas is null)
        {
            return;
        }

        _bannerTime += (float)delta;
        if (_bannerTime >= BannerSeconds)
        {
            SkipBanner();
            return;
        }

        _banner.Update((float)delta, GetViewportRect().Size);
        _canvas.Submit(_banner.DrawList.Items);
    }

    public override void _Input(InputEvent @event)
    {
        if (_banner is not null && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            SkipBanner();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Ends the banner and shows the table.</summary>
    public void SkipBanner()
    {
        if (_banner is null)
        {
            return;
        }

        _banner = null;
        if (_canvas is not null)
        {
            _canvas.Visible = false;
        }

        if (_column is not null)
        {
            _column.Visible = true;
        }

        MenuButton?.GrabFocus();
    }

    /// <summary>The player's name, with his rank emblem and level before it when he has one.</summary>
    private void AddNameCell(string name, int rank, Color colour)
    {
        PlayerRank r = PlayerRank.Unpack(rank);
        if (r.IsNone)
        {
            AddCell(name, colour);
            return;
        }

        var cell = new HBoxContainer();
        cell.AddThemeConstantOverride("separation", 8);
        cell.AddChild(new RankBadge(22).Show(r));
        cell.AddChild(new Label { Text = name, Modulate = colour });
        Table!.AddChild(cell);
    }

    private void AddCell(string text, Color colour) => Table!.AddChild(new Label { Text = text, Modulate = colour });

    public override void _ExitTree()
    {
        if (Online is { } online)
        {
            online.Changed -= RefreshWaiting;
        }
    }

    /// <summary>Online: the players the next round still waits for.</summary>
    private void RefreshWaiting()
    {
        if (Online is not { } online || Waiting is null)
        {
            return;
        }

        var waiting = new System.Collections.Generic.List<string>();
        foreach (OpenScct.Server.Online.LobbyPlayer p in online.Lobby.Players)
        {
            if (!p.IsSpectator && !p.Ready)
            {
                waiting.Add(p.Name);
            }
        }

        Waiting.Text = waiting.Count == 0 ? "Everyone is ready." : "Waiting for " + string.Join(", ", waiting) + ".";
        if (RematchButton is not null && online.Lobby.Local is { Ready: true })
        {
            RematchButton.Disabled = true;
        }
    }

    private void ToMainMenu()
    {
        if (Online is not null)
        {
            OpenScctGame.Online.OnlineService.Get(GetTree()).Leave();
            return;
        }

        FrontEnd.ShowMainMenu(GetTree());
    }

    private void Rematch()
    {
        if (Online is { } online)
        {
            online.SetReady(true);
            if (RematchButton is not null)
            {
                RematchButton.Disabled = true;
            }

            return;
        }

        if (Result?.Request is { } request)
        {
            FrontEnd.ShowLobby(GetTree(), PlayEntry.Current.CreateLobby(request, host: true) ?? new LocalLobby(request));
        }
    }
}
