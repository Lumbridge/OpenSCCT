using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Voice;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>Settings of a hub visit.</summary>
public sealed record HubClientSettings
{
    public string PlayerName { get; init; } = "Player";

    /// <summary>The side asked for at login: 0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    public MapCatalog? Maps { get; init; }

    public NetClassRegistry? Classes { get; init; }

    /// <summary>Install the gameplay services on the client world (bots and tests; the play scene installs its own).</summary>
    public bool InstallGameplayServices { get; init; } = true;

    public NetDriverSettings Net { get; init; } = new();

    /// <summary>Carry voice chat on the visit's transport (<see cref="HubClient.Voice"/>, read by <see cref="HubVoice"/>).</summary>
    public bool Voice { get; init; } = true;

    /// <summary>Back from this pad's match: the hub spawns the player beside it (<c>?HubReturn=</c>), or null.</summary>
    public string? ReturnPad { get; init; }

    /// <summary>The master's ticket for this hub (a signed-in player's join answer), or null: a guest.</summary>
    public string? Ticket { get; init; }

    /// <summary>The game's install id (the hub's bans), or empty.</summary>
    public string InstallId { get; init; } = string.Empty;

    /// <summary>The title the player shows (TitleCatalog id); the hub keeps it only when the ticket unlocks it.</summary>
    public string? Title { get; init; }

    /// <summary>What the player wears as a spy (the hub keeps only what his ticket or host account unlocks).</summary>
    public CosmeticLook SpyLook { get; init; }

    /// <summary>What the player wears as a merc.</summary>
    public CosmeticLook MercLook { get; init; }

    /// <summary>The key of the hub this game hosts (its player gets the host account's unlocks and rank), or null.</summary>
    public string? HostKey { get; init; }
}

/// <summary>The player's own place in a pad's queue, as the hub last said (<c>hub queue</c>).</summary>
/// <param name="PadId">The pad, or null when he is on none.</param>
/// <param name="Side">The side the next match gives him (0 spies, 1 mercs), -1 when it is full.</param>
/// <param name="Text">"Queued on Pad 2 as a merc: 3/4, waiting for 1 merc".</param>
public readonly record struct HubQueueStatus(string? PadId, int Side, string Text);

/// <summary>One line of the hub's chat box: a player's chat line or a system line (<see cref="PlayerId"/> 0).</summary>
public readonly record struct HubChatEntry(int PlayerId, string Name, int Team, string Text, bool TeamOnly, bool Local = false)
{
    public bool IsSystem => PlayerId == 0;

    public override string ToString() => IsSystem ? Text : $"{Name}: {Text}";
}

/// <summary>One player of the hub as the player list shows him (<c>hub who</c>).</summary>
public readonly record struct HubRosterEntry(int PlayerId, string Name, int Team, int PingMs, bool SignedIn);

/// <summary>A name over a pawn's head: where (UE space), who, which side and how far from the viewer.</summary>
/// <param name="Rank">The player's rank and title (OpenScct.Net.Cosmetics.PlayerRank packed; 0 for guests).</param>
public readonly record struct HubNameTag(FVector Location, string Name, int Team, float Distance, int PlayerId, int Rank = 0);

/// <summary>
/// A player's visit to a hub (an OpenSCCT addition, spec/additions/hub.md): a <see cref="ClientSession"/> with the
/// gameplay on and the hub's <see cref="PlayRules"/> installed, the hub's session lines (welcome, side, system lines),
/// the chat box, side changes and respawns, and the name tags of the pawns around the player. The session is ticked
/// by <see cref="Tick"/>, or elsewhere (the play scene) when <see cref="SessionTickedElsewhere"/> is set; the lines and
/// chat arrive through the session's events either way. Godot-free; the game's hub screen and HUD read it.
/// </summary>
public sealed class HubClient : IDisposable
{
    /// <summary>Chat box lines kept.</summary>
    public const int MaxChat = 64;

    private readonly List<HubChatEntry> _chat = new();
    private readonly Dictionary<string, HubSign> _signs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HubPadInfo> _pads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (LobbySetup Setup, bool Changed)> _padSetups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HubRosterEntry> _roster = new();
    private readonly Dictionary<string, (string Text, double At)> _lastSaid = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chatMuted = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsSession;
    private bool _disposed;

