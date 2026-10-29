using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>Switches for things the original HUD pages do not have.</summary>
public sealed class HudOptions
{
    /// <summary>
    /// Draws the merc's fire mode under the ammo count and the active vision mode next to the gadget box. The
    /// original shows the fire mode on the rifle's sight screen and the vision mode only as the picture itself, so
    /// these lines are a slice addition until the first-person weapon draws its sight skins.
    /// </summary>
    public bool SliceExtras { get; set; } = true;

    /// <summary>
    /// Shows the spy's objective markers even when HUD.EnhancedRealityLevel is 0. The level comes from the player's
    /// profile in the original; until profiles are ported the slice turns the markers on here.
    /// </summary>
    public bool EnhancedReality { get; set; } = true;

    /// <summary>
    /// Stretches every page over the whole screen as the unpatched game does (Reloaded's widescreen off); otherwise
    /// each area keeps the 4:3 scale and is anchored to its side.
    /// </summary>
    public bool StretchHud { get; set; }

    /// <summary>Reloaded's ping column next to the connected players list (rl:qol.scoreboard show_ping).</summary>
    public bool ShowPing { get; set; }

    /// <summary>Reloaded's classic_crosshair: the merc reticle at the original off-centre spot (rl:qol.crosshair).</summary>
    public bool ClassicCrosshair { get; set; }

    /// <summary>The merc's custom crosshair; while it is off the original reticle draws.</summary>
    public OpenScctGame.Options.CrosshairSettings Crosshair { get; set; } = new();

    /// <summary>
    /// Reloaded's scoreboard_split_team_size (1 to 4): once the bigger team has this many players, the connected
    /// players list shows a column per team; 0 never splits.
    /// </summary>
    public int ScoreboardSplit { get; set; } = 3;

    /// <summary>The name of the key a command is bound to in the player's layout (for the key tokens of page texts), or null.</summary>
    public Func<string, string?>? KeyName { get; set; }

    /// <summary>
    /// The localized name of the first key whose whole binding is the given string, or null (the key tokens of drawn
    /// texts, <see cref="OpenScctGame.Controls.KeyTokens"/>).
    /// </summary>
    public Func<string, string?>? KeyBoundTo { get; set; }
}

/// <summary>
/// A driver for page areas another module owns (the gadget overlays: sticky cam menu, mine menu, backpack, gas mask,
/// heart sensor). The page runs it after its own drivers and stops hiding the areas it names.
/// </summary>
public interface IHudAreaDriver
{
    /// <summary>The page side it runs on, or null for both.</summary>
    HudSide? Side { get; }

    /// <summary>Top-level instance names it drives (the page leaves their visibility to it).</summary>
    IReadOnlyCollection<string> Areas { get; }

    /// <summary>Reads what it needs from the local controller (null without one), once a frame before the pages run.</summary>
    void Read(OpenScct.Game.SBase.SPlayerController? pc, HudSnapshot s);

    /// <summary>Drives its areas of <paramref name="page"/> for this frame.</summary>
    void Drive(HudPage page, HudSnapshot s, float dt);
}

/// <summary>
/// A full page that replaces the spy or merc page while it is active (binoculars, sticky cam view, camera network),
/// as the native page manager swaps pages.
/// </summary>
public interface IHudPageOverride
{
    /// <summary>The page to update and draw instead of the player page this frame, or null.</summary>
    HudPage? ActivePage(OpenScct.Game.SBase.SPlayerController? pc, HudSnapshot s);
}

/// <summary>
/// One Magma page driven from a <see cref="HudSnapshot"/>, the way the native page object drives its areas: it shows
/// and hides instances, restarts their animations, writes texts and moves widgets, then the page clock runs and the
/// page is drawn into a <see cref="MagmaDrawList"/>.
/// </summary>
public abstract class HudPage
{
    private readonly Dictionary<string, string> _lastTexts = new(StringComparer.Ordinal);

    protected HudPage(HudAssets assets, string pageName, HudOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        Assets = assets;
        Options = options ?? new HudOptions();
        MagmaPackage package = assets.Hud;
        if (!package.Pages.TryGetValue(pageName, out int area))
        {
            throw new InvalidOperationException($"page {pageName} is not in the Hud package");
        }

        Root = new MagmaNode(package, area);
        Renderer = new MagmaRenderer
        {
            TextureSize = assets.TextureSize,
            MeasureText = (font, text, height, tracking) => assets.Font(font)?.Measure(text, height, 1f, tracking) ?? 0f,
            ExpandText = text => OpenScctGame.Controls.KeyTokens.Expand(text, Options.KeyBoundTo),
        };
        // The first update stops every top-level area at frame 0 (the native shared init) and the page inits hide
        // the areas their drivers own: everything starts hidden here and each driver shows what it draws.
        foreach (MagmaNode child in Root.Children)
        {
            child.SetFrame(0);
            child.Playing = false;
            child.SetVisible(false);
        }
    }

