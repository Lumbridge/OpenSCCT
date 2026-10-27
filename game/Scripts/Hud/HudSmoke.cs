using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScctGame.Chat;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;
using OpenScctGame.Smoke;
using OpenScctGame.UI;

namespace OpenScctGame.Hud;

/// <summary>
/// <c>--svm-smoke=hud</c>: builds a small offline scene with real SBase actors (a spy and a merc with their pawns,
/// HUDs, rifle and gadget, a mission, a terminal being hacked, an alarm, players and messages), drives the spy page
/// (P_HudATT), the merc page (P_HudDEF), the spectator page (P_SpectatorMode) and both binocular pages
/// (P_BinocularsATT with its objective markers, P_BinocularsDEF) from it through
/// <see cref="HudSimReader"/>, and checks the values that reach the drawn widgets: timer, objectives and their
/// variants, countdown, alarm and alert boxes, gauges, gadget box, ammunition, messages and the kill feed, console
/// and chat lines, lives and pings, radar, reticle with its arrows and message, posture icon, the screen effects
/// (damage wash, perturbation, shutdown, sticky shock, droplets, foam, flash), objective and GPO markers, the
/// interaction list, the respawn weapon, the frame-rate readout and the mission status page. Then it opens the
/// front-end screens (main menu, lobby, options, results). Needs the converted UI content
/// (<c>python -m svmbuild ui</c>); headless it checks the draw lists, not pixels. With a renderer and
/// <c>--svm-hud-capture=dir</c> it also saves each screen as a PNG there (for a look, kept out of git).
/// </summary>
public sealed class HudSmoke : SmokeTest
{
    public const string TestName = "hud";