    public HubClient(ITransport transport, string serverAddress, long nowMicros, HubClientSettings? settings = null, bool ownsTransport = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Settings = settings ?? new HubClientSettings();
        Voice = Settings.Voice ? new VoiceTransport(transport, ownsInner: ownsTransport) : null;
        Session = new ClientSession((ITransport?)Voice ?? transport, serverAddress, nowMicros, new ClientSettings
        {
            PlayerName = Settings.PlayerName,
            LoginOptions = "?Team=" + (Settings.Team == 1 ? 1 : 0).ToString(CultureInfo.InvariantCulture) +
                (Settings.ReturnPad is { Length: > 0 } pad ? "?" + HubProtocol.ReturnOption + "=" + pad : string.Empty) +
                (Settings.InstallId.Length > 0 ? "?SvmId=" + OptionText(Settings.InstallId) : string.Empty) +
                (Settings.Ticket is { Length: > 0 } ticket ? "?SvmTicket=" + OptionText(ticket) : string.Empty) +
                (Settings.Title is { Length: > 0 } title ? "?SvmTitle=" + OptionText(title) : string.Empty) +
                (!Settings.SpyLook.IsDefault ? "?SvmLookS=" + Settings.SpyLook.ToHex() : string.Empty) +
                (!Settings.MercLook.IsDefault ? "?SvmLookM=" + Settings.MercLook.ToHex() : string.Empty) +
                (Settings.HostKey is { Length: > 0 } hostKey ? "?SvmHost=" + OptionText(hostKey) : string.Empty),
            Gameplay = true,
            InstallGameplayServices = Settings.InstallGameplayServices,
            Maps = Settings.Maps,
            Classes = Settings.Classes,
            Net = Settings.Net,
        }, ownsTransport);
        _ownsSession = true;
        Attach(Session);
    }