    public HudAssets Assets { get; }

    public HudOptions Options { get; }

    public MagmaNode Root { get; }

    public MagmaRenderer Renderer { get; }

    public MagmaDrawList DrawList { get; } = new();

    private bool? _ownStretch;

    public abstract HudSide Side { get; }

    /// <summary>Drivers of areas other modules own, run after the page's own.</summary>
    public List<IHudAreaDriver> Drivers { get; } = new();

    /// <summary>Drives the areas from <paramref name="s"/>, then advances the page clocks by <paramref name="dt"/>.</summary>
    public void Update(HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(s);
        Root.SetVisible(s.Valid && !s.HideHud, children: false);
        if (Root.Visible)
        {
            Drive(s, dt);
            foreach (IHudAreaDriver d in Drivers)
            {
                if (d.Side is null || d.Side == Side)
                {
                    d.Drive(this, s, dt);
                }
            }
        }

        Root.Tick(dt);
    }

    /// <summary>Draws the page for a screen of <paramref name="screen"/> pixels into <see cref="DrawList"/>.</summary>
    public void Draw(Vector2 screen, HudSnapshot s)
    {
        DrawList.Clear();
        _ownStretch ??= Renderer.StretchAll;
        Renderer.StretchAll = _ownStretch.Value || Options.StretchHud;
        Renderer.DrawPage(Root, screen, DrawList);
        if (Root.Visible)
        {
            DrawGadgetCross(s);
            DrawCanvas(screen, s);
        }

        if (Root.Visible && Options.SliceExtras)
        {
            DrawExtras(s);
        }

        if (Root.Visible)
        {
            DrawBoard(s);
        }
    }

    protected abstract void Drive(HudSnapshot s, float dt);

    protected virtual void DrawExtras(HudSnapshot s)
    {
    }

    /// <summary>What the page's SHud state draws on the canvas over the page (the binocular markers).</summary>
    protected virtual void DrawCanvas(Vector2 screen, HudSnapshot s)
    {
    }

    /// <summary>The instance at <paramref name="path"/> below the page root ("Gadget", "Reticule/Reticule").</summary>
    public MagmaNode? Area(string path) => Root.Find(path);

    /// <summary>Shows an instance; when it was hidden it restarts from frame 0 (native SetVisible + SetTime(0) + Play).</summary>
    public static void Show(MagmaNode? node, bool restart = true)
    {
        if (node is null || node.Visible)
        {
            return;
        }

        node.SetVisible(true);
        if (restart)
        {
            node.SetFrame(0);
            node.Playing = true;
        }
    }

    public static void Hide(MagmaNode? node)
    {
        if (node is { Visible: true })
        {
            node.SetVisible(false);
        }
    }

    public static void SetShown(MagmaNode? node, bool shown, bool restart = true)
    {
        if (shown)
        {
            Show(node, restart);
        }
        else
        {
            Hide(node);
        }
    }

    public static void SetText(MagmaNode? node, string widget, string text)
    {
        if (node is not null)
        {
            node.Widget(widget).Text = text;
        }
    }

    /// <summary>True when <paramref name="text"/> differs from the last value given for <paramref name="key"/>.</summary>
    protected bool Changed(string key, string text)
    {
        if (_lastTexts.TryGetValue(key, out string? last) && last == text)
        {
            return false;
        }

        _lastTexts[key] = text;
        return true;
    }

    // ---- shared drivers ---------------------------------------------------------------------------------------

    private float _shownHealth = -1f;

    /// <summary>
    /// A_Jauge / A_JaugeEnergie: the fill's top at min(133, 137 - value / 100 x 133) and the two triangles 3 units
    /// above it (native gauge 0x10b30090).
    /// </summary>
    protected static void DriveGauge(MagmaNode gauge, float percent)
    {
        float top = Math.Min(HudK.GaugeBottom, HudK.GaugeTopOffset - Math.Clamp(percent, 0f, 100f) / 100f * HudK.GaugeSpan);
        gauge.Widget("jaugeinterne").Top = top;
        gauge.Widget("jaugeindicator_gauche").MoveTop = top - HudK.GaugeArrowLift;
        gauge.Widget("jaugeindicator_droite").MoveTop = top - HudK.GaugeArrowLift;
    }

