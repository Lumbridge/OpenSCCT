using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server.Hub;
using OpenScctGame.Chat;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Controls;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's own HUD over the play scene (an OpenSCCT addition): every other player's name over his head, coloured by
/// side (<see cref="HubClient.CollectNameTags"/>), the shared chat panel (Enter to type/send, Escape to cancel;
/// Tab switches between everyone, the player's side and nearby players), the voice overlay (who is heard, the muted microphone),
/// the match pad panel (top centre, while the player stands on a pad: "Queued on Pad 2 as a merc: 3/4, waiting for 1
/// merc", in the pad's state colour, or the hub service's note about the last match), and the hub keys: J play spy,
/// K play merc, N back to the plaza, Enter or F5 chat, F6 chat to your side. The activities' part (HubHud.Activities.cs) adds the course
/// clock, the range round, notices, boards, prompts and the emote menu. Self-contained: it reads the visit and the
/// play scene's camera and never writes the simulation.
/// </summary>
public partial class HubHud : CanvasLayer
{
    private const int MaxTags = 64;

    private readonly List<HubNameTag> _tags = new();
    private readonly List<Label> _tagLabels = new();

    /// <summary>The rank emblem beside a name tag, pixels before the tag's scale.</summary>
    private const float TagIconSize = 20f;
    private readonly List<string> _voiceLines = new();
    private Label? _voice;
    private Label? _help;
    private Label? _queue;
    private readonly ChatTranscript _transcript = new();
    private ChatInput? _composer;
    private HubClient? _chatClient;
    private HubPlayerList? _playerList;
    private OpenScctGame.UI.ArmouryScreen? _armoury;
    private HubLaptopPanel? _laptop;

    /// <summary>A chat line shows over its speaker's head this long (seconds).</summary>
    private const double BubbleSeconds = 6.0;

    public HubClient? Client { get; set; }

    public PlayRoot? Round { get; set; }

    public HubService? Service { get; set; }

    /// <summary>True while the player types a chat line (the game's input is off meanwhile).</summary>
    public bool Typing => _composer is { } composer && GodotObject.IsInstanceValid(composer) && composer.IsOpen;

    /// <summary>Name tags drawn last frame (tests and smokes).</summary>
    public int TagsShown { get; private set; }
    internal bool TravelMenuOpen => _travel is not null;
    internal bool PlayerListOpen => _playerList is not null;

