using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Native chat input, scroll/fade behaviour and real HUD-page captures at multiple resolutions.</summary>
public sealed class ModernChatSmoke : SmokeTest
{
    public const string TestName = "modern-chat";
    public override string Name => TestName;
    private static readonly Vector2I[] Sizes = [new(1280, 720), new(1920, 1080), new(3840, 2160), new(1024, 768), new(3440, 1440)];
    private readonly List<string> _failures = [];
    private readonly Source _source = new();
    private readonly LocalInput _local = new();
    private SubViewport? _viewport;
    private ColorRect? _background;
    private ChatInput? _chat;
    private PlayInput? _input;
    private HudLayer? _hud;
    private int _phase, _wait, _case = -1, _captures, _scrollBefore;
    private double _now = 100;
    private const string AdminText = "[b]Admin notice[/b]: Maintenance begins in five minutes. Finish the current round, then reconnect when this notice clears. Thank you for playing on this server.";
    private long _adminEntryId;
    private bool _savedDetached;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_wait-- > 0) return SmokeResult.Running;
            if (_case >= 0) return VisualCase();
            switch (_phase++)
            {
                case 0:
                    _savedDetached = Pointer.Detached;
                    Pointer.Detached = true;
                    _viewport = new SubViewport { Size = Sizes[0], Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                    context.Tree.Root.AddChild(_viewport);
                    _background = new ColorRect { Size = Sizes[0], Color = new Color(.055f, .07f, .065f), MouseFilter = Control.MouseFilterEnum.Ignore };
                    _viewport.AddChild(_background);
                    _hud = HudLayer.Create(out string? error);
                    if (_hud is null) return Finish(SmokeResult.Fail(error ?? "HUD content missing"));
                    _hud.Source = _source;
                    _viewport.AddChild(_hud);
                    _input = new PlayInput { Bindings = Bindings(), Target = _local, RequireCapture = false, CapturePointer = false };
                    _viewport.AddChild(_input);
                    _chat = new ChatInput { PlayInput = _input, Clock = () => _now };
                    _viewport.AddChild(_chat);
                    _wait = 4;
                    return SmokeResult.Running;
                case 1:
                    CheckEditor();
                    for (int i = 0; i < 60; i++) Add("History " + i + ": cover the entrance while I reach the terminal.");
                    _chat!.Open(EChatScope.Say);
                    _wait = 5;
                    return SmokeResult.Running;
                case 2:
                    Expect(_chat!.Panel.HistoryScroll.GetVScrollBar().MaxValue > _chat.Panel.HistoryScroll.Size.Y, "history is scrollable");
                    _chat.Panel.HistoryScroll.ScrollVertical = 120;
                    _wait = 3;
                    return SmokeResult.Running;
                case 3:
                    _scrollBefore = _chat!.Panel.HistoryScroll.ScrollVertical;
                    Add("New arrival while reading older messages");
                    _wait = 5;
                    return SmokeResult.Running;
                case 4:
                    Expect(Math.Abs(_chat!.Panel.HistoryScroll.ScrollVertical - _scrollBefore) <= 2, "new arrival preserves reading position");
                    Expect(_chat.Panel.NewMessagesButton.Visible, "scrolled reader sees New messages action");
                    for (int i = 0; i < 3; i++) Add("fill " + i);
                    _wait = 4;
                    return SmokeResult.Running;
                case 5:
                    _chat!.Panel.HistoryScroll.ScrollVertical = 250;
                    _wait = 3;
                    return SmokeResult.Running;
                case 6:
                    _scrollBefore = _chat!.Panel.HistoryScroll.ScrollVertical;
                    Add("Capacity eviction must preserve the reading anchor");
                    _wait = 5;
                    return SmokeResult.Running;
                case 7:
                    Expect(_chat!.Panel.HistoryScroll.ScrollVertical <= _scrollBefore && _chat.Panel.HistoryScroll.ScrollVertical > 0,
                        "capacity eviction keeps the reader near the same retained entry");
                    _chat.Panel.NewMessagesButton.EmitSignal(Button.SignalName.Pressed);
                    _wait = 4;
                    return SmokeResult.Running;
                case 8:
                    var bar = _chat!.Panel.HistoryScroll.GetVScrollBar();
                    Expect(Math.Abs(bar.Value - (bar.MaxValue - bar.Page)) <= 2, "New messages returns to latest");
                    Expect(!_chat.Panel.NewMessagesButton.Visible, "New messages clears at latest");
                    _chat.Close();
                    _wait = 3;
                    return SmokeResult.Running;
                case 9:
                    Expect(_chat!.Panel.PassiveLineCount is > 0 and <= 6, "passive display is bounded to six wrapped lines");
                    _now += 9;
                    _wait = 3;
                    return SmokeResult.Running;
                case 10:
                    Expect(_chat!.Panel.PassiveLineCount == 0, "expired passive messages disappear");
                    Expect(!_chat.Panel.GetNode<Control>("Recent").Visible, "empty passive backing disappears");
                    Expect(_chat.Transcript.Entries.Count == 64, "fading keeps full history");
                    ChatNotifications.ReceiveChat(_chat.Transcript, new ChatLine(EChatScope.Say, 0, "Server", ChatK.NoTeam, AdminText[..100]), _now);
                    _adminEntryId = _chat.Transcript.Entries[^1].Id;
                    _chat.Open(EChatScope.Say);
                    _wait = 4;
                    return SmokeResult.Running;
                case 11:
                    ChatNotifications.ReceiveNotice(_chat!.Transcript, "Server: " + AdminText, _now + .1);
                    _wait = 4;
                    return SmokeResult.Running;
                case 12:
                    Expect(_chat!.Transcript.Entries.Count == 64 && _chat.Transcript.Entries[^1].Id == _adminEntryId,
                        "legacy admin echo is upgraded in place without adding a duplicate");
                    Expect(_chat.Transcript.Entries[^1].Channel == ChatChannel.System && _chat.Transcript.Entries[^1].Text == AdminText,
                        "full admin announcement survives the player message limit");
                    Expect(Descendants(_chat.Panel.HistoryScroll).OfType<RichTextLabel>().Count(row => row.GetParsedText().Contains(AdminText, StringComparison.Ordinal)) == 1,
                        "native history refreshes the upgraded admin text once and keeps markup literal");
                    BeginVisualCase(0);
                    return SmokeResult.Running;
            }
            return Finish(SmokeResult.Fail("Unexpected smoke phase"));
        }
        catch (Exception exception) { return Finish(SmokeResult.Fail(exception.ToString())); }
    }

    private void CheckEditor()
    {
        ChatInput chat = _chat!;
        PlayInput input = _input!;
        Pointer.Capture();
        _local.Set("BT_Up", true);
        _local.SetPadStick(1, 1);
        Press(Key.Enter);
        Expect(chat.IsOpen && chat.Panel.Channel == ChatChannel.All, "unassigned Enter opens All");
        Expect(!input.Enabled && !Pointer.IsCaptured && _local.Get("BT_Up") == 0 && !_local.PadStick, "opening releases pointer and held keyboard/controller input");
        chat.Panel.Draft = "abc";
        chat.Panel.Entry.CaretColumn = 2;
        Press(Key.Left);
        Expect(chat.Panel.Draft == "abc" && chat.Panel.Entry.CaretColumn == 1, "Left moves caret without deleting");
        chat.Panel.Entry.Select(0, 2);
        Press(Key.X, 'x');
        Press(Key.Y, 'y');
        Expect(chat.Panel.Draft == "xyc", "native selection replacement");
        chat.Panel.Draft = "";
        chat.Panel.Entry.InsertTextAtCaret(new string('x', 120));
        Expect(chat.Panel.Draft.Length == 100, "native insertion/paste path respects body-only limit");
        chat.Panel.Draft = new string('x', 99) + "\U0001F600";
        Expect(chat.Panel.Draft.Length == 99 && !char.IsSurrogate(chat.Panel.Draft[^1]), "Unicode boundary preserves the relay budget without half an emoji");
        chat.Panel.Draft = "cover me";
        Press(Key.Tab);
        Expect(chat.Panel.Channel == ChatChannel.Team && chat.Panel.Draft == "cover me", "Tab preserves draft while changing channel");
        // Opening emits SetWalk/SetScope/SetSpyFirstPerson releases for the next simulation tick.
        // Consume those intentional releases before checking for new mouse-driven commands.
        _local.EndTick();
        _viewport!.PushInput(new InputEventMouseMotion { Relative = new Vector2(20, 10) });
        _viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(1000, 50) });
        input.FillTick(_local);
        Expect(_local.MouseX == 0 && _local.MouseY == 0 && _local.Commands.Count == 0, "mouse look and firing suppressed while typing");
        chat.Panel.Entry.GrabFocus();
        Press(Key.Enter);
        Expect(!chat.IsOpen && chat.Sent.Count == 1 && chat.Sent[0] == (EChatScope.TeamSay, "cover me"), "native Enter submits exactly once to selected channel");
        Expect(input.Enabled && Pointer.IsCaptured, "close restores gameplay and prior pointer capture");
        Expect(chat.Transcript.Entries.Count == 0, "submission does not add optimistic echo");
        Press(Key.F6);
        chat.Panel.Draft = "discard this";
        Press(Key.Escape);
        Expect(!chat.IsOpen && chat.Panel.Draft.Length == 0 && chat.Sent.Count == 1, "Escape discards without sending");
        input.Bindings = Bindings("IK_Enter=Jump\nIK_J=Talk\nIK_K=TeamTalk");
        Press(Key.Enter);
        Expect(!chat.IsOpen, "assigned Enter remains gameplay input");
        Press(Key.J);
        Expect(chat.IsOpen && chat.Panel.Channel == ChatChannel.All, "rebound Talk opens All");
        chat.Close();
        Press(Key.K);
        Expect(chat.IsOpen && chat.Panel.Channel == ChatChannel.Team, "rebound TeamTalk opens Team");
        chat.Close();
        input.Bindings = Bindings();
        chat.AllowNearby = true;
        chat.Open(ChatChannel.Team);
        chat.Panel.Draft = "nearby draft";
        Press(Key.Tab);
        Expect(chat.Panel.Channel == ChatChannel.Nearby && chat.Panel.Draft == "nearby draft", "hub Nearby participates in channel cycle");
        chat.Close();
        chat.AllowNearby = false;
        chat.CanOpen = () => false;
        Press(Key.Enter);
        Expect(!chat.IsOpen, "menu guard blocks opening");
        chat.CanOpen = null;
        chat.VisibilityAllowed = () => false;
        Press(Key.F5);
        Expect(!chat.IsOpen, "hidden HUD blocks opening");
        chat.VisibilityAllowed = null;
        chat.Open(EChatScope.Say);
        chat.VisibilityAllowed = () => false;
        chat._Process(0);
        Expect(!chat.IsOpen && input.Enabled, "hiding HUD closes composer without stranding gameplay input");
        Expect(!Pointer.IsCaptured, "hidden HUD does not recapture pointer");
        chat.VisibilityAllowed = null;
        chat.Open(EChatScope.Say);
        chat.CanOpen = () => false;
        chat._Process(0);
        Expect(!chat.IsOpen && !input.Enabled, "another overlay retains ownership of disabled gameplay input");
        chat.CanOpen = null;
        input.Enabled = true;
        _local.Clear();
    }

    private static InputBindings Bindings(string extra = "") => InputBindings.FromLayout(
        KeyLayout.Parse("IK_F5=Talk\nIK_F6=TeamTalk\nIK_W=Button BT_Up\nIK_LeftMouse=Fire\nIK_MouseX=Axis aMouseX Speed=1\n" + extra), PlaySide.Spy);

    private void Press(Key key, uint unicode = 0)
    {
        _viewport!.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
        _viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Add(string text) => _chat!.Transcript.Add(ChatChannel.Team, text, "Ghost", 1, 0, _now);

    private void BeginVisualCase(int index)
    {
        _case = index;
        _wait = 6;
        Vector2I size = Sizes[index / 4];
        int variant = index % 4;
        _source.Variant = variant;
        _viewport!.Size = size;
        _background!.Size = size;
        _hud!.ScreenSize = size;
        _hud.Visible = variant != 3;
        _chat!.Close();
        _chat.HubLayout = variant == 3;
        _chat.Panel.Side = variant == 1 ? HudSide.Merc : HudSide.Spy;
        _chat.AllowNearby = variant == 3;
        _chat.Transcript = new ChatTranscript();
        _chat.Transcript.Add(ChatChannel.System, variant == 3 ? "Welcome to the hub." : "Ghost joined the match.", "", 0, -1, _now);
        _chat.Transcript.Add(ChatChannel.All, "Good luck, have fun!", "Shadownet", 2, 1, _now);
        _chat.Transcript.Add(ChatChannel.Team, "I'll cover the entrance while you reach the terminal. Watch the hallway on your left.", "Ghost", 1, 0, _now);
        _chat.Transcript.Add(ChatChannel.All, "[b]This is literal text[/b] — no formatting from players.", "[color=red]Sam[/color]", 3, 0, _now);
        if (variant == 3) _chat.Transcript.Add(ChatChannel.Nearby, "Meet us at the next match pad.", "Lambert", 4, 1, _now);
    }

    private SmokeResult VisualCase()
    {
        Vector2I size = Sizes[_case / 4];
        string variant = new[] { "spy", "merc", "spectator", "hub" }[_case % 4];
        Rect2 viewportRect = new(Vector2.Zero, size);
        ChatPanel panel = _chat!.Panel;
        if (!panel.Expanded)
        {
            Expect(panel.PassiveLineCount is > 0 and <= 6, $"{variant} {size}: passive wrapped line bound");
            Expect(viewportRect.Encloses(panel.GetNode<Control>("Recent").GetGlobalRect()), $"{variant} {size}: passive bounds");
            Capture($"modern-chat-{variant}-{size.X}x{size.Y}-recent");
            _chat.Open(EChatScope.TeamSay);
            panel.Draft = "Hold here, I'll cover the entrance.";
            _wait = 5;
            return SmokeResult.Running;
        }
        Expect(viewportRect.Encloses(panel.GetNode<Control>("Conversation").GetGlobalRect()), $"{variant} {size}: expanded bounds");
        Expect(viewportRect.Encloses(panel.Entry.GetGlobalRect()), $"{variant} {size}: editor bounds");
        Expect(Descendants(panel).OfType<Label>().Any(label => label.Text == $"{panel.Draft.Length}/100"),
            $"{variant} {size}: character counter follows the current draft");
        Expect(Descendants(panel).OfType<RichTextLabel>().Any(row => row.GetParsedText().Contains("[b]This is literal text[/b]", StringComparison.Ordinal)),
            $"{variant} {size}: player markup remains literal");
        Capture($"modern-chat-{variant}-{size.X}x{size.Y}-open");
        if (_case + 1 < Sizes.Length * 4)
        {
            BeginVisualCase(_case + 1);
            return SmokeResult.Running;
        }
        _viewport!.RemoveChild(_chat);
        Expect(ChatInput.Typing is null && !_chat.IsOpen, "scene exit closes and clears active composer");
        _chat.QueueFree();
        return Finish(_failures.Count == 0
            ? SmokeResult.Pass($"editor, bindings, input isolation, history, fade and scroll checks; 20 HUD/layout cases, {_captures} captures")
            : SmokeResult.Fail(string.Join("; ", _failures)));
    }

    private void Capture(string name) { if (MenuPage.Shot(_chat!, name) is not null) _captures++; }
    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }
    private void Expect(bool condition, string detail) { if (!condition) _failures.Add(detail); }
    private SmokeResult Finish(SmokeResult result)
    {
        _viewport?.QueueFree();
        Pointer.Release();
        Pointer.Detached = _savedDetached;
        return result;
    }

    private sealed class Source : IHudSource
    {
        public int Variant { get; set; }
        public void Fill(HudSnapshot snapshot)
        {
            snapshot.Valid = true;
            snapshot.Side = Variant == 1 ? HudSide.Merc : HudSide.Spy;
            snapshot.HasPawn = true;
            snapshot.Health = 85;
            snapshot.EnergyLevel = 70;
            snapshot.EnergyLevelMax = 100;
            snapshot.HasWeapon = true;
            snapshot.ClipAmmo = 24;
            snapshot.Ammo = 120;
            snapshot.ShowReticle = Variant == 1;
            snapshot.TimeLimit = 10;
            snapshot.ElapsedTime = 93;
            snapshot.ObjectivesLeft = 2;
            snapshot.Lives = 3;
            snapshot.TimeSeconds = 100;
            snapshot.Spectator.Active = Variant == 2;
            snapshot.Spectator.ShowWatching = true;
            snapshot.Spectator.Watched = "Ghost";
            snapshot.Spectator.Lives = "3";
            snapshot.Spectator.RemainingTime = "08:27";
            snapshot.Spectator.Objectives = "2";
        }
    }
}