    protected void DriveHealth(HudSnapshot s, float dt)
    {
        MagmaNode? vie = Area("Vie");
        SetShown(vie, s.HasPawn && !s.LifeOff, restart: false);
        if (vie is null || !s.HasPawn)
        {
            _shownHealth = -1f;
            return;
        }

        // The native gauge (0x10b30090) starts a frame-count curve on each change, but its blend factor is the int
        // truncation of (1 - curve), which is 0 on every frame: fill and triangles show the current health at once.
        _shownHealth = Math.Max(0, s.Health);
        DriveGauge(vie, _shownHealth);
        SetShown(vie.Find("poison"), s.Poisoned);
    }

    /// <summary>The health the gauge shows now (the real value, clamped at 0).</summary>
    public float ShownHealth => _shownHealth;

    /// <summary>
    /// Objectif (spec/hud/counters-and-prompts.md rule 2): the Story count, or in a deathmatch the two team lives on
    /// the DeathMatch counter, or in Disk Hunt the disks; the shown counter restarts when its value changes.
    /// </summary>
    protected void DriveObjectives(HudSnapshot s)
    {
        MagmaNode? objectif = Area("Objectif");
        SetShown(objectif, true, restart: false);
        MagmaNode? story = objectif?.Find("Story");
        MagmaNode? dm = objectif?.Find("DeathMatch");
        bool deathMatch = s.CounterMode == HudCounterMode.DeathMatch;
        MagmaNode? shown = deathMatch ? dm : story;
        if (deathMatch)
        {
            Hide(story);
        }
        else
        {
            Hide(dm);
        }

        string text = s.CounterText.Length > 0 ? s.CounterText : s.ObjectivesLeft.ToString(CultureInfo.InvariantCulture);
        SetText(shown, "Objectif", text);
        if (shown is not null && Changed(deathMatch ? "objectives-dm" : "objectives", text))
        {
            shown.Restart();
        }
    }

    /// <summary>Chrono: the time-limit countdown, hidden without a time limit or past it.</summary>
    protected void DriveChrono(HudSnapshot s)
    {
        MagmaNode? chrono = Area("Chrono");
        string? text = s.TimeOff ? null : HudK.ChronoText(s.TimeLimit, s.ElapsedTime);
        SetShown(chrono, text is not null, restart: false);
        if (text is not null && chrono is not null)
        {
            SetText(chrono, "Chrono", text);
            chrono.Widget("Chrono").KeepInside = true;
        }
    }

    /// <summary>
    /// The Hacking, Objective and Alarm countdown boxes (open over f0..f6 and hold). Returns whether an objective box
    /// is in use (the merc's alert box follows it).
    /// </summary>
    protected bool DriveCountdowns(HudSnapshot s)
    {
        Countdown(Area("ChronoHack"), "Chrono_Hack", s.HackCountdown);
        Countdown(Area("ChronoObjectif"), "Chrono_Objectif", s.ObjectiveCountdowns.Count > 0 ? s.ObjectiveCountdowns[0] : null);
        Countdown(Area("ChronoObjectif2"), "Chrono_Objectif2", s.ObjectiveCountdowns.Count > 1 ? s.ObjectiveCountdowns[1] : null);
        Countdown(Area("ChronoAlarm"), "Chrono_Alarm", s.AlarmSeconds is { } a ? new HudCountdown(a) : null);
        return s.ObjectiveCountdowns.Count > 0;
    }

    private static void Countdown(MagmaNode? box, string widget, HudCountdown? value)
    {
        if (box is null)
        {
            return;
        }

        if (value is not { } v)
        {
            Hide(box);
            return;
        }

        SetText(box, widget, HudK.CountdownText(v.Seconds));
        if (!box.Visible)
        {
            Show(box);
        }
        else if (box.Frame == 0)
        {
            box.Playing = true;
        }
    }

    /// <summary>Gadget: shown with gadget slots, a selected gadget and the gadget display on.</summary>
    protected void DriveGadget(HudSnapshot s)
    {
        MagmaNode? box = Area("Gadget");
        bool shown = s.TeamGadgetSlots > 0 && s.Gadget is not null && !s.GadgetOff;
        SetShown(box, shown, restart: false);
        if (!shown || box is null || s.Gadget is not { } g)
        {
            return;
        }

        SetText(box, "NbGadget", g.Charge < 0 ? HudK.NoCharge : g.Charge.ToString(CultureInfo.InvariantCulture));
        SetText(box, "NomGadget", g.Name);
        // The icon is a HUDConfig region named by the gadget's MagmaIconName in Options.mgm (C_Default_2 when missing).
        MagmaPackage? options = Assets.Package("Options");
        int icon = options?.FindMaterial(g.MagmaIconName) ?? -1;
        if (icon < 0)
        {
            icon = options?.FindMaterial("C_Default_2") ?? -1;
        }

        MagmaOverride image = box.Widget("Gadget");
        if (icon >= 0)
        {
            image.Region = options!.Materials[icon].Region;
        }

        image.Color = g.Unavailable ? UnavailableIconColour : null;
    }