    /// <summary>A login option's value with the characters that would end it taken out.</summary>
    private static string OptionText(string text) => text.Replace("?", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>A visit over a session made elsewhere (a bot's): the hub lines, chat and rules on it; the session is not disposed with it.</summary>
    public HubClient(ClientSession session)
    {
        Settings = new HubClientSettings { PlayerName = session?.Settings.PlayerName ?? "Player" };
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Attach(session);
    }

    public HubClientSettings Settings { get; }

    public ClientSession Session { get; }

    /// <summary>The visit's voice transport (null with voice off, or for a session made elsewhere).</summary>
    public VoiceTransport? Voice { get; }

    /// <summary>The server's endpoint on <see cref="Voice"/>.</summary>
    public EndpointId ServerEndpoint => Session.Connection?.RemoteEndpoint ?? EndpointId.None;

    /// <summary>The play scene ticks the session; <see cref="Tick"/> then only does the hub's part.</summary>
    public bool SessionTickedElsewhere { get; set; }

    /// <summary>True once the server's welcome line arrived.</summary>
    public bool Welcomed { get; private set; }

    public string ServerName { get; private set; } = string.Empty;

    /// <summary>This player's id in the hub (its welcome line's), or 0 before it.</summary>
    public int PlayerId { get; private set; }

    public int MaxPlayers { get; private set; }

    /// <summary>The side the server says this player plays (0 spies, 1 mercs), or -1 before it said.</summary>
    public int Team { get; private set; } = -1;

    /// <summary>The activities as the server told them (boards, the course run, the range round, emotes, jukebox, ball).</summary>
    public HubPlayState Play { get; } = new();
    /// <summary>The world texts the hub sent (pad signs, leaderboards) by id.</summary>
    public IReadOnlyDictionary<string, HubSign> Signs => _signs;

    /// <summary>The match pads by id: their match (map code, mode, team size), state and lineup.</summary>
    public IReadOnlyDictionary<string, HubPadInfo> Pads => _pads;

    /// <summary>
    /// Each pad's full match settings (<c>hub padsetup</c>) and whether they were set at its laptop (else hub.json's).
    /// </summary>
    public IReadOnlyDictionary<string, (LobbySetup Setup, bool Changed)> PadSetups => _padSetups;

    /// <summary>Raised when a pad's settings arrived or changed (its id).</summary>
    public event Action<string>? PadSetupChanged;

    /// <summary>Raised when the hub opens a pad's laptop for this player (he pressed U at it): the pad's id.</summary>
    public event Action<string>? LaptopOpened;

    /// <summary>The player's own place in a queue.</summary>
    public HubQueueStatus Queue { get; private set; } = new(null, -1, string.Empty);

    /// <summary>The last match ticket the hub sent (the game takes it and leaves for the match).</summary>
    public HubMatchOffer? Offer { get; private set; }

    /// <summary>Raised when a sign was put up or changed.</summary>
    public event Action<HubSign>? SignChanged;

    /// <summary>Raised when a sign was taken down (its id).</summary>
    public event Action<string>? SignRemoved;

    /// <summary>Raised for each pad line; the bool is true when the pad's match (map, mode or team size) is new or changed.</summary>
    public event Action<HubPadInfo, bool>? PadChanged;

    /// <summary>Raised when the hub sends this player to a pad's match.</summary>
    public event Action<HubMatchOffer>? MatchOffered;

    /// <summary>The chat box, oldest first.</summary>
    public IReadOnlyList<HubChatEntry> Chat => _chat;

    /// <summary>Raised for each new chat box line.</summary>
    public event Action<HubChatEntry>? ChatAdded;

    /// <summary>Raised when the server says this player's side changed.</summary>
    public event Action<int>? SideChanged;

    /// <summary>The local player's controller, once it arrived.</summary>
    public SPlayerController? LocalPlayer => Session.LocalPlayer as SPlayerController;

    /// <summary>Refused or dropped: the reason, else null.</summary>
    public string? Error => Session.State == EClientState.Rejected ? Session.RejectReason ?? "Refused" :
        Session.State == EClientState.Disconnected ? "The connection to the hub closed." : null;

    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SessionTickedElsewhere)
        {
            Session.Tick(nowMicros);
        }
    }

    /// <summary>Asks to play the other side (the server respawns the player at the plaza).</summary>
    public void RequestSide(int team) => Session.SendText(HubProtocol.Side(team == 1 ? 1 : 0));

    /// <summary>Asks to be put back at the plaza.</summary>
    public void Respawn() => Session.SendText(HubProtocol.Respawn());

    /// <summary>Sets a pad's match from its laptop (the hub checks the player stands at it).</summary>
    public void SetPadSetup(string padId, LobbySetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Session.SendText(HubProtocol.PadSetupRequest(padId, setup.ToOptions()));
    }

    /// <summary>Asks to watch the match being played on a pad (the hub answers with a spectator's offer).</summary>
    public void Watch(string padId) => Session.SendText(HubProtocol.Watch(padId));

    /// <summary>Wears new looks now (the Armoury changed them); the hub checks them as at login.</summary>
    public void SetLook(CosmeticLook spy, CosmeticLook merc) => Session.SendText(HubProtocol.Look(spy, merc));

    /// <summary>Plays an emote (one of <see cref="HubEmotes.All"/>) for the players near.</summary>
    public void Emote(string emote) => Session.SendText(HubPlayProtocol.EmoteRequest(emote));

    /// <summary>Uses what is near: starts a range round at the firing line, changes the jukebox's track.</summary>
    public void Use() => Session.SendText(HubPlayProtocol.Use());

    /// <summary>Stops the player's course run or range round.</summary>
    public void CancelActivity() => Session.SendText(HubPlayProtocol.Cancel());

    /// <summary>Says a line to everyone in the hub (or to the player's side).</summary>
    public void Say(string text, bool teamOnly = false) => Session.SendChat(teamOnly ? EChatScope.TeamSay : EChatScope.Say, text);

    /// <summary>Says a line to the players near this one only.</summary>
    public void SayLocal(string text) => Session.SendText(HubProtocol.LocalRequest(text));