    private static readonly Vector2 Screen = new(1280f, 720f);
    private const float Dt = 1f / 60f;

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private int _phase;
    private int _wait;
    private double _bannerStart;
    private Scene? _scene;
    private HudLayer? _layer;
    private ChatInput? _chat;
    private SPlayerController? _viewer;
    private Action<HudSnapshot>? _extra;
    private ResultsScreen? _results;
    private readonly string? _captureDir = CaptureDir();

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_wait > 0)
        {
            _wait--;
            return Running();
        }

        // Screens get a few frames to lay out (and, in a capture run, to be drawn) before they are checked.
        const int Settle = 4;
        switch (_phase++)
        {
            case 0:
                return Setup(context);
            case 1:
                // The first frame meets the local player (a fresh HUD); then a console line and a hit arrive.
                Run(1);
                _layer!.Receiver.TeamMessage(_scene!.SpyPc, _scene.MatePawn.PlayerReplicationInfo, "on my way", "TeamSay");
                _layer.Effects.ClientDamageHudEffect(_scene.SpyPc, (int)SDamageType.SDamage_Explosion, Now);
                Run(29);
                CheckSpy();
                CheckTextInsideScreens();
                return Running();
            case 2:
                Capture(context, "spy");
                CheckInteractionList();
                CheckSpyExtras();
                return Running();
            case 3:
                Capture(context, "spy-list");
                _viewer = _scene!.MercPc;
                _extra = null;
                Run(30);
                CheckMerc();
                return Running();
            case 4:
                Capture(context, "merc");
                CheckMercEffects();
                return Running();
            case 5:
                Capture(context, "merc-effects");
                CheckSpyBinoculars();
                return Running();
            case 6:
                Capture(context, "binoculars-spy");
                CheckMercBinoculars();
                return Running();
            case 7:
                Capture(context, "binoculars-merc");
                LeaveBinoculars();
                CheckCounters();
                CheckSpectator();
                return Running();
            case 8:
                Capture(context, "spectator");
                CheckStatusPage(context);
                _wait = Settle;
                return Running();
            case 9:
                Capture(context, "status");
                FreeStatusPage(context);
                _layer!.Visible = false;
                FrontEnd.ShowMainMenu(context.Tree);
                _wait = Settle;
                return Running();
            case 10:
                Capture(context, "main-menu");
                CheckMainMenu(context.Tree);
                FrontEnd.ShowLobby(context.Tree, new LocalLobby(new PlayRequest { PlayerName = "Tester", Side = HudSide.Merc }));
                _wait = Settle;
                return Running();
            case 11:
                CheckLobby(context.Tree);
                _wait = Settle;
                return Running();
            case 12:
                Capture(context, "lobby");
                FrontEnd.ShowOptions(context.Tree);
                _wait = Settle;
                return Running();
            case 13:
                Capture(context, "options");
                CheckOptions(context.Tree);
                _results = FrontEnd.ShowResults(context.Tree, SampleResult());
                _bannerStart = context.ElapsedSeconds;
                return Running();
            case 14:
                // Frames run faster than 60 Hz headless: wait for the banner's text (from its frame 5) by time.
                if (context.ElapsedSeconds - _bannerStart < 1.0)
                {
                    _phase--;
                    return Running();
                }

                Capture(context, "results-banner");
                CheckBanner();
                _results?.SkipBanner();
                _wait = Settle;
                return Running();
            default:
                Capture(context, "results");
                CheckResults();
                return _failures.Count == 0
                    ? SmokeResult.Pass(string.Join(" ", _notes))
                    : SmokeResult.Fail(string.Join("; ", _failures.Take(14)));
        }
    }

    private SmokeResult Running() => _failures.Count > 0 ? SmokeResult.Fail(string.Join("; ", _failures.Take(14))) : SmokeResult.Running;

    private SmokeResult Setup(SmokeContext context)
    {
        _layer = HudLayer.Create(out string? error);
        if (_layer is null)
        {
            return SmokeResult.Fail(error ?? "no UI content");
        }

        _scene = new Scene();
        _viewer = _scene.SpyPc;
        HudAssets assets = _layer.Assets!;
        var reader = new HudSimReader(() => _viewer, (section, key, fallback) => assets.Localize("MagmaDynamic", section, key, fallback), _layer.Effects);
        _layer.Source = new Composite(reader, s => _extra?.Invoke(s));
        _layer.Controller = () => _viewer;
        _layer.InitialConnectedPlayers = true;
        _layer.Options.ShowPing = true;
        _layer.ShowFps = true;
        // Headless the HUD draws for a fixed 1280 x 720 screen; a capture run draws for the real window.
        _layer.ScreenSize = _captureDir is null ? Screen : null;
        _layer.ProcessMode = Node.ProcessModeEnum.Disabled;   // stepped by the test
        _layer.Projector = _scene.Project;
        context.Tree.Root.AddChild(_layer);
        _chat = new ChatInput { ProcessMode = Node.ProcessModeEnum.Disabled };
        context.Tree.Root.AddChild(_chat);
        _layer.AddOverlay(_chat);
        return SmokeResult.Running;
    }

    private void Run(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            _layer!.Step(Dt);
        }
    }

    private float Now => _scene!.World.Level.TimeSeconds;

    // ---- spy page -------------------------------------------------------------------------------------------

    private void CheckSpy()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        HudSnapshot snap = layer.Snapshot;
        Expect(layer.ActivePage is SpyHudPage, "spy page active");
        Expect(snap.Side == HudSide.Spy, $"side {snap.Side}");
        Expect(snap.Health == 60 && snap.EnergyLevel == 50f && snap.EnergyLevelMax == 100, $"spy health {snap.Health} energy {snap.EnergyLevel}/{snap.EnergyLevelMax}");
        MagmaDrawList list = layer.Spy!.DrawList;
        ExpectText(list, "Chrono/Chrono", "8:44");
        ExpectText(list, "Objectif/Story/Objectif", "3");
        ExpectText(list, "ChronoHack/Chrono_Hack", "0:06");
        ExpectText(list, "ChronoObjectif/Chrono_Objectif", "0:07");
        ExpectText(list, "ChronoAlarm/Chrono_Alarm", "0:25");
        ExpectText(list, "Gadget/NomGadget", s.Gadget.ItemName);
        ExpectText(list, "Gadget/NbGadget", "3");
        ExpectText(list, "PCConnectedPlayers/PCTextPlayer1", "Sam");
        ExpectText(list, "PCConnectedPlayers/PCText_Lives1", "4");
        ExpectText(list, "PCConnectedPlayers/PCText_Lives3", "3");
        ExpectText(list, "Extras/Ping1", "48");
        ExpectContains(list, "SystemMessage/messageSystem", "Laboratory : Neutralization in progress");
        Expect(list.TextOf("SystemMessage/messageSystem")?.StartsWith('\n') == true, "stack text starts with the driver's line break");
        ExpectContains(list, "GameMessage/messageGame", s.Objective.Description);
        // The kill feed: the death message waits behind the mission text in the game queue (priority order).
        Expect(s.SpyHud.GameMessageQueue.Count == 2 && s.SpyHud.GameMessageQueue[1].Message == "Shadownet killed Ghost",
            $"kill line queued ({string.Join(" | ", s.SpyHud.GameMessageQueue.Select(m => m.Message))})");
        // Gauges: fill top = min(133, 137 - value x 1.33) under the area origin (y 230).
        ExpectTop(list, "Vie/jaugeinterne", 230f + 137f - 0.60f * 133f);
        ExpectTop(list, "Energie/jaugeinterne", 230f + 137f - 0.50f * 133f);
        Expect(list.Any("Spy_Speed/"), "sneak icon drawn");
        // The coop icons follow the decoded spy driver (InteractionPrompts): its mate search needs the movement
        // physics, which this synthetic scene has none of, so the ready mate here shows nothing. The play-team smoke
        // checks the drawn icons on SteeE.
        Expect(!list.Any("Coop/Coop1/") && !list.Any("Coop/Coop2/") && !list.Any("Coop/Coop3/") && !list.Any("Coop/Coop4/"),
            "no coop icon without movement physics");
        Expect(list.Find("Gadget/Gadget") is { } icon && icon.Texture.Equals("HUDConfig", StringComparison.OrdinalIgnoreCase),
            "gadget icon from HUDConfig");
        // Anchors: the gauge is right of the page centre, the timer too; the message areas straddle it.
        Expect(list.Find("Vie/jaugeinterne")?.Anchor == MagmaAnchor.Right, "health gauge right anchored");
        Expect(list.Find("SystemMessage/messageSystem")?.Anchor == MagmaAnchor.Center, "messages centred");
        Expect(layer.Spy.Radar?.Teammates == 1, $"spy radar teammates {layer.Spy.Radar?.Teammates}");
        // Objective markers: the terminal in front of the spy, red while hacked, with its percent or distance.
        HudObjective? lab = snap.Objectives.Find(o => o.Name == "Laboratory");
        Expect(layer.Markers.DrawList.Find("ER/Laboratory/icon") is { } marker && marker.Colors[0].R > marker.Colors[0].G,
            $"hacked terminal marker drawn red (markers {layer.Markers.DrawList.Items.Count}, taken {lab?.Taken}, " +
            $"at {s.Project(lab?.Location ?? default)}, pawn {snap.PawnLocation})");
        Expect(layer.Markers.DrawList.TextOf("ER/Laboratory/state") == s.SpyHud.ObjectiveHackingSpy, "marker shows Under hacking");
        Expect(layer.Markers.DrawList.Find("GPO/1/icon") is { } gpo && gpo.Colors[0].R > gpo.Colors[0].B, "GPO marker drawn orange");
        Expect(snap.Objectives.Exists(o => o.Name == "Laboratory" && o.Percent == 60), "terminal capture 60 %");
        // Console line (TeamMessage) and the damage wash (ClientDamageHudEffect: red 120 for a spy).
        ExpectItemText(layer.Items, "Console/0", "Ghost: on my way");
        Expect(layer.SfxDrawList.Items.Find(i => i.Path.StartsWith("Sfx/", StringComparison.Ordinal)) is { } wash &&
               Math.Abs(wash.Colors[0].R - HudSfx.Red.R) < 0.01f && Math.Abs(wash.Colors[0].A - 120f / 255f) < 0.01f,
            $"red damage wash ({layer.SfxDrawList.Items.Count} sfx items)");
        Expect(layer.Items.Any(i => i.Path == "Fps"), "frame-rate readout drawn");
        CheckAssets(layer);
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"spy={list.Items.Count}"));
    }

    /// <summary>
    /// The spy page at other window sizes (1920 x 1080, 2560 x 1080, 1024 x 768): every text the page draws, measured
    /// with its font as the canvas lays it out, ends inside the screen (the timer sits at the right edge).
    /// </summary>
    private void CheckTextInsideScreens()
    {
        HudLayer layer = _layer!;
        Vector2? saved = layer.ScreenSize;
        foreach (Vector2 screen in new[] { new Vector2(1920f, 1080f), new Vector2(2560f, 1080f), new Vector2(1024f, 768f), Screen })
        {
            layer.ScreenSize = screen;
            Run(2);
            foreach (MagmaDrawItem item in layer.Spy!.DrawList.Items)
            {
                if (item.Kind != MagmaWidgetKind.Text || item.Text.Length == 0 || layer.Assets?.Font(item.Font) is not { } font)
                {
                    continue;
                }

                foreach ((string line, float _) in MagmaCanvas.Layout(font, item))
                {
                    float width = font.Measure(line, item.HeightPx, item.XScale, item.TrackingPx);
                    float x = item.Align switch
                    {
                        MagmaAlign.Center => item.Rect.Position.X + ((item.Rect.Size.X - width) * 0.5f),
                        MagmaAlign.Right => item.Rect.End.X - width,
                        _ => item.Rect.Position.X,
                    };
                    // The glyphs as the canvas draws them (DrawRun): each at its advance, its quad from XOff over W.
                    float k = item.HeightPx / font.Size * item.XScale;
                    float pen = x, right = x;
                    foreach (char ch in line)
                    {
                        if (font.TryGlyph(ch, out MagmaGlyph g) && g.W > 0)
                        {
                            right = MathF.Max(right, pen + ((g.XOff + g.W) * k));
                        }

                        pen += (font.AdvanceOf(ch) * k) + item.TrackingPx;
                    }

                    Expect(x >= -1f && right <= screen.X + 1f,
                        string.Create(CultureInfo.InvariantCulture, $"{item.Path} '{line}' drawn at x {x:F0} to {right:F0} inside a {screen.X:F0} x {screen.Y:F0} screen"));
                }
            }
        }

        // The timer (an AUTOSIZED text) sits under the objective counter's figure, not against the screen edge.
        layer.ScreenSize = Screen;
        Run(2);
        MagmaDrawList list = layer.Spy!.DrawList;
        if (list.Find("Chrono/Chrono") is { } chrono && list.Find("Objectif/Story/Objectif") is { } counter &&
            layer.Assets?.Font(chrono.Font) is { } chronoFont)
        {
            float width = chronoFont.Measure(chrono.Text, chrono.HeightPx, chrono.XScale, chrono.TrackingPx);
            float x = chrono.Align switch
            {
                MagmaAlign.Center => chrono.Rect.Position.X + ((chrono.Rect.Size.X - width) * 0.5f),
                MagmaAlign.Right => chrono.Rect.End.X - width,
                _ => chrono.Rect.Position.X,
            };
            float centre = x + (width * 0.5f);
            float counterCentre = counter.Rect.Position.X + (counter.Rect.Size.X * 0.5f);
            float s = Screen.Y / MagmaRenderer.PageHeight;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_HUD timer '{chrono.Text}' rect {chrono.Rect} centre {centre:F0}; counter '{counter.Text}' rect {counter.Rect} centre {counterCentre:F0}"));
            Expect(Math.Abs(centre - counterCentre) <= 30f * s,
                string.Create(CultureInfo.InvariantCulture, $"timer centred at x {centre:F0} under the objective counter at x {counterCentre:F0} (within {30f * s:F0})"));
        }
        else
        {
            Expect(false, "timer and objective counter drawn on the spy page");
        }

        layer.ScreenSize = saved;
        Run(2);
    }

    private void CheckInteractionList()
    {
        _extra = s =>
        {
            s.Interactions.Clear();
            s.Interactions.AddRange(["Use", "Heal Mate", "Exit"]);
            s.SelectedInteraction = 1;
            s.InteractionFocus = true;
            s.ShowInteractions = true;
        };
        Run(20);
        MagmaDrawList list = _layer!.Interaction!.DrawList;
        ExpectText(list, "ListChoix#0/Choice", "Use");
        ExpectText(list, "ListChoix#2/Choice", "Exit");
        Expect(list.Find("ListChoix#1/PCButtonChoice") is not null, "selected row arrow");
        Expect(list.Find("ListChoix#0/PCButtonChoice") is null, "unselected row has no arrow");
        Expect(list.Find("ListChoix#1/highlight") is { Blend: MagmaBlend.Dodge }, "selected row dodge highlight");
        Expect(list.Find("Fond/Element") is { Anchor: MagmaAnchor.Right }, "list frame right anchored");
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"list={list.Items.Count}"));
        _extra = null;
    }

    /// <summary>The chat line, the kill feed's turn, the Enhanced Reality switch.</summary>
    private void CheckSpyExtras()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        ChatInput chat = _chat!;
        chat.Open(OpenScct.Net.EChatScope.TeamSay);
        chat.Type('g');
        chat.Type('o');
        chat.Type('`');
        Run(1);
        ExpectItemText(layer.Items, "Chat/line", "> TeamSay go_");
        chat.Backspace();
        chat.Backspace();
        chat.Backspace();
        chat.Type('o');
        chat.Type('k');
        chat.Submit();
        Expect(!chat.IsOpen && chat.Sent.Count == 1 && chat.Sent[0] == (OpenScct.Net.EChatScope.TeamSay, "ok"), $"chat line sent ({chat.Sent.Count})");
        Run(1);
        Expect(!layer.Items.Any(i => i.Path.StartsWith("Chat/", StringComparison.Ordinal)), "chat line closed");

        // The queue's head has shown its 5 s: the kill line comes next.
        s.SpyHud.GameMessageEndTime = Now - 1f;
        Run(2);
        ExpectText(layer.Spy!.DrawList, "GameMessage/messageGame", "Shadownet killed Ghost");

        // F9: Enhanced Reality off, then on again.
        HudCommands.SwitchEnhancedReality(s.SpyPc, layer.Options);
        Run(2);
        Expect(s.SpyHud.EnhancedRealityLevel == 0 && layer.Markers.DrawList.Find("ER/Laboratory/icon") is null, "Enhanced Reality off hides the markers");
        ExpectContains(layer.Spy.DrawList, "SystemMessage/messageSystem", s.SpyPc.EROff);
        HudCommands.SwitchEnhancedReality(s.SpyPc, layer.Options);
        Run(2);
        Expect(s.SpyHud.EnhancedRealityLevel == 1 && layer.Markers.DrawList.Find("ER/Laboratory/icon") is not null, "Enhanced Reality back on");
    }

    // ---- merc page ------------------------------------------------------------------------------------------

    private void CheckMerc()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        HudSnapshot snap = layer.Snapshot;
        Expect(layer.ActivePage is MercHudPage, "merc page active");
        MagmaDrawList list = layer.Merc!.DrawList;
        Expect(!layer.Spy!.Root.Visible, "spy page hidden for the merc");
        ExpectText(list, "Ammo/TextAmmoStock", "17/92");
        ExpectText(list, "Extras/FireMode", s.Rifle.GetROFName());
        ExpectText(list, "Extras/Vision", "Motion tracking");
        ExpectText(list, "Chrono/Chrono", "8:44");
        ExpectText(list, "Objectif/Story/Objectif", "3");
        ExpectText(list, "ChronoObjectif/Chrono_Objectif", "0:07");
        ExpectText(list, "ReticuleMessage/messageReticule", "Reloading");
        ExpectContains(list, "SystemMessage/messageSystem", "Hacking in progress...");
        ExpectTop(list, "Vie/jaugeinterne", 230f + 137f - 1.00f * 133f);
        Expect(snap.FireMode == (int)SRateOfFireMode.ROF_Burst && snap.VisionMode == 37, $"merc fire mode {snap.FireMode} vision {snap.VisionMode}");
        Expect(layer.Merc.Radar?.Alarms == 1, $"merc radar alarms {layer.Merc.Radar?.Alarms}");
        Expect(list.Any("Radar/IconAlarm1"), "hacked terminal on the merc radar");
        Expect(list.Any("Reticule/Reticule/FN2000/"), "rifle reticle drawn");
        Expect(!list.Any("Reticule/Reticule/UZI/"), "other reticles hidden");
        Expect(list.Any("Reticule/arrowSoundLeft/") && !list.Any("Reticule/arrowSoundRight/"), "left sound arrow only");
        Expect(s.MercHud.LeftArrowTime > Now && s.MercHud.LeftArrowInt > 0.6f && s.MercHud.TopArrowTime == 0f,
            $"noise to the left: left {s.MercHud.LeftArrowTime} ({s.MercHud.LeftArrowInt}) top {s.MercHud.TopArrowTime}");
        Expect(list.Any("AlertMessage/"), "alert box with an objective countdown");
        Expect(list.Any("Pos_Merc/Element/"), "posture icon drawn");
        Expect(list.Find("Ammo/TextAmmoStock")?.Anchor == MagmaAnchor.Left, "ammo left anchored");
        // Widescreen off: the whole page is stretched as in the unpatched game, then back.
        layer.Options.StretchHud = true;
        Run(1);
        Expect(layer.Merc.DrawList.Find("Ammo/TextAmmoStock")?.Anchor == MagmaAnchor.Stretch, "widescreen off stretches the page");
        layer.Options.StretchHud = false;
        Run(1);
        Expect(layer.Merc.DrawList.Find("Ammo/TextAmmoStock")?.Anchor == MagmaAnchor.Left, "widescreen on anchors again");
        Expect(layer.Markers.DrawList.Items.Count == 0, "no objective markers for mercs");
        Expect(layer.Interaction!.DrawList.Items.Count == 0, "no interaction list for mercs");
        Expect(!layer.Merc.Root.Children.Any(c => c.Name == "Flash" && c.Visible), "no white-out in motion vision (immune)");
        CheckAssets(layer);
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"merc={list.Items.Count}"));
    }

    /// <summary>The merc page's effects, one after another.</summary>
    private void CheckMercEffects()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        MercHudPage merc = layer.Merc!;

        s.MercPawn.bStickyShocking = true;
        s.MercPawn.bStickyShockedInTheHead = true;
        Run(3);
        Expect(merc.DrawList.Any("A_StickyShock/") && !merc.DrawList.Any("A_StickyShock2/"), "head sticky shock drawn");
        s.MercPawn.bStickyShocking = false;
        Run(1);
        Expect(!merc.DrawList.Any("A_StickyShock"), "sticky shock gone");

        s.MercPawn.CurrentRainVolume = s.Mission;
        Run(3);
        Expect(merc.Area("gouttes/Gouttes") is { Visible: true }, "droplets while wet");
        s.MercPawn.CurrentRainVolume = null;

        s.MercPc.RendMap = 0;
        layer.Effects.ClientDamageHudEffect(s.MercPc, (int)SDamageType.SDTFlash, Now);
        Run(2);
        Expect(merc.DrawList.Any("Flash/Flash/"), "flash-bang white-out drawn");
        s.MercPc.RendMap = 37;

        layer.Effects.SetHudProjection(1, 5, 10, Now);
        Run(2);
        Expect(merc.DrawList.Any("Mousse/Element/"), "extinguisher foam drawn");
        layer.Effects.Reset();
        Run(2);
        Expect(!merc.DrawList.Any("Mousse/"), "foam gone");

        s.MercHud.bPerturbation = true;
        s.MercHud.TimetobeginPerturb = Now;
        s.MercHud.HudPerturbationLength = 100f;
        Run(2);
        Expect(merc.Perturbed && merc.Area("Vie") is { Frame: >= MercHudPage.PerturbedFrame }, $"perturbed areas at frame 60+ ({merc.Area("Vie")?.Frame})");
        s.MercHud.bPerturbation = false;
        Run(1);
        Expect(!merc.Perturbed && merc.Area("Vie") is { Frame: 0 }, $"perturbation over ({merc.Area("Vie")?.Frame})");

        layer.Effects.Post(new SimEvent { Kind = SimEventKind.Notify, ActorId = s.MercPc.ActorId, What = OpenScct.Game.Combat.CombatFx.HudShutDown, Location = new FVector(1f, 9f, 0f) });
        Run(2);
        Expect(merc.ShuttingDown && merc.Area("Vie") is { Frame: >= MercHudPage.ShutDownFrame }, "HUD shutdown runs");
        ExpectContains(merc.DrawList, "SystemMessage/messageSystem", s.MercHud.TextMalfunction);
        _notes.Add("effects=7");
    }

    /// <summary>Counter variants and the canvas extras, through the reader and the snapshot.</summary>
    // ---- binoculars -----------------------------------------------------------------------------------------

    private Name _spyState;
    private Name _mercState;

    /// <summary>The spy's binocular page (spec/hud/binoculars.md rules 2 to 7) and its objective markers (12 to 17).</summary>
    private void CheckSpyBinoculars()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        _viewer = s.SpyPc;

        // The drop zone above the reticle gets the lock panel, the hacked terminal an icon, the generator an arrow.
        s.Terminal.bActive = true;
        s.Generator.bActive = true;

        _spyState = s.SpyPc.GetStateName();
        s.SpyPc.bBinocularVision = true;
        s.SpyPc.GotoState(new Name("s_Binoculars_Att"));
        Expect(s.SpyPc.IsInState(new Name("s_Binoculars_Att")), $"spy in s_Binoculars_Att ({s.SpyPc.GetStateName()})");
        s.SpyPc.VoiceChatTarget = s.MercPawn;
        Run(3);
        Expect(layer.ActivePage is SpyBinocularsPage, $"spy binocular page active ({layer.ActivePage?.GetType().Name})");
        Expect(!layer.Spy!.Root.Visible, "spy page hidden in the binoculars");
        SpyBinocularsPage page = layer.Binoculars!.Spy!;
        MagmaDrawList list = page.DrawList;
        ExpectText(list, "ZoomAtt/ZoomValue", "x2");
        ExpectLeft(list, "ZoomAtt/zoomInside", 317f - SpyBinocularsPage.ZoomMarkerStep);
        ExpectText(list, "ZoomAtt/PCtextZoomAtt1Binding", "Mouse Wheel Up / Mouse Wheel Down");
        ExpectText(list, "ZoomAtt/PCtextZoomAtt2Binding", "B");
        Expect(list.Find("ZoomAtt/textZoomAtt1") is null && list.Find("ZoomAtt/IconZoomUp") is null && list.Find("ZoomAtt/PCtextZoomAtt3") is null,
            "console-only binocular texts hidden");
        Expect(list.Any("frame/") && list.Any("SoundAnimation/"), "binocular frame and sound meter drawn");
        ExpectText(list, "Speak/_TextSpeakATT", "Hack");
        Expect(page.Markers.Candidate == "Drop zone", $"lock panel on the drop zone ({page.Markers.Candidate ?? "none"})");
        Expect(page.Markers.Paths().Count(p => p.EndsWith("/panel", StringComparison.Ordinal)) == 1, "one lock panel");
        Expect(page.Markers.Paths().Any(p => p.EndsWith("/icon", StringComparison.Ordinal)), "terminal marker icon in the binoculars");
        Expect(page.Markers.Paths().Any(p => p.EndsWith("/arrow", StringComparison.Ordinal)), "off-screen arrow in the binoculars");
        ExpectItemText(layer.Items, "BinER/" + (1 + MarkerIndex(page, "Drop zone")).ToString(CultureInfo.InvariantCulture) + "/hint",
            s.SpyHud.BinocularsAttLock.Replace(HudBinocularMarkers.UseToken, layer.Assets!.KeyDisplayName("Shift"), StringComparison.Ordinal));
        Expect(layer.Markers.DrawList.Items.Count == 0, "the normal ER markers are not drawn in the binoculars");

        // Zoom to x6: the label and the marker move right.
        s.SpyPc.fBinocularVisionFOV = 10f;
        Run(2);
        ExpectText(list, "ZoomAtt/ZoomValue", "x6");
        ExpectLeft(list, "ZoomAtt/zoomInside", 317f + SpyBinocularsPage.ZoomMarkerStep);
        s.SpyPc.VoiceChatTarget = null;
        Run(1);
        Expect(list.Find("Speak/_TextSpeakATT") is null, "Hack prompt hidden without a voice target");
        CheckAssets(layer);
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"bino-spy={list.Items.Count}"));
    }

    /// <summary>Index of the binocular target whose lock panel shows <paramref name="name"/> (the drop zone).</summary>
    private int MarkerIndex(SpyBinocularsPage page, string name)
    {
        List<HudBinocularTarget> targets = _layer!.Snapshot.Binoculars.Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].Kind == HudObjectiveKind.DropZone)
            {
                return i;
            }
        }

        _ = page;
        _failures.Add($"no binocular target for {name}");
        return 0;
    }

    /// <summary>The merc's scope page (spec/hud/binoculars.md rules 2, 3 and 8 to 11).</summary>
    private void CheckMercBinoculars()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        _spyState = _spyState == default ? new Name("PlayerWalking") : _spyState;
        s.SpyPc.GotoState(_spyState);
        _viewer = s.MercPc;
        _mercState = s.MercPc.GetStateName();
        s.MercPc.bBinocularVision = true;
        s.MercPc.GotoState(new Name("s_Binoculars_Def"));
        Expect(s.MercPc.IsInState(new Name("s_Binoculars_Def")), $"merc in s_Binoculars_Def ({s.MercPc.GetStateName()})");
        s.MercPc.fBinocularVisionFOV = 30f;
        s.MercPc.BinocularHoldBreathTime = 2.5f;
        s.MercPc.bHoldBreath = 0;
        s.MercPc.fDateLastShootedPawn = Now;
        HudMessages.AddReticuleMessage(s.MercHud, "Reloading", 30, 0);
        Run(2);
        Expect(layer.ActivePage is MercBinocularsPage, $"merc binocular page active ({layer.ActivePage?.GetType().Name})");
        MercBinocularsPage page = layer.Binoculars!.Merc!;
        MagmaDrawList list = page.DrawList;
        ExpectText(list, "ZoomDef/ZoomValue", "x2");
        int frame = page.Area("frame")?.Frame ?? -1;
        Expect(frame is >= MercBinocularsPage.ZoomOutFrame and < MercBinocularsPage.ZoomOutFrame + 10,
            $"scope rings play the zoom-out keys on entry (frame {frame})");
        Expect(page.BreathFrame == 10, $"breath gauge frame 10 at 2.5 s left ({page.BreathFrame})");
        Expect(list.Find("ZoomDef/sights") is { } red && red.Colors.Length == 4 && red.Colors[0] == MercBinocularsPage.SightsHitColour,
            "sights red after a hit");
        ExpectText(list, "Reload/messageReticule", "Reloading");
        string? distance = list.TextOf("ZoomDef/distance");
        Expect(distance is not null && System.Text.RegularExpressions.Regex.IsMatch(distance, @"^\d{2,}\.\d{2,}$"), $"range finder text ({distance ?? "(not drawn)"})");
        Expect(MercBinocularsPage.DistanceText(1237f, 0.01f) == "12.37" && MercBinocularsPage.DistanceText(0f, 0.01f) == "00.00",
            "range finder format");
        Expect(!string.IsNullOrEmpty(list.TextOf("ZoomDef/PCtextZoomDef3Binding")), "hold breath key named");
        Expect(list.Find("ZoomDef/PCtextZoomDef4") is null && list.Find("ZoomDef/textZoomDef3") is null, "unused scope texts hidden");

        // Zoom in to x4: the rings play the zoom-in keys; holding the breath centres the sights; the hit fades.
        s.MercPc.fBinocularVisionFOV = 20f;
        s.MercPc.bHoldBreath = 1;
        s.MercPc.fDateLastShootedPawn = Now - 1f;
        Run(2);
        ExpectText(list, "ZoomDef/ZoomValue", "x4");
        frame = page.Area("frame")?.Frame ?? -1;
        Expect(frame is >= MercBinocularsPage.ZoomInFrame and < MercBinocularsPage.ZoomInFrame + 10, $"scope rings play the zoom-in keys (frame {frame})");
        ExpectLeft(list, "ZoomDef/sights", 289f);
        ExpectTop(list, "ZoomDef/sights", 223f);
        Expect(list.Find("ZoomDef/sights") is { } orange && orange.Colors[0] == MercBinocularsPage.SightsColour, "sights orange again");
        Expect(!layer.Merc!.Root.Visible, "merc page hidden in the scope");
        CheckAssets(layer);
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"bino-merc={list.Items.Count}"));
    }

    private void LeaveBinoculars()
    {
        Scene s = _scene!;
        s.MercPc.bBinocularVision = false;
        s.MercPc.GotoState(_mercState == default ? new Name("PlayerWalking") : _mercState);
        s.MercPc.bHoldBreath = 0;
        _viewer = s.SpyPc;
        Run(1);
        Expect(_layer!.ActivePage is SpyHudPage, $"spy page back after the binoculars ({_layer.ActivePage?.GetType().Name})");
    }

    private void ExpectLeft(MagmaDrawList list, string path, float left)
    {
        if (list.Find(path) is not { } item || Math.Abs(item.PageRect.L - left) > 0.01f)
        {
            _failures.Add($"{path} left = {(list.Find(path) is { } i ? i.PageRect.L.ToString(CultureInfo.InvariantCulture) : "(not drawn)")}, expected {left}");
        }
    }

    private void CheckCounters()
    {
        HudLayer layer = _layer!;
        _viewer = _scene!.SpyPc;
        GameReplicationInfo gri = _scene.World.Level.Info.GRI!;
        gri.GameMode = EGameMode.GM_DeathMatch;
        gri.TotalTeamLives[0] = 7;
        gri.TotalTeamLives[1] = 1000;
        _extra = s => s.RespawnWeapon = new HudRespawnWeapon(64, 0, "Use");
        Run(2);
        ExpectText(layer.Spy!.DrawList, "Objectif/DeathMatch/Objectif", "7/Inf.");
        Expect(!layer.Spy.DrawList.Any("Objectif/Story/"), "story counter hidden in a deathmatch");
        Expect(layer.Items.Any(i => i.Path == "Respawn/icon" && i.Texture == HudRespawnWeaponDrawer.Atlas), "respawn weapon icon drawn");
        ExpectItemText(layer.Items, "Respawn/key", "Use");
        gri.GameMode = EGameMode.GM_Multi;
        _extra = null;
        Run(1);
    }

    /// <summary>The spectator page: the spy watches after his death (BaseSpectating counts as spectating).</summary>
    private void CheckSpectator()
    {
        HudLayer layer = _layer!;
        Scene s = _scene!;
        _viewer = s.SpyPc;
        s.SpyPc.GotoState(new Name("BaseSpectating"));
        if (!HudK.IsSpectating(s.SpyPc))
        {
            _notes.Add("spectating=forced");
            _extra = snap => snap.Spectator.Active = true;
        }

        s.SpyPc.SpecTarget = (SBasePawn)s.MatePawn;
        s.SpyPc.fRespawnDate = Now + 6.5f;
        Run(5);
        Expect(layer.ActivePage is SpectatorHudPage, $"spectator page active ({layer.ActivePage?.GetType().Name})");
        MagmaDrawList list = layer.Spectator!.DrawList;
        ExpectText(list, "Spectator/textNbObjectives", "1/4");
        ExpectText(list, "Spectator/textNbRemainingTime", "8:44");
        ExpectText(list, "Spectator/textNbDeaths", "4");
        ExpectText(list, "Spectator/textPlayerWatched", "Ghost");
        ExpectText(list, "Spectator/textRespawnTime", "7");
        ExpectContains(list, "Spectator/textTip", "spy tip");
        Expect(list.Find("Spectator/textNoRespawnLeft") is null, "respawn left");
        Expect(!layer.Spy!.Root.Visible && !layer.Merc!.Root.Visible, "player pages hidden while spectating");
        _extra = null;
    }

    private GameStatusPage? _status;

    private void CheckStatusPage(SmokeContext context)
    {
        _status = new GameStatusPage { Source = _layer!.Snapshot };
        context.Tree.Root.AddChild(_status);
        _status.Open();
        _status.Refresh();
        List<string> spies = _status.Names(0), mercs = _status.Names(1);
        Expect(spies.Contains("Sam") && spies.Contains("Ghost") && mercs.SequenceEqual(["Shadownet"]),
            $"mission status lists ({string.Join(",", spies)} / {string.Join(",", mercs)})");
        Expect(_status.ObjectiveList?.Text.Contains("- Neutralize the laboratory", StringComparison.Ordinal) == true,
            $"objective list ({_status.ObjectiveList?.Text})");
    }

    private void FreeStatusPage(SmokeContext context)
    {
        _status?.Close();
        _status?.QueueFree();
        _status = null;
        _chat?.QueueFree();
        _ = context;
    }

    /// <summary>Every drawn image has its texture and every text its font; the HUD's own texts sit on whole pixels.</summary>
    private void CheckAssets(HudLayer layer)
    {
        if (layer.Assets!.Font(HudAssets.DefaultFont) is not { Padding: >= 1 })
        {
            _failures.Add("the HUD font pages are not padded (python -m svmbuild ui)");
        }

        foreach (MagmaDrawItem item in layer.Items)
        {
            if (item.Kind == MagmaWidgetKind.Image && !layer.Assets!.HasTexture(item.Package, item.Texture))
            {
                _failures.Add($"{item.Path}: texture {item.Package}/{item.Texture} missing");
                return;
            }

            if (item.Kind == MagmaWidgetKind.Text && layer.Assets!.Font(item.Font) is null)
            {
                _failures.Add($"{item.Path}: font {item.Font} missing");
                return;
            }

            bool own = item.Path.StartsWith("Console/", StringComparison.Ordinal) || item.Path.StartsWith("Chat/", StringComparison.Ordinal) ||
                item.Path.StartsWith("Extras/", StringComparison.Ordinal) || item.Path == "Fps" || item.Path.StartsWith("Respawn/", StringComparison.Ordinal);
            if (own && item.Kind == MagmaWidgetKind.Text && item.HeightPx != MathF.Round(item.HeightPx))
            {
                _failures.Add($"{item.Path}: text height {item.HeightPx} is not a whole pixel");
                return;
            }
        }
    }

    // ---- front end ------------------------------------------------------------------------------------------

    private void CheckMainMenu(SceneTree tree)
    {
        if (tree.CurrentScene is not MainMenu menu)
        {
            _failures.Add($"main menu is not the current scene ({tree.CurrentScene?.Name})");
            return;
        }

        Expect(menu.HostButton is not null &&
            menu.JoinButton is not null && menu.OptionsButton is not null && menu.QuitButton is not null, "main menu buttons");
    }

    private void CheckLobby(SceneTree tree)
    {
        if (tree.CurrentScene is not LobbyScreen lobby || lobby.Session is not LocalLobby session)
        {
            _failures.Add("lobby is not the current scene");
            return;
        }

        Expect(session.Players.Count == 4 && session.Players.Count(p => p.Side == HudSide.Spy) == 2, $"lobby 2v2 ({session.Players.Count})");
        Expect(!session.CanStart && lobby.StartButton is { Disabled: true }, "start waits for ready");
        session.SetReady(true);
        Expect(session.CanStart && lobby.StartButton is { Disabled: false }, "start after ready");
        session.SetSide(HudSide.Spy);
        Expect(session.Local.Side == HudSide.Spy && !session.Local.Ready, "side change clears ready");
    }

    private void CheckOptions(SceneTree tree)
    {
        if (tree.CurrentScene is not OptionsMenu options)
        {
            _failures.Add($"options is not the current scene ({tree.CurrentScene?.Name})");
            return;
        }

        Expect(options.Tabs?.GetTabCount() == 7, $"option pages {options.Tabs?.GetTabCount()}");
        Expect(options.KeyButtons.Count > 0, "key bindings listed");
        foreach (string key in new[] { "mouse_sensitivity", "invert_y", "controls_spy_kbm.free_look", "controls_merc_kbm.look", "controller.enabled",
                     "controller.deadzone_left", "display_mode", "resolution", "vsync", "widescreen", "ws_fov", "fps_spy", "fps_merc", "master_volume",
                     "sound_volume", "music_volume", "voice_output_volume", "show_scoreboard", "show_ping" })
        {
            Expect(options.Controls.ContainsKey(key), "option " + key);
        }

        Expect(options.Controls.TryGetValue("ws_fov", out Control? fov) && fov is HSlider { MinValue: 95, MaxValue: 112 }, "ws_fov slider 95..112");
        Expect(options.Keys?.Rows().Any(r => r.Command.Contains("Talk", StringComparison.OrdinalIgnoreCase)) == true, "chat keys in the layout");
        // The settings file round trip (in memory: a smoke run never writes the player's settings).
        GameSettings edited = options.Editing.Clone();
        edited.MercFovCap = 500f;
        edited.FpsSpy = 10;
        edited.SoundVolume = 0.25f;
        GameSettings back = GameSettings.FromJson(edited.ToJson());
        Expect(back.MercFovCap == GameSettings.MaxMercFov && back.FpsSpy == GameSettings.MinFps && back.SoundVolume == 0.25f,
            $"settings round trip and clamps ({back.MercFovCap}, {back.FpsSpy}, {back.SoundVolume})");
        _notes.Add(string.Create(CultureInfo.InvariantCulture, $"keys={options.KeyButtons.Count}"));
    }

    private static MatchResult SampleResult() => new()
    {
        Map = "SteeE",
        Winner = HudSide.Spy,
        Reason = MatchEndReason.Story,
        LocalSide = HudSide.Spy,
        ObjectivesTaken = 4,
        ObjectivesTotal = 4,
        Rows =
        [
            new MatchResultRow("Sam", HudSide.Spy, 120, 1, 2, 3, true),
            new MatchResultRow("Shadownet", HudSide.Merc, 80, 3, 1, 0, false),
        ],
        Request = new PlayRequest(),
    };

    private void CheckResults()
    {
        ResultsScreen? results = _results;
        if (results is null || !results.IsInsideTree())
        {
            _failures.Add("results screen not shown");
            return;
        }

        Expect(results.Headline?.Text == "SPIES WIN", $"results headline {results.Headline?.Text}");
        Expect(results.Table?.GetChildCount() == 6 * 3, $"results table cells {results.Table?.GetChildCount()}");
        Expect(!results.BannerShowing && results.Table is { Visible: true }, "table shown after the banner");
    }

    private void CheckBanner()
    {
        if (_results is null)
        {
            _failures.Add("results screen not shown");
            return;
        }

        Expect(_results.BannerShowing && _results.Banner?.DrawList.TextOf("YouWin/Text1") == "You Win", "You Win banner drawn");
    }

    private static string? CaptureDir()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--svm-hud-capture=", StringComparison.Ordinal))
            {
                return arg["--svm-hud-capture=".Length..];
            }
        }

        return null;
    }

    /// <summary>Saves the frame drawn last as &lt;dir&gt;/hud-&lt;name&gt;.png (renderer runs only).</summary>
    private void Capture(SmokeContext context, string name)
    {
        if (_captureDir is null || DisplayServer.GetName() == "headless")
        {
            return;
        }

        Image? image = context.Tree.Root.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
        {
            return;
        }

        System.IO.Directory.CreateDirectory(_captureDir);
        image.SavePng(System.IO.Path.Combine(_captureDir, "hud-" + name + ".png"));
    }

    // ---- checks ---------------------------------------------------------------------------------------------

    private void Expect(bool condition, string what)
    {
        if (!condition)
        {
            _failures.Add(what);
        }
    }

    private void ExpectText(MagmaDrawList list, string path, string expected)
    {
        string? text = list.TextOf(path);
        if (text != expected)
        {
            _failures.Add($"{path} = '{text ?? "(not drawn)"}', expected '{expected}'");
        }
    }

    private void ExpectItemText(IReadOnlyList<MagmaDrawItem> items, string path, string expected)
    {
        string? text = null;
        foreach (MagmaDrawItem i in items)
        {
            if (i.Path == path && i.Kind == MagmaWidgetKind.Text)
            {
                text = i.Text;
                break;
            }
        }

        if (text != expected)
        {
            _failures.Add($"{path} = '{text ?? "(not drawn)"}', expected '{expected}'");
        }
    }

    private void ExpectContains(MagmaDrawList list, string path, string expected)
    {
        string? text = list.TextOf(path);
        if (text is null || !text.Contains(expected, StringComparison.Ordinal))
        {
            _failures.Add($"{path} = '{text ?? "(not drawn)"}', expected it to contain '{expected}'");
        }
    }

    private void ExpectTop(MagmaDrawList list, string path, float top)
    {
        if (list.Find(path) is not { } item || Math.Abs(item.PageRect.T - top) > 0.01f)
        {
            _failures.Add(string.Create(CultureInfo.InvariantCulture, $"{path} top {list.Find(path)?.PageRect.T}, expected {top}"));
        }
    }

    /// <summary>The sim source plus test additions.</summary>
    private sealed class Composite(IHudSource inner, Action<HudSnapshot> after) : IHudSource
    {
        public void Fill(HudSnapshot snapshot)
        {
            inner.Fill(snapshot);
            after(snapshot);
        }
    }

    /// <summary>The offline scene: SBase actors set up the way a match would leave them.</summary>
    private sealed class Scene
    {
        public Scene()
        {
            World = new World(1, ENetMode.NM_Standalone);
            Level level = World.Level;
            var gri = new GameReplicationInfo { TimeLimit = 10, ElapsedTime = 75, NumObjectives = 4, GameMode = EGameMode.GM_Multi };
            var spies = new STeamInfo { TeamIndex = 0, iNBGadgets = 4 };
            spies.aTipStrings.Add("A spy tip.");
            gri.Teams[0] = spies;
            gri.Teams[1] = new STeamInfo { TeamIndex = 1, iNBGadgets = 4 };
            level.Info.GRI = gri;

            level.BeginLoad();
            Mission = Spawn(level, new SMission { GameMode = EGameMode.GM_Multi, TotalObjectivesLeft = 3 }, default);
            Terminal = Spawn(level, new SComputerObjectiveTrigger { TriggerName = "Laboratory", DominationCaptureTime = 10 },
                new FVector(800f, 0f, 0f));
            Objective = Spawn(level, new SObjective { ObjectiveName = "Laboratory", Description = "Neutralize the laboratory" }, default);
            Objective.Triggers.Add(Terminal);
            Terminal.Objective = Objective;
            Mission.Objectives.Add(Objective);
            Alarm = Spawn(level, new SAlarm { MessagePriority = 5, Duration = 30, StartTime = 70 }, default);

            // For the binoculars: a drop zone just above the reticle (the lock panel) and a terminal far to the right
            // (an arrow on the screen edge).
            DropZone = Spawn(level, new SFlagDropZone(), new FVector(1000f, 0f, 70f));
            Generator = Spawn(level, new SComputerObjectiveTrigger { TriggerName = "Generator" }, new FVector(500f, 2000f, 0f));
            level.EndLoad();

            // A hack 6 s into the 10 s capture, counting down to ElapsedTime 82.
            Mission.GameMode = EGameMode.GM_Multi;
            Mission.TotalObjectivesLeft = 3;
            Terminal.CaptureTime = 4;
            Terminal.bTriggering = true;
            Objective.MinTriggerTime = 82f;
            Objective.ObjectiveTaken = false;   // a freshly placed objective counts as taken until its mission resets it

            (SpyPc, SpyPawn) = Player(level, gri, spy: true, new FVector(0f, 0f, 0f), "Sam", 4, 48);
            (SPlayerController mate, MatePawn) = Player(level, gri, spy: true, new FVector(-200f, 100f, 0f), "Ghost", 4, 60);
            (MercPc, MercPawn) = Player(level, gri, spy: false, new FVector(400f, 0f, 0f), "Shadownet", 3, 70);
            ((SBasePawn)MatePawn).bReadyForShortLadder = true;

            var spy = (SPawnAttaque)SpyPawn;
            spy.Health = 60;
            spy.EnergyLevelMax = 100;
            spy.EnergyLevel = 50f;
            SpyPc.bFastSpeed = false;
            Gadget = Give(level, new SGadStickyCam(), SpyPawn);
            Gadget.Charge = 3;
            SpyPawn.SelectedGadget = Gadget;
            SpyHud = Hud(level, SpyPc);
            SpyHud.bShowConnectedPlayers = true;
            var hack = Spawn(level, new STriggerInteraction { MinTriggerTime = 80f }, default);
            ObjectiveHud.AddRemoveActorOnHud(SpyHud, Objective, true);
            ObjectiveHud.AddRemoveActorOnHud(SpyHud, hack, true);
            SpyHud.AlarmsList.Add(Alarm);
            SpyHud.MalfunctionGamePlayObjects.Add(new FVector(600f, 120f, 40f));
            HudMessages.LocalizedMessage(SpyHud, K.MsgNeutralizationInProgress, HudMessages.GetString(K.MsgNeutralizationInProgress, Terminal));
            HudMessages.LocalizedMessage(SpyHud, K.MsgMissionSpy, HudMessages.GetString(K.MsgMissionSpy, Objective));
            HudMessages.ReceiveLocalizedMessage(SpyPc, HudMessages.DeathMessageClass, 0, MercPc.PlayerReplicationInfo, mate.PlayerReplicationInfo, null);

            MercPawn.Health = 100;
            Rifle = Give(level, new SWeaponDefense(), MercPawn);
            Rifle.GiveTo(MercPawn);
            Rifle.ClipAmmo = 17;
            Rifle.Ammo = 92;
            Rifle.ROFMode = SRateOfFireMode.ROF_Burst;
            MercPawn.WeaponLoaded = 1;
            MercPawn.bIsCrouched = true;
            MercPc.RendMap = 37;
            MercPc.Rotation = new FRotator(0, 32768, 0);   // facing the terminal behind him: it is on the radar's lower half
            MercHud = Hud(level, MercPc);
            ObjectiveHud.AddObjective(MercHud, Terminal);
            ObjectiveHud.AddRemoveActorOnHud(MercHud, Objective, true);
            HudMessages.ReceiveLocalizedMessage(MercPc, K.MsgHackingInProgress, null);
            HudMessages.AddReticuleMessage(MercHud, "Reloading", 30, 0);
            // A noise 600 uu to the merc's left lights the left arrow (SPlayerController.ClientNotifyNoise, the
            // SHud.NotifyHotSound port); the merc's own weapon dummy gives none.
            int left = MercPawn.Rotation.Yaw - 16384;
            float a = left * MathF.PI / 32768f;
            Noise = Spawn(level, new SNotifyNoise(), MercPawn.Location + new FVector(MathF.Cos(a) * 600f, MathF.Sin(a) * 600f, 0f));
            MercPc.ClientNotifyNoise(Noise, 1f);
            MercPc.ClientNotifyNoise(Spawn(level, new SWeaponDefenseDummy(), MercPawn.Location + new FVector(0f, 0f, 600f)), 1f);
        }

        public World World { get; }

        public SPlayerController SpyPc { get; }

        public SBasePawn SpyPawn { get; }

        public Pawn MatePawn { get; }

        public SPlayerController MercPc { get; }

        public SBasePawn MercPawn { get; }

        public SHud SpyHud { get; }

        public SHud MercHud { get; }

        public SNotifyNoise Noise { get; }

        public SMission Mission { get; }

        public SObjective Objective { get; }

        public SComputerObjectiveTrigger Terminal { get; }

        public SAlarm Alarm { get; }

        public SFlagDropZone DropZone { get; }

        public SComputerObjectiveTrigger Generator { get; }

        public SGadget Gadget { get; }

        public SWeaponDefense Rifle { get; }

        /// <summary>A camera at the spy's eyes looking down +X with a 90 degree field of view.</summary>
        public Vector2? Project(FVector p)
        {
            FVector r = p - (SpyPawn.Location + new FVector(0f, 0f, 60f));
            if (r.X <= 1f)
            {
                return null;
            }

            float f = Screen.X / 2f;
            return new Vector2(Screen.X / 2f + r.Y / r.X * f, Screen.Y / 2f - r.Z / r.X * f);
        }

        private static T Spawn<T>(Level level, T actor, FVector at)
            where T : Actor => level.SpawnActor(actor, at, default) ?? throw new InvalidOperationException($"{actor} did not spawn");

        private static T Give<T>(Level level, T actor, Pawn owner)
            where T : Actor
        {
            T spawned = Spawn(level, actor, owner.Location);
            spawned.Instigator = owner;
            spawned.SetOwner(owner);
            return spawned;
        }

        private static SHud Hud(Level level, SPlayerController pc)
        {
            SHud hud = Spawn(level, new SHud(), default);
            hud.PlayerOwner = pc;
            pc.myHUD = hud;
            return hud;
        }

        private static (SPlayerController, SBasePawn) Player(Level level, GameReplicationInfo gri, bool spy, FVector at, string name, int lives, int ping)
        {
            SBasePawn pawn = level.SpawnActor<SBasePawn>(spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh(), at, default)
                ?? throw new InvalidOperationException("pawn did not spawn");
            SPlayerController pc = Spawn(level, new SPlayerController(), at);
            pc.Possess(pawn);
            pawn.Health = 100;
            pc.SPawnType = pawn.SPawnType;
            pc.PlayerReplicationInfo ??= new PlayerReplicationInfo();
            pc.PlayerReplicationInfo.PlayerName = name;
            pc.PlayerReplicationInfo.NumLives = lives;
            pc.PlayerReplicationInfo.Ping = ping;
            pc.PlayerReplicationInfo.Team = gri.Teams[spy ? 0 : 1];
            pawn.PlayerReplicationInfo = pc.PlayerReplicationInfo;
            pc.GameReplicationInfo = gri;
            if (!gri.PRIArray.Contains(pc.PlayerReplicationInfo))
            {
                gri.PRIArray.Add(pc.PlayerReplicationInfo);
            }

            return (pc, pawn);
        }
    }
}