    /// <summary>An unavailable gadget's icon: greyed and half transparent, like an unavailable gadget menu line.</summary>
    private static readonly Color UnavailableIconColour = new(0.5f, 0.5f, 0.5f, 0.6f);

    /// <summary>The cross over an unavailable gadget's icon.</summary>
    private static readonly Color GadgetCrossColour = new(0.9f, 0.12f, 0.1f, 0.95f);

    private WhiteRegion? _white;

    /// <summary>
    /// An OpenSCCT addition: a red cross drawn over the gadget box's icon when the selected gadget cannot work on this
    /// level (<see cref="HudGadget.Unavailable"/>), on top of the drawn page.
    /// </summary>
    private void DrawGadgetCross(HudSnapshot s)
    {
        if (s.Gadget is not { Unavailable: true } || DrawList.Find("Gadget/Gadget") is not { Points.Length: >= 4 } icon)
        {
            return;
        }

        _white ??= WhiteRegion.Find(Assets);
        if (_white is not { } white)
        {
            return;
        }

        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        foreach (Vector2 p in icon.Points)
        {
            l = Mathf.Min(l, p.X);
            t = Mathf.Min(t, p.Y);
            r = Mathf.Max(r, p.X);
            b = Mathf.Max(b, p.Y);
        }

        float inset = (r - l) * 0.12f;
        var tl = new Vector2(l + inset, t + inset);
        var br = new Vector2(r - inset, b - inset);
        float thickness = Mathf.Max(2f, (r - l) * 0.1f);
        DrawList.Items.Add(Bar("Extras/GadgetCrossA", tl, br, thickness, white));
        DrawList.Items.Add(Bar("Extras/GadgetCrossB", new Vector2(br.X, tl.Y), new Vector2(tl.X, br.Y), thickness, white));
    }