    /// <summary>The hub's players as last listed (the player list).</summary>
    public IReadOnlyCollection<HubRosterEntry> Roster => _roster.Values;

    /// <summary>Hides (or shows again) a player's chat lines, by name, for this visit.</summary>
    public void MuteChat(string name, bool muted)
    {
        if (muted)
        {
            _chatMuted.Add(name);
        }
        else
        {
            _chatMuted.Remove(name);
        }
    }

    public bool IsChatMuted(string name) => _chatMuted.Contains(name);

    /// <summary>
    /// What the player named <paramref name="name"/> last said in chat (any scope) and when (the play clock), for a
    /// bubble over his head. By name: chat lines carry the session's player ids, name tags the replication infos'.
    /// </summary>
    public bool LastSaid(string name, out string text, out double at)
    {
        if (_lastSaid.TryGetValue(name, out var said))
        {
            (text, at) = said;
            return true;
        }

        text = string.Empty;
        at = 0;
        return false;
    }

    /// <summary>The name tags of the other players' pawns within <paramref name="range"/> of <paramref name="viewPoint"/>.</summary>
    public void NameTags(FVector viewPoint, List<HubNameTag> into, float range = HubK.NameTagRange) =>
        CollectNameTags(Session.World.Level, LocalPlayer, viewPoint, range, into);

    /// <summary>
    /// The name tags of every living pawn of <paramref name="level"/> but <paramref name="local"/>'s own, within
    /// <paramref name="range"/> of <paramref name="viewPoint"/>: the name and side from the pawn's replication info,
    /// placed <see cref="HubK.NameTagHeight"/> over the pawn's location, farthest first (the order to draw them in).
    /// </summary>
    public static void CollectNameTags(Level level, PlayerController? local, FVector viewPoint, float range, List<HubNameTag> into)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        float range2 = range * range;
        foreach (SBasePawn pawn in level.DynamicActors<SBasePawn>())
        {
            if (pawn.bDeleteMe || pawn.Health <= 0 || pawn.bHidden || (local is not null && ReferenceEquals(pawn, local.Pawn)) ||
                pawn.PlayerReplicationInfo is not { } pri)
            {
                continue;
            }

            FVector at = pawn.Location + new FVector(0f, 0f, HubK.NameTagHeight);
            float d2 = (at - viewPoint).SizeSquared();
            if (d2 > range2)
            {
                continue;
            }

            int team = pri.Team?.TeamIndex ?? (pawn.SPawnType == PawnType.SPAWNOFFENSE ? 0 : 1);
            into.Add(new HubNameTag(at, pri.PlayerName, team, MathF.Sqrt(d2), pri.PlayerID, pri.SvmRank));
        }