    public override void _Ready()
    {
        Name = "HubHud";
        Layer = HudLayer.DefaultLayer + 3;
        _help = new Label
        {
            Name = "Help",
            Position = new Vector2(16f, 40f),
            Modulate = new Color(1f, 1f, 1f, 0.75f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "Hub",
        };
        if (OpenScctGame.UI.UiArt.CaptionFont is { } caption)
        {
            _help.AddThemeFontOverride("font", caption);
            _help.AddThemeFontSizeOverride("font_size", OpenScctGame.UI.UiArt.CaptionSize);
        }

        // Sized now, not first in _Process: a wrapping label with no width puts one letter per line.
        _help.CustomMinimumSize = new Vector2(HelpWidth(), 0f);
        AddChild(_help);
        _queue = new Label { Name = "Queue", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _queue.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
        _queue.AddThemeConstantOverride("shadow_offset_x", 2);
        _queue.AddThemeConstantOverride("shadow_offset_y", 2);
        _queue.AddThemeFontSizeOverride("font_size", 24);
        if (OpenScctGame.UI.UiArt.BodyFont is { } font)
        {
            _queue.AddThemeFontOverride("font", font);
        }

        AddChild(_queue);
        _voice = new Label { Name = "Voice", HorizontalAlignment = HorizontalAlignment.Right };
        _voice.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        AddChild(_voice);
        _chatClient = Client;
        if (_chatClient is not null)
        {
            foreach (HubChatEntry entry in _chatClient.Chat)
            {
                AddChat(entry);
            }

            _chatClient.ChatAdded += AddChat;
        }

        AttachChat();
        for (int i = 0; i < MaxTags; i++)
        {
            var label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            // The player's rank emblem, left of the name (OpenSCCT's own; hidden for guests).
            label.AddChild(new TextureRect
            {
                Name = "Rank",
                Visible = false,
                Size = new Vector2(TagIconSize, TagIconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            _tagLabels.Add(label);
            AddChild(label);
        }

        ReadyActivities();
        ReadyTravel();
        AttachHubInput();
        UpdateBindingHints();
        _laptopClient = Client;
        if (_laptopClient is not null)
        {
            _laptopClient.LaptopOpened += OpenLaptop;
        }
    }

    /// <summary>The client whose laptop lines this HUD listens to (unhooked when it leaves).</summary>
    private HubClient? _laptopClient;

    public override void _ExitTree()
    {
        DetachHubInput();
        if (Client is { } travelClient) travelClient.Play.TravelMenuOpened -= OpenTravel;
        if (_chatClient is not null)
        {
            _chatClient.ChatAdded -= AddChat;
            _chatClient = null;
        }

        if (_laptopClient is not null)
        {
            _laptopClient.LaptopOpened -= OpenLaptop;
            _laptopClient = null;
        }

        if (Round?.Hud?.Source is HudSimReader reader)
        {
            reader.HidePlayers = false;
        }
    }

    /// <summary>The key hints wrap within this share of the screen's width, clear of the top right readouts (frame rate, network, voice).</summary>
    private const float HelpWidthShare = 0.42f;

    private float HelpWidth() => Mathf.Max(320f, GetViewport().GetVisibleRect().Size.X * HelpWidthShare);

    public override void _Process(double delta)
    {
        AttachHubInput();
        UpdateBindingHints();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (_help is not null)
        {
            float width = HelpWidth();
            if (!Mathf.IsEqualApprox(_help.CustomMinimumSize.X, width))
            {
                _help.CustomMinimumSize = new Vector2(width, 0f);
                _help.ResetSize();
            }
        }

        AttachChat();

        if (Round?.Hud?.Source is HudSimReader reader)
        {
            reader.HidePlayers = true;
        }

        if (Client is not { } client)
        {
            return;
        }

        DrawQueue(client, size);
        DrawTags(client);
        DrawVoice(size);
        DrawActivities(client, size);
    }

    /// <summary>Reloaded's voice overlay, top right: who is heard (and at which volume), the muted microphone.</summary>
    private void DrawVoice(Vector2 size)
    {
        if (_voice is null)
        {
            return;
        }

        Service?.VoiceChat?.OverlayLines(_voiceLines);
        _voice.Visible = _voiceLines.Count > 0;
        if (_voice.Visible)
        {
            _voice.Text = string.Join('\n', _voiceLines);
            _voice.Size = new Vector2(360f, 0f);
            _voice.Position = new Vector2(size.X - 376f, 12f);
        }
    }

    /// <summary>The player's place in a pad's queue (or the last match's note), top centre.</summary>
    private void DrawQueue(HubClient client, Vector2 size)
    {
        if (_queue is null)
        {
            return;
        }

        string text = client.Queue.PadId is not null ? client.Queue.Text : Service?.MatchNote ?? string.Empty;
        if (client.Queue.PadId is not null && Service?.DownloadNote is { } download)
        {
            text += "\n" + download;
        }
        _queue.Visible = text.Length > 0;
        if (!_queue.Visible)
        {
            return;
        }

        _queue.Text = text;
        _queue.Modulate = client.Queue.PadId is { } pad && client.Pads.TryGetValue(pad, out OpenScct.Server.Hub.HubPadInfo? info)
            ? HubPadView.StateColour(info.State)
            : new Color(1f, 1f, 1f, 0.9f);
        _queue.Size = new Vector2(size.X * 0.8f, 0f);
        // Under the key hints however many lines they wrap to.
        float below = _help is { Visible: true } help ? help.Position.Y + help.Size.Y + 8f : 0f;
        _queue.Position = new Vector2(size.X * 0.1f, Mathf.Max(84f, below));
    }

    /// <summary>Use the play scene's composer so the hub has one input owner and one chat display.</summary>
    private void AttachChat()
    {
        if (_composer is not null || Round?.Menu?.Hud?.Chat is not { } composer)
        {
            return;
        }

        _composer = composer;
        composer.ReceiveMatchMessages = false;
        composer.AllowEnterShortcut = false;
        composer.Transcript = _transcript;
        composer.Clock = () => Time.GetTicksMsec() / 1000d;
        composer.AllowNearby = true;
        composer.HubLayout = true;
        composer.SendMessage = Send;
        System.Func<bool>? canOpen = composer.CanOpen;
        composer.CanOpen = () => _playerList is null && _armoury is null && _laptop is null && _travel is null && !_emoteOpen &&
            (canOpen?.Invoke() ?? true);
        System.Func<bool>? visible = composer.VisibilityAllowed;
        composer.VisibilityAllowed = () => _playerList is null && _armoury is null && _laptop is null &&
            (visible?.Invoke() ?? true);
    }

    private void AddChat(HubChatEntry entry)
    {
        ChatChannel channel = entry.IsSystem ? ChatChannel.System : entry.TeamOnly ? ChatChannel.Team : entry.Local ? ChatChannel.Nearby : ChatChannel.All;
        _transcript.Add(channel, entry.Text, entry.Name, entry.PlayerId, entry.Team, Time.GetTicksMsec() / 1000d);
    }

    private void DrawTags(HubClient client)
    {
        if (_travel is not null) { HideTags(0); return; }
        Camera3D? camera = Round?.View?.Camera;
        TagsShown = 0;
        if (camera is null || !Round!.IsLoaded)
        {
            HideTags(0);
            return;
        }

        FVector eye = UeSpace.ToUe(camera.GlobalPosition);
        client.NameTags(eye, _tags);
        int shown = 0;
        foreach (HubNameTag tag in _tags)
        {
            if (shown >= _tagLabels.Count)
            {
                break;
            }

            Vector3 at = UeSpace.ToGodot(tag.Location);
            if (camera.IsPositionBehind(at))
            {
                continue;
            }

            Label label = _tagLabels[shown++];
            Vector2 screen = camera.UnprojectPosition(at);
            OpenScct.Net.Cosmetics.PlayerRank rank = OpenScct.Net.Cosmetics.PlayerRank.Unpack(tag.Rank);
            string name = rank.IsNone ? tag.Name
                : rank.Title is { } title ? tag.Name + "\n" + OpenScctGame.UI.RankBadge.Text(rank) + "  " + title.Text
                : tag.Name + "\n" + OpenScctGame.UI.RankBadge.Text(rank);
            label.Text = (client.Play.EmoteOf(tag.PlayerId) is { } emote ? name + "\n" + HubEmotes.Caption(emote) : name) + Bubble(client, tag.Name);
            if (label.GetNodeOrNull<TextureRect>("Rank") is { } icon)
            {
                icon.Visible = !rank.IsNone;
                icon.Texture = rank.IsNone ? null : OpenScctGame.UI.RankIcons.For(rank, (int)TagIconSize);
                icon.Position = new Vector2(-TagIconSize - 4f, 0f);
            }
            label.Modulate = tag.Team == 1 ? HudK.MercColour : HudK.SpyColour;
            float scale = Mathf.Clamp(1.4f - tag.Distance / HubK.NameTagRange, 0.6f, 1.2f);
            label.Scale = new Vector2(scale, scale);
            label.ResetSize();
            label.Position = screen - new Vector2(label.Size.X * scale / 2f, label.Size.Y * scale);
            label.Visible = true;
        }

        TagsShown = shown;
        HideTags(shown);
    }

    /// <summary>What the player said in the last few seconds, as a bubble under his name (two short lines at most), or empty.</summary>
    private static string Bubble(HubClient client, string name)
    {
        if (!client.LastSaid(name, out string text, out double at) || client.Play.Now - at > BubbleSeconds)
        {
            return string.Empty;
        }

        const int Width = 34;
        if (text.Length <= Width)
        {
            return "\n\u201c" + text + "\u201d";
        }

        int cut = text.LastIndexOf(' ', Width);
        cut = cut > 8 ? cut : Width;
        string first = text[..cut].TrimEnd();
        string rest = text[cut..].TrimStart();
        if (rest.Length > Width)
        {
            rest = rest[..(Width - 3)] + "...";
        }

        return "\n\u201c" + first + "\n" + rest + "\u201d";
    }

    private void HideTags(int from)
    {
        for (int i = from; i < _tagLabels.Count; i++)
        {
            _tagLabels[i].Visible = false;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!HubInputAvailable || _travel is not null || !@event.IsPressed() || @event is InputEventKey { Echo: true })
        {
            return;
        }

        if (EmoteMenuInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Typing)
        {
            return;
        }

        if (HubCommandOf(@event) is { } command && RunHubCommand(command))
            GetViewport().SetInputAsHandled();
    }

    private bool HubInputAvailable => Client is not null && !Typing && _armoury is null && _laptop is null &&
        Round?.Menu?.IsOpen != true && BindingInput.Current?.CaptureSuspended != true;

    private bool HubModalActive => Client is not null && !Typing && Round?.Menu?.IsOpen != true &&
        BindingInput.Current?.CaptureSuspended != true &&
        (_travel is not null || _emoteOpen || _playerList is not null || _laptop is not null || _armoury is not null);

    private bool CloseOverlayCommand(string command)
    {
        if (_travel is not null && command is "ShowMenu" or "HubTravel") CloseTravel();
        else if (_emoteOpen && command is "ShowMenu" or "HubEmotes") CloseEmoteMenu();
        else if (_playerList is not null && command is "ShowMenu" or "HubPlayers") TogglePlayerList(Client!);
        else if (_laptop is not null && command == "ShowMenu") CloseLaptop();
        else if (_armoury is not null && command is "ShowMenu" or "HubArmoury") CloseArmoury();
        else return false;
        return true;
    }

    private string? HubCommandOf(InputEvent input)
    {
        if (Round?.Controls?.Bindings is not { } bindings || BindingInput.ResolveKey(input, bindings) is not { } key) return null;
        foreach (var action in bindings.Actions(key))
            if (action.Name.StartsWith("Hub", System.StringComparison.Ordinal)) return action.Name;
        return null;
    }

    private bool HubBound(InputEvent input, string command)
    {
        if (Round?.Controls?.Bindings is not { } bindings || BindingInput.ResolveKey(input, bindings) is not { } key) return false;
        foreach (var action in bindings.Actions(key)) if (action.Name == command) return true;
        return false;
    }

    private PlayInput? _hubInput;

    private void AttachHubInput()
    {
        if (ReferenceEquals(_hubInput, Round?.Controls)) return;
        DetachHubInput();
        _hubInput = Round?.Controls;
        if (_hubInput is { } hubInput)
        {
            hubInput.LocalCommand += HubLocalCommand;
            hubInput.ModalInputActive = () => HubModalActive;
            hubInput.ModalCommand = command =>
            {
                if (!HubModalActive) return false;
                if (CloseOverlayCommand(command)) return true;
                if (_playerList is not null || _laptop is not null || _armoury is not null) return true;
                return RunHubCommand(command);
            };
        }
    }

    private void DetachHubInput()
    {
        if (_hubInput is not { } input) return;
        input.LocalCommand -= HubLocalCommand;
        input.ModalInputActive = null;
        input.ModalCommand = null;
        _hubInput = null;
    }

    private void HubLocalCommand(string command)
    {
        if (HubInputAvailable) RunHubCommand(command);
    }

    private bool RunHubCommand(string command)
    {
        if (Client is not { } client) return false;
        if (_travel is not null)
        {
            if (command is "HubTravel" or "ShowMenu") CloseTravel();
            return true;
        }
        if (_emoteOpen && command.StartsWith("HubEmote", System.StringComparison.Ordinal) &&
            int.TryParse(command[8..], out int choice) && choice >= 1 && choice <= HubEmotes.All.Count)
        {
            client.Emote(HubEmotes.All[choice - 1]); CloseEmoteMenu(); return true;
        }
        switch (command)
        {
            case "HubTravel":
                OpenTravel();
                break;
            case "HubSpy":
                client.RequestSide(0);
                break;
            case "HubMerc":
                client.RequestSide(1);
                break;
            case "HubPlaza":
                client.Respawn();
                break;
            case "HubEmotes":
                if (_emoteOpen) CloseEmoteMenu(); else OpenEmoteMenu();
                break;
            case "HubUse":
                client.Use();
                break;
            case "HubCancel":
                client.CancelActivity();
                break;
            case "HubWatch" when client.Queue.PadId is { } watchPad:
                client.Watch(watchPad);
                break;
            case "HubPlayers":
                TogglePlayerList(client);
                break;
            case "HubArmoury" when _playerList is null:
                OpenArmoury();
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>P: the hub's players, with each one's voice volume and mute and a chat mute.</summary>
    private void TogglePlayerList(HubClient client)
    {
        if (_playerList is { } open)
        {
            _playerList = null;
            open.QueueFree();
            if (Round?.Controls is { } on)
            {
                on.Enabled = true;
            }

            return;
        }

        _playerList = new HubPlayerList { Client = client, Voice = Service?.VoiceChat?.Voice };
        _playerList.Closed += () => TogglePlayerList(client);
        AddChild(_playerList);
        if (Round?.Controls is { } input)
        {
            // The pointer is freed for the sliders; a click in the world captures it again once the list is closed.
            input.ReleasePointer();
            input.Enabled = false;
        }
    }

    /// <summary>
    /// A pad's laptop (the hub answered U at it): the pad's match settings as a lobby's Session Parameters. Apply sends
    /// them; the hub checks the player still stands at the laptop.
    /// </summary>
    private void OpenLaptop(string padId)
    {
        if (Client is not { } client || _laptop is not null || _armoury is not null || Typing)
        {
            return;
        }

        if (_playerList is not null)
        {
            TogglePlayerList(client);
        }

        CloseEmoteMenu();
        _laptop = new HubLaptopPanel(client, padId, Service?.Catalog.Maps) { Closed = CloseLaptop };
        AddChild(_laptop);
        if (Round?.Controls is { } input)
        {
            input.ReleasePointer();
            input.Enabled = false;
        }
    }

    private void CloseLaptop()
    {
        if (_laptop is not { } open)
        {
            return;
        }

        _laptop = null;
        open.QueueFree();
        if (Round?.Controls is { } input)
        {
            // A click in the world captures the pointer again (as after the player list).
            input.Enabled = true;
        }
    }

    /// <summary>
    /// O: the Armoury over the hub. A look picked there is saved, and HubService sends saved looks to the hub, which
    /// puts them on the player's character at once.
    /// </summary>
    private void OpenArmoury()
    {
        _armoury = new OpenScctGame.UI.ArmouryScreen { Closed = CloseArmoury, CloseInput = _ => false };
        AddChild(_armoury);
        if (Round?.Controls is { } input)
        {
            input.ReleasePointer();
            input.Enabled = false;
        }
    }

    private void CloseArmoury()
    {
        if (_armoury is not { } open)
        {
            return;
        }

        _armoury = null;
        open.QueueFree();
        if (Round?.Controls is { } input)
        {
            // A click in the world captures the pointer again (as after the player list).
            input.Enabled = true;
        }
    }

    private void Send(ChatChannel channel, string text)
    {
        string line = text.Trim();
        if (line.Length > 1 && line[0] == '/' && HubEmotes.IsEmote(line[1..].ToLowerInvariant()))
        {
            // "/wave" and the like play the emote instead of saying it.
            Client?.Emote(line[1..].ToLowerInvariant());
        }
        else if (line.StartsWith("/l ", System.StringComparison.OrdinalIgnoreCase) && line.Length > 3)
        {
            Client?.SayLocal(line[3..]);
        }
        else if (line.Length > 0 && channel == ChatChannel.Nearby)
        {
            Client?.SayLocal(line);
        }
        else if (line.Length > 0)
        {
            Client?.Say(line, channel == ChatChannel.Team);
        }
    }
}