    /// <summary>A solid bar from <paramref name="from"/> to <paramref name="to"/>, <paramref name="thickness"/> pixels wide.</summary>
    private static MagmaDrawItem Bar(string path, Vector2 from, Vector2 to, float thickness, WhiteRegion white)
    {
        Vector2 dir = (to - from).Normalized();
        Vector2 side = new Vector2(-dir.Y, dir.X) * (thickness / 2f);
        Color c = GadgetCrossColour;
        return new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Image,
            Package = white.Package,
            Texture = white.Texture,
            Points = [from + side, to + side, to - side, from - side],
            Uvs = [white.Uv, white.Uv, white.Uv, white.Uv],
            Colors = [c, c, c, c],
        };
    }

    /// <summary>
    /// The four message areas (spec/hud/messages.md rules 8 and 9): the stacks' live entries after a leading line
    /// break, the game queue's head alone; an area shows from frame 0 when it appears and is not restarted when its
    /// text changes. The zone area is not driven in a deathmatch; the game area waits while the tutorial shows.
    /// </summary>
    protected void DriveMessages(HudSnapshot s)
    {
        Message("SystemMessage", "messageSystem", HudMessages.StackText(s.SystemMessages));
        Message("StateMessage", "messageState", HudMessages.StackText(s.StateMessages));
        if (s.CounterMode != HudCounterMode.DeathMatch)
        {
            Message("ZoneMessage", "_ZoneMessage", HudMessages.StackText(s.InfoMessages));
        }

        if (s.Tutorial.Length == 0)
        {
            Message("GameMessage", "messageGame", s.GameMessages.Count > 0 ? s.GameMessages[0] : "");
        }
    }

    private void Message(string area, string widget, string text)
    {
        MagmaNode? node = Area(area);
        if (node is null)
        {
            return;
        }

        if (text.Length == 0)
        {
            Hide(node);
            return;
        }

        SetText(node, widget, text);
        Show(node);
    }

    /// <summary>TutoMessage: the tutorial text while it is longer than four characters, restarted on show.</summary>
    protected void DriveTutorial(HudSnapshot s)
    {
        MagmaNode? tuto = Area("TutoMessage");
        SetShown(tuto, s.Tutorial.Length > 0);
        if (s.Tutorial.Length > 0)
        {
            SetText(tuto?.Find("Tuto"), "messageTuto", s.Tutorial);
        }
    }

    private float _ballisticLatch;

    /// <summary>
    /// The throw charge (Bridge spy page notes 3.1): hidden at 0; below 1 paused on frame trunc(10 v); at 1 it plays
    /// from frame 10 once (the full-bar blink), latched until the area hides.
    /// </summary>
    protected void DriveBallistic(MagmaNode? area, float v)
    {
        if (area is null)
        {
            return;
        }

        if (v <= 0f)
        {
            Hide(area);
            _ballisticLatch = 0f;
            return;
        }

        if (!area.Visible)
        {
            area.SetVisible(true);
        }

        if (v < 1f)
        {
            area.SetFrame((int)(10f * v));
            area.Playing = false;
        }
        else if (_ballisticLatch != v)
        {
            _ballisticLatch = v;
            area.SetFrame(10);
            area.Playing = true;
        }
    }

    /// <summary>Connected players (Bridge spy page notes 8): up to four rows of name, objectives or kills and lives.</summary>
    protected void DrivePlayers(HudSnapshot s)
    {
        MagmaNode? list = Area("PCConnectedPlayers");
        SetShown(list, s.ShowPlayers, restart: false);
        if (list is null || !s.ShowPlayers)
        {
            return;
        }

        string infinite = Assets.Localize("MagmaDynamic", "Magma", "Inifite_Short", "Inf.");
        List<HudPlayerRow> rows = ListRows(s);
        for (int i = 0; i < HudSimReader.MaxConnectedPlayers; i++)
        {
            string n = (i + 1).ToString(CultureInfo.InvariantCulture);
            HudPlayerRow? row = i < rows.Count ? rows[i] : null;
            list.Widget("PCTextPlayer" + n).Hidden = row is null;
            list.Widget("PCText_ObjOrKills" + n).Hidden = row is null || row.Spectator;
            list.Widget("PCText_Lives" + n).Hidden = row is null || row.Spectator;
            list.Widget("IcoSpy" + n).Hidden = row is null || row.Spectator || row.Team != 0;
            list.Widget("IcoMerc" + n).Hidden = row is null || row.Spectator || row.Team != 1;
            list.Widget("IcoSpectator" + n).Hidden = row is null || !row.Spectator;
            if (row is null)
            {
                continue;
            }

            list.Widget("PCTextPlayer" + n).Text = row.Name;
            list.Widget("PCText_ObjOrKills" + n).Text = row.ObjOrKills.ToString(CultureInfo.InvariantCulture);
            list.Widget("PCText_Lives" + n).Text = row.Lives >= HudK.InfiniteLives ? infinite : row.Lives.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The rows of the connected players list: the list as read (spies, mercs, spectators, four rows), or, once the
    /// bigger team has <see cref="HudOptions.ScoreboardSplit"/> players and both teams have someone, the spies only:
    /// the mercs then get their own panel beside it (<see cref="DrawBoard"/>). That is Reloaded's split
    /// (rl:qol.scoreboard, scoreboard_split_team_size).
    /// </summary>
    protected List<HudPlayerRow> ListRows(HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return SplitTeams(s) ? TeamRows(s, 0) : s.Players;
    }

    /// <summary>Whether the list splits into a column per team.</summary>
    protected bool SplitTeams(HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (Options.ScoreboardSplit <= 0)
        {
            return false;
        }

        int spies = 0, mercs = 0;
        foreach (HudScoreRow r in s.Score)
        {
            if (r.Spectator)
            {
                continue;
            }

            if (r.Team == 0)
            {
                spies++;
            }
            else
            {
                mercs++;
            }
        }

        return spies > 0 && mercs > 0 && Math.Max(spies, mercs) >= Options.ScoreboardSplit;
    }

    /// <summary>A team's players as list rows (at most four, the list's rows).</summary>
    private static List<HudPlayerRow> TeamRows(HudSnapshot s, int team)
    {
        var rows = new List<HudPlayerRow>();
        foreach (HudScoreRow r in s.Score)
        {
            if (!r.Spectator && r.Team == team && rows.Count < HudSimReader.MaxConnectedPlayers)
            {
                rows.Add(new HudPlayerRow(r.Name, r.Team, false, r.ObjOrKills, r.Lives, r.Ping, r.Rank));
            }
        }

        return rows;
    }

    /// <summary>
    /// OpenSCCT's look for the connected players list (F10): the Magma list's widgets are driven as before (their
    /// texts are the values) but drawn here instead, as a translucent panel with a header row, a side icon per row,
    /// centred columns sized to their titles (score, lives, and Reloaded's ping and OpenSCCT's rank when shown), the
    /// local player's row tinted in their side's colour and the ping coloured by its size. The panel's bottom edge sits
    /// where the Magma list ends, centred on it. A split list (<see cref="SplitTeams"/>) draws a panel per team side by
    /// side.
    /// </summary>
    protected void DrawBoard(HudSnapshot s)
    {
        if (!s.ShowPlayers || DrawList.Find("PCConnectedPlayers/PCText_LivesTitle") is not { } title)
        {
            return;
        }

        string scoreTitle = DrawList.TextOf("PCConnectedPlayers/PCText_ObjOrKillsTitle") ?? "Obj / Kills";
        string livesTitle = title.Text;
        MagmaAnchor anchor = title.Anchor;
        Rect4 t = title.PageRect;
        DrawList.Items.RemoveAll(i => i.Path.StartsWith("PCConnectedPlayers/", StringComparison.OrdinalIgnoreCase));
        _white ??= WhiteRegion.Find(Assets);

        string local = "";
        foreach (HudScoreRow r in s.Score)
        {
            if (r.Local)
            {
                local = r.Name;
                break;
            }
        }

        float centre = t.L - LivesTitleLeft + ListWidth / 2f;
        float bottom = t.T + BoardBottom;
        if (SplitTeams(s))
        {
            BoardLayout spies = Layout(TeamRows(s, 0), "Spies", scoreTitle, livesTitle);
            BoardLayout mercs = Layout(TeamRows(s, 1), "Mercenaries", "Kills", livesTitle);
            float left = centre - (spies.Width + BoardGap + mercs.Width) / 2f;
            DrawPanel(spies, left, bottom, anchor, local, HudK.SpyColour, "PCConnectedPlayers/", "Extras/");
            DrawPanel(mercs, left + spies.Width + BoardGap, bottom, anchor, local, HudK.MercColour, "Extras/Split", "Extras/Split");
        }
        else
        {
            BoardLayout all = Layout(ListRows(s), "Players", scoreTitle, livesTitle);
            Color accent = Side == HudSide.Merc ? HudK.MercColour : HudK.SpyColour;
            DrawPanel(all, centre - all.Width / 2f, bottom, anchor, local, accent, "PCConnectedPlayers/", "Extras/");
        }
    }

    /// <summary>Page width of the Magma list's own columns (name 20 to 170, kills to 200, lives to 255).</summary>
    private const float ListWidth = 255f;

    /// <summary>The list's lives title starts this far from the list's left (PCText_LivesTitle rect 205).</summary>
    private const float LivesTitleLeft = 205f;

    /// <summary>The board's bottom edge below the list's top (the Magma list's fourth row ends at 48).</summary>
    private const float BoardBottom = 52f;

    private const float BoardPad = 4f;
    private const float BoardRow = 11f;
    private const float BoardIcon = 8f;
    private const float BoardColumnGap = 10f;
    private const float BoardGap = 8f;
    private const float BoardNameMin = 60f;
    private const float BoardNameMax = 120f;

    private static readonly Color BoardBack = new(0f, 0f, 0f, 0.5f);
    private static readonly Color BoardHeaderBack = new(0f, 0f, 0f, 0.35f);
    private static readonly Color BoardStripe = new(1f, 1f, 1f, 0.035f);
    private static readonly Color BoardTitleText = new(1f, 1f, 1f, 0.6f);
    private static readonly Color BoardDim = new(1f, 1f, 1f, 0.45f);
    private static readonly Color PingGood = new(0.55f, 0.95f, 0.55f, 1f);
    private static readonly Color PingFair = new(1f, 0.85f, 0.35f, 1f);
    private static readonly Color PingBad = new(1f, 0.4f, 0.35f, 1f);

    private sealed record BoardLayout(List<HudPlayerRow> Rows, string Heading, string ScoreTitle, string LivesTitle,
        float NameWidth, float ScoreWidth, float LivesWidth, float PingWidth, float RankWidth)
    {
        public float Width => BoardPad + BoardIcon + 4f + NameWidth + ScoreWidth + LivesWidth + PingWidth + RankWidth + BoardPad;
    }

    private BoardLayout Layout(List<HudPlayerRow> rows, string heading, string scoreTitle, string livesTitle)
    {
        bool ranks = rows.Exists(p => p.Rank != 0);
        float name = Measure(heading);
        foreach (HudPlayerRow r in rows)
        {
            name = Math.Max(name, Measure(r.Name));
        }

        name = Math.Clamp(name + BoardColumnGap, BoardNameMin, BoardNameMax);
        float Column(string head, float min) => Math.Max(Measure(head), min) + BoardColumnGap;
        return new BoardLayout(rows, heading, scoreTitle, livesTitle, name, Column(scoreTitle, 14f), Column(livesTitle, 14f),
            Options.ShowPing ? Column("Ping", 18f) : 0f, ranks ? Column("Rank", 30f) : 0f);
    }

    /// <summary>Width of <paramref name="text"/> in page units at the list's text height.</summary>
    private float Measure(string text)
    {
        const float h = HudK.PingTextHeight;
        return Assets.Font(HudAssets.DefaultFont)?.Measure(text, h, 1f, 0.045f * h) ?? text.Length * h * 0.6f;
    }

    /// <summary>
    /// One panel of the board, bottom edge at <paramref name="bottom"/>. Text paths keep the Magma list's names
    /// (<paramref name="listPrefix"/>: PCTextPlayerN, PCText_ObjOrKillsN, PCText_LivesN) and the extras' (PingN, RankN).
    /// </summary>
    private void DrawPanel(BoardLayout b, float left, float bottom, MagmaAnchor a, string local, Color accent,
        string listPrefix, string extraPrefix)
    {
        float top = bottom - (BoardPad * 2f + BoardRow * (b.Rows.Count + 1));
        float right = left + b.Width;
        float headerBottom = top + BoardPad + BoardRow;
        Quad("Extras/BoardBack" + listPrefix, new Rect4(left, top, right, bottom), a, BoardBack);
        Quad("Extras/BoardHeader" + listPrefix, new Rect4(left, top, right, headerBottom), a, BoardHeaderBack);
        Quad("Extras/BoardAccent" + listPrefix, new Rect4(left, headerBottom - 0.6f, right, headerBottom), a, accent with { A = 0.85f });

        float nameX = left + BoardPad + BoardIcon + 4f;
        float scoreX = nameX + b.NameWidth;
        float livesX = scoreX + b.ScoreWidth;
        float pingX = livesX + b.LivesWidth;
        float rankX = pingX + b.PingWidth;
        float y = top + BoardPad;
        void Cell(string path, string text, float x, float w, float rowTop, Color colour, MagmaAlign align = MagmaAlign.Center) =>
            ExtraText(path, text, new Rect4(x, rowTop + 1.5f, x + w, rowTop + 1.5f + BoardRow), a, align, colour, HudK.PingTextHeight);

        Cell("Extras/BoardHeading" + listPrefix, b.Heading, nameX, b.NameWidth, y, accent, MagmaAlign.Left);
        Cell(listPrefix + "PCText_ObjOrKillsTitle", b.ScoreTitle, scoreX, b.ScoreWidth, y, BoardTitleText);
        Cell(listPrefix + "PCText_LivesTitle", b.LivesTitle, livesX, b.LivesWidth, y, BoardTitleText);
        if (b.PingWidth > 0f)
        {
            Cell(extraPrefix + "PingTitle", "Ping", pingX, b.PingWidth, y, BoardTitleText);
        }

        if (b.RankWidth > 0f)
        {
            Cell(extraPrefix + "RankTitle", "Rank", rankX, b.RankWidth, y, BoardTitleText);
        }

        string infinite = Assets.Localize("MagmaDynamic", "Magma", "Inifite_Short", "Inf.");
        var white = new Color(1f, 1f, 1f, 1f);
        for (int i = 0; i < b.Rows.Count; i++)
        {
            HudPlayerRow row = b.Rows[i];
            string n = (i + 1).ToString(CultureInfo.InvariantCulture);
            float rowTop = y + BoardRow * (i + 1);
            var band = new Rect4(left, rowTop, right, rowTop + BoardRow);
            Color side = row.Spectator ? BoardDim : row.Team == 1 ? HudK.MercColour : HudK.SpyColour;
            if (local.Length > 0 && row.Name == local)
            {
                Quad("Extras/BoardLocal" + listPrefix + n, band, a, side with { A = 0.16f });
            }
            else if (i % 2 == 1)
            {
                Quad("Extras/BoardStripe" + listPrefix + n, band, a, BoardStripe);
            }

            float iconTop = rowTop + (BoardRow - BoardIcon) / 2f;
            Icon("Extras/BoardIcon" + listPrefix + n, row.Spectator ? "Spectator" : row.Team == 1 ? "Merc" : "Spy",
                new Rect4(left + BoardPad, iconTop, left + BoardPad + BoardIcon, iconTop + BoardIcon), a, side);
            Cell(listPrefix + "PCTextPlayer" + n, row.Name, nameX, b.NameWidth, rowTop, row.Spectator ? BoardDim : white, MagmaAlign.Left);
            if (row.Spectator)
            {
                continue;
            }

            Cell(listPrefix + "PCText_ObjOrKills" + n, row.ObjOrKills.ToString(CultureInfo.InvariantCulture), scoreX, b.ScoreWidth, rowTop, white);
            Cell(listPrefix + "PCText_Lives" + n, row.Lives >= HudK.InfiniteLives ? infinite : row.Lives.ToString(CultureInfo.InvariantCulture),
                livesX, b.LivesWidth, rowTop, row.Lives == 0 ? BoardDim : white);
            if (b.PingWidth > 0f)
            {
                int ms = row.Ping * HudK.PingUnit;
                Cell(extraPrefix + "Ping" + n, ms.ToString(CultureInfo.InvariantCulture), pingX, b.PingWidth, rowTop,
                    ms < 80 ? PingGood : ms < 150 ? PingFair : PingBad);
            }

            if (b.RankWidth > 0f && row.Rank != 0)
            {
                Cell(extraPrefix + "Rank" + n, OpenScct.Net.Cosmetics.PlayerRank.Unpack(row.Rank).Tag, rankX, b.RankWidth, rowTop, white);
            }
        }
    }

    /// <summary>A solid page-unit rectangle of the board, at least a pixel tall.</summary>
    private void Quad(string path, Rect4 r, MagmaAnchor a, Color colour)
    {
        if (_white is not { } white)
        {
            return;
        }

        Vector2 p0 = Renderer.ToScreen(new Vector2(r.L, r.T), a);
        Vector2 p1 = Renderer.ToScreen(new Vector2(r.R, r.B), a);
        if (p1.Y - p0.Y < 1f)
        {
            p0.Y = p1.Y - 1f;
        }

        DrawList.Items.Add(white.Quad(path, p0, p1 - p0, colour));
    }

    /// <summary>A Hud package material (Spy, Merc, Spectator) drawn in a page-unit rectangle.</summary>
    private void Icon(string path, string material, Rect4 r, MagmaAnchor a, Color colour)
    {
        MagmaPackage hud = Assets.Hud;
        int m = hud.FindMaterial(material);
        if (m < 0)
        {
            return;
        }

        MagmaMaterial mat = hud.Materials[m];
        if (Assets.TextureSize(hud.Name, mat.Texture) is not { } size || size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        Vector2 p0 = Renderer.ToScreen(new Vector2(r.L, r.T), a);
        Vector2 p1 = Renderer.ToScreen(new Vector2(r.R, r.B), a);
        Rect4 g = mat.Region;
        var uv0 = new Vector2(g.L / size.X, g.T / size.Y);
        var uv1 = new Vector2(g.R / size.X, g.B / size.Y);
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Image,
            Package = hud.Name,
            Texture = mat.Texture,
            Points = [p0, new Vector2(p1.X, p0.Y), p1, new Vector2(p0.X, p1.Y)],
            Uvs = [uv0, new Vector2(uv1.X, uv0.Y), uv1, new Vector2(uv0.X, uv1.Y)],
            Colors = [colour, colour, colour, colour],
        });
    }

    /// <summary>The page position of a top-level instance from its first key.</summary>
    protected Vector2 InstanceOrigin(MagmaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        MagmaWidget w = Root.Area.Widgets[node.WidgetIndex];
        return w.Keys.Count > 0 ? w.Keys[0].Pos : Vector2.Zero;
    }

    /// <summary>Areas the slice does not drive yet: hidden, as the native inits leave them until their driver runs.</summary>
    protected void HideAll(params string[] names)
    {
        foreach (string name in names)
        {
            if (!OwnedByDriver(name))
            {
                Hide(Area(name));
            }
        }
    }

    /// <summary>Whether an area belongs to one of <see cref="Drivers"/>.</summary>
    protected bool OwnedByDriver(string name)
    {
        foreach (IHudAreaDriver d in Drivers)
        {
            if (d.Side is not null && d.Side != Side)
            {
                continue;
            }

            foreach (string area in d.Areas)
            {
                if (string.Equals(area, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>An extra text line in the HUD font (slice additions), at page units with the anchor of the area.</summary>
    protected void ExtraText(string path, string text, Rect4 pageRect, MagmaAnchor anchor, MagmaAlign align, Color color,
        float pageHeight = HudK.ExtraTextHeight)
    {
        if (text.Length == 0)
        {
            return;
        }

        Vector2 p0 = Renderer.ToScreen(new Vector2(pageRect.L, pageRect.T), anchor);
        Vector2 p1 = Renderer.ToScreen(new Vector2(pageRect.R, pageRect.B), anchor);
        float height = Mathf.Round(pageHeight * Renderer.S);
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Text,
            Anchor = anchor,
            PageRect = pageRect,
            Text = text,
            Font = HudAssets.DefaultFont,
            Rect = new Rect2(p0, p1 - p0),
            HeightPx = height,
            Align = align,
            Color = color,
            Shadow = new Color(0f, 0f, 0f, color.A),
            ShadowOffsetPx = new Vector2(1f, 1f) * Renderer.S,
            TrackingPx = 0.045f * height,
        });
    }
}