        into.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
    }

    private void Attach(ClientSession session)
    {
        session.SessionLineReceived += OnLine;
        session.ChatReceived += OnChat;
        session.BeforeWorldTick += OnBeforeWorldTick;
    }

    private static void OnBeforeWorldTick(ClientSession session)
    {
        Level level = session.World.Level;
        if (level.GetService<PlayRules>() is null)
        {
            level.SetService(PlayRules.Hub);
        }
    }

    private void OnLine(string line)
    {
        if (Play.Read(line))
        {
            return;
        }

        if (!HubProtocol.TryParse(line, out string verb, out List<string> fields, fromServer: true))
        {
            return;
        }

        switch (verb)
        {
            case "welcome":
                Welcomed = true;
                PlayerId = SessionProtocol.Int(fields, 1);
                MaxPlayers = SessionProtocol.Int(fields, 2);
                ServerName = SessionProtocol.Text(fields, 3);
                break;
            case "side":
                int team = SessionProtocol.Int(fields, 0) == 1 ? 1 : 0;
                if (Team == 0 && team == 1 && LocalPlayer is { } pc)
                {
                    // The spy's start state left the crouch toggle on (EnterStartState sets it only for spies; in the
                    // original a controller never changes side), so the new merc would crouch as soon as he landed.
                    pc.bCrouchWithRoll = false;
                }

                Team = team;
                SideChanged?.Invoke(Team);
                break;
            case "msg":
                Add(new HubChatEntry(0, string.Empty, -1, SessionProtocol.Text(fields, 0), false));
                break;
            case "who" when fields.Count >= 5:
            {
                int id = SessionProtocol.Int(fields, 0);
                _roster[id] = new HubRosterEntry(id, SessionProtocol.Text(fields, 4), SessionProtocol.Int(fields, 1),
                    SessionProtocol.Int(fields, 2), SessionProtocol.Int(fields, 3) == 1);
                break;
            }

            case "gone":
                if (_roster.Remove(SessionProtocol.Int(fields, 0), out HubRosterEntry goneEntry))
                {
                    _lastSaid.Remove(goneEntry.Name);
                }

                break;
            case "local" when fields.Count >= 3:
            {
                int id = SessionProtocol.Int(fields, 0);
                string who = _roster.TryGetValue(id, out HubRosterEntry r) ? r.Name : "?";
                Add(new HubChatEntry(id, who, SessionProtocol.Int(fields, 1), SessionProtocol.Text(fields, 2), false, Local: true));
                break;
            }
            case "sign" when fields.Count >= 9:
                var sign = new HubSign(fields[0],
                    new FVector(HubProtocol.Float(fields, 1), HubProtocol.Float(fields, 2), HubProtocol.Float(fields, 3)),
                    HubSigns.DecodeText(SessionProtocol.Text(fields, 9)),
                    uint.TryParse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba) ? rgba : HubSigns.White,
                    HubProtocol.Float(fields, 5, 1f),
                    new HubSignFace(HubProtocol.Float(fields, 6, -1f), HubProtocol.Float(fields, 7), HubProtocol.Float(fields, 8)));
                _signs[sign.Id] = sign;
                SignChanged?.Invoke(sign);
                break;
            case "unsign" when fields.Count >= 1:
                if (_signs.Remove(fields[0]))
                {
                    SignRemoved?.Invoke(fields[0]);
                }

                break;
            case "pad" when HubPadInfo.Parse(fields) is { } pad:
                bool configChanged = !_pads.TryGetValue(pad.Id, out HubPadInfo? old) || old.Map != pad.Map || old.Mode != pad.Mode ||
                    old.TeamSize != pad.TeamSize;
                _pads[pad.Id] = pad;
                PadChanged?.Invoke(pad, configChanged);
                break;
            case "padsetup" when fields.Count >= 3:
            {
                string id = fields[0];
                _padSetups[id] = (LobbySetup.FromOptions(SessionProtocol.Text(fields, 2)), SessionProtocol.Int(fields, 1) == 1);
                PadSetupChanged?.Invoke(id);
                break;
            }

            case "laptop" when fields.Count >= 1:
                LaptopOpened?.Invoke(SessionProtocol.Text(fields, 0));
                break;
            case "queue":
                string padId = SessionProtocol.Text(fields, 0);
                Queue = new HubQueueStatus(padId is "" or "-" ? null : padId, SessionProtocol.Int(fields, 1, -1), SessionProtocol.Text(fields, 2));
                break;
            case "match" when HubMatchOffer.Parse(fields) is { } offer:
                Offer = offer;
                MatchOffered?.Invoke(offer);
                break;
        }
    }

    private void OnChat(ChatLine line) =>
        Add(new HubChatEntry(line.SenderId, line.SenderName, line.SenderTeam == ChatK.NoTeam ? -1 : line.SenderTeam, line.Text, line.Scope == EChatScope.TeamSay));

    private void Add(HubChatEntry entry)
    {
        if (!entry.IsSystem && _chatMuted.Contains(entry.Name))
        {
            return;
        }

        if (!entry.IsSystem)
        {
            _lastSaid[entry.Name] = (entry.Text, Play.Now);
        }

        _chat.Add(entry);
        if (_chat.Count > MaxChat)
        {
            _chat.RemoveAt(0);
        }

        ChatAdded?.Invoke(entry);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.SessionLineReceived -= OnLine;
        Session.ChatReceived -= OnChat;
        Session.BeforeWorldTick -= OnBeforeWorldTick;
        if (_ownsSession)
        {
            Session.Dispose();
        }
    }
}
