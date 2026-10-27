using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server.Hub;

/// <summary>One queued player of a pad: who, when he stepped on, and the side the lineup gives him (-1: waiting).</summary>
public sealed class HubQueueEntry
{
    internal HubQueueEntry(HubPlayer player, long order)
    {
        Player = player;
        Order = order;
    }

    public HubPlayer Player { get; }

    public long Order { get; }

    /// <summary>0 spies, 1 mercs, -1 not in the next match (both sides full).</summary>
    public int Side { get; internal set; } = -1;

    /// <summary>The player pressed ready (U on the pad); cleared when he drops out of the lineup.</summary>
    public bool Ready { get; internal set; }
}

/// <summary>A match pad on the server: its match, where it is, its queue and its countdown.</summary>
public sealed class HubPad
{
    internal HubPad(HubPadConfig config, HubPadArea area, int number)
    {
        Config = config;
        Area = area;
        Number = number;
        State = config.Playable ? HubPadState.Idle : HubPadState.Off;
    }

    public HubPadConfig Config { get; internal set; }

    public HubPadArea Area { get; }

    /// <summary>1 for Pad1 (as the floor marking reads).</summary>
    public int Number { get; }

    public HubPadState State { get; internal set; }

    /// <summary>The people on the pad, first come first.</summary>
    public List<HubQueueEntry> Queue { get; } = new();

    /// <summary>Level time the countdown ends at, or -1.</summary>
    public float CountdownEnd { get; internal set; } = -1f;

    /// <summary>Level time the "starting" state lasts until.</summary>
    internal float StartingUntil { get; set; } = -1f;

    /// <summary>Level time before which no new start is tried (after a failed start).</summary>
    internal float RetryAt { get; set; } = -1f;

    public int Spies { get; internal set; }

    public int Mercs { get; internal set; }

    /// <summary>The pad's matches still running.</summary>
    public int MatchesRunning { get; internal set; }

    internal string LastLine { get; set; } = string.Empty;

    /// <summary>How many of the lineup are ready.</summary>
    public int ReadyCount { get; internal set; }

    public int InLineup => Spies + Mercs;

    /// <summary>Everyone in the lineup is ready (and there is a lineup).</summary>
    public bool AllReady => InLineup > 0 && ReadyCount == InLineup;
}

/// <summary>
/// The hub's match pads (spec/additions/hub.md "Match pads", phase H2): each pad queues for one match (its
/// <see cref="HubPadConfig"/>). The server checks every tick which pad each living pawn stands on: stepping on puts the
/// player at the end of that pad's queue, stepping off (or leaving the hub) takes him out. The lineup is the queue in
/// order, each player on the side he plays in the hub when it has room, else on the other side when that has room,
/// else waiting for the next match. Once the lineup has the people the pad needs, a countdown runs
/// (<see cref="HubConfig.CountdownSeconds"/>, stopped when the lineup drops below); at its end the pad starts the
/// match through the <see cref="IHubMatchLauncher"/> and sends each player of the lineup his ticket
/// (<see cref="HubMatchOffer"/>). Those who were waiting keep their places. The sign over each pad
/// (<see cref="HubSigns"/>) and the <c>hub pad</c> lines show the pad's match and state; each player on a pad gets a
/// <c>hub queue</c> line about his own place.
/// </summary>
public sealed class HubPads
{
    /// <summary>Seconds the pad shows "starting" after a match was started.</summary>
    public const float StartingSeconds = 5f;

    /// <summary>Seconds a failed start waits before the pad tries again.</summary>
    public const float RetrySeconds = 10f;

    /// <summary>Seconds a player sent to a match may stay in the hub before he can queue again (his client did not go).</summary>
    public const float DepartSeconds = 30f;

    public const uint IdleColour = 0xE8ECF2FFu;
    public const uint WaitingColour = 0xFFD45AFFu;
    public const uint CountdownColour = 0x6CF08AFFu;
    public const uint StartingColour = 0x6CD8FFFFu;
    public const uint OffColour = 0x8A9099FFu;

    private readonly HubServer _hub;
    private readonly List<HubPad> _pads = new();
    private readonly List<IHubMatch> _matches = new();
    private readonly List<HubQueueEntry> _gone = new();
    private long _order;

    internal HubPads(HubServer hub, HubConfig config, IHubMatchLauncher? launcher)
    {
        _hub = hub;
        Config = config;
        Launcher = launcher;
        int n = 0;
        foreach (HubPadConfig pad in config.Pads)
        {
            if (config.Area(pad.Id) is { } area)
            {
                n++;
                int number = int.TryParse(pad.Id.AsSpan(pad.Id.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) ? d : n;
                _pads.Add(new HubPad(launcher is null ? pad with { Playable = false } : pad, area, number));
            }
        }
    }

    public HubConfig Config { get; }

    /// <summary>Starts the matches; null when the hub cannot start any (the pads then show as off).</summary>
    public IHubMatchLauncher? Launcher { get; }

    public IReadOnlyList<HubPad> Pads => _pads;

    /// <summary>Matches started and not yet over.</summary>
    public IReadOnlyList<IHubMatch> Matches => _matches;

    /// <summary>Raised when a pad's match or state changed (the client hook's server side: tests and logs).</summary>
    public event Action<HubPad>? PadChanged;

    public HubPad? Find(string id) => _pads.Find(p => string.Equals(p.Config.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Changes what a pad queues for (a rotation or a vote later): the queue stays, the countdown starts over, and
    /// every client is told (its theme and sign follow).
    /// </summary>
    public bool Reconfigure(string padId, HubPadConfig config, MapCatalog maps)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (Find(padId) is not { } pad)
        {
            return false;
        }

        HubConfig one = (Config with { Pads = [config with { Id = pad.Config.Id }] }).Checked(maps, _hub.LogLine);
        if (one.Pads.Count == 0)
        {
            return false;
        }

        pad.Config = Launcher is null ? one.Pads[0] with { Playable = false } : one.Pads[0];
        pad.CountdownEnd = -1f;
        pad.State = pad.Config.Playable ? HubPadState.Idle : HubPadState.Off;
        pad.LastLine = string.Empty;
        return true;
    }

    /// <summary>Tells a player who just joined every pad's match and state.</summary>
    internal void SendAll(PlayerSlot slot)
    {
        foreach (HubPad pad in _pads)
        {
            Send(slot, HubProtocol.Pad(Info(pad)));
        }
    }

    internal void Left(HubPlayer player)
    {
        if (player.Pad is { } pad)
        {
            pad.Queue.RemoveAll(e => ReferenceEquals(e.Player, player));
            player.Pad = null;
        }
    }

    /// <summary>The level's time; called once per hub tick.</summary>
    internal void Tick(float now, IReadOnlyList<HubPlayer> players)
    {
        DrainMatches();
        foreach (HubPlayer p in players)
        {
            UpdatePresence(p, now);
        }

        foreach (HubPad pad in _pads)
        {
            Lineup(pad);
            TickPad(pad, now);
            Publish(pad, now);
        }

        foreach (HubPlayer p in players)
        {
            SendQueueLine(p, now);
        }
    }

    private void UpdatePresence(HubPlayer p, float now)
    {
        if (p.DepartedAt >= 0f)
        {
            if (now - p.DepartedAt < DepartSeconds)
            {
                return;
            }

            p.DepartedAt = -1f;
        }

        HubPad? on = null;
        if (p.Slot.Controller is { bDeleteMe: false } pc && pc.Pawn is { bDeleteMe: false, Health: > 0 } pawn)
        {
            foreach (HubPad pad in _pads)
            {
                if (pad.State != HubPadState.Off && pad.Area.Contains(pawn.Location))
                {
                    on = pad;
                    break;
                }
            }
        }

        if (ReferenceEquals(on, p.Pad))
        {
            return;
        }

        if (p.Pad is { } old)
        {
            old.Queue.RemoveAll(e => ReferenceEquals(e.Player, p));
            _hub.LogLine($"{p.Slot.Name} left the queue of {old.Config.Id}");
        }

        p.Pad = on;
        if (on is not null)
        {
            on.Queue.Add(new HubQueueEntry(p, ++_order));
            _hub.LogLine($"{p.Slot.Name} queued on {on.Config.Id} ({on.Queue.Count} there)");
        }
    }

    /// <summary>The queue in order: the player's own side when it has room, else the other side's room, else waiting.</summary>
    private static void Lineup(HubPad pad)
    {
        int spies = 0;
        int mercs = 0;
        int ready = 0;
        HubPadConfig c = pad.Config;
        foreach (HubQueueEntry e in pad.Queue)
        {
            int own = c.MercsOnly ? 1 : SGameInfo.TeamIndexOf(e.Player.Slot.Controller) == 1 ? 1 : 0;
            bool ownRoom = own == 0 ? spies < c.SpySlots : mercs < c.MercSlots;
            bool otherRoom = own == 0 ? mercs < c.MercSlots : spies < c.SpySlots;
            e.Side = ownRoom ? own : otherRoom ? 1 - own : -1;
            if (e.Side < 0)
            {
                // Only the lineup can be ready: a player pushed out of it has to ready again when a place opens.
                e.Ready = false;
            }
            else if (e.Ready)
            {
                ready++;
            }

            if (e.Side == 0)
            {
                spies++;
            }
            else if (e.Side == 1)
            {
                mercs++;
            }
        }

        pad.Spies = spies;
        pad.Mercs = mercs;
        pad.ReadyCount = ready;
    }

    /// <summary>
    /// U on a pad: the player's ready flag flips (only in the lineup). True when the player stands on a pad, so the
    /// key is the pad's and not the activities'.
    /// </summary>
    public bool ToggleReady(PlayerSlot slot)
    {
        HubPlayer? p = null;
        foreach (HubPlayer x in _hub.Players)
        {
            if (ReferenceEquals(x.Slot, slot))
            {
                p = x;
                break;
            }
        }

        if (p?.Pad is not { } pad || pad.Queue.Find(e => ReferenceEquals(e.Player, p)) is not { } entry)
        {
            return false;
        }

        if (entry.Side >= 0 && pad.State != HubPadState.Starting)
        {
            entry.Ready = !entry.Ready;
            _hub.LogLine($"{p.Slot.Name} is {(entry.Ready ? "ready" : "not ready")} on {pad.Config.Id}");
        }

        return true;
    }

    private void TickPad(HubPad pad, float now)
    {
        if (pad.State == HubPadState.Off)
        {
            return;
        }

        // The countdown waits for a full enough lineup in which everyone pressed ready.
        bool enough = pad.InLineup >= pad.Config.Needed && pad.AllReady && now >= pad.RetryAt;
        if (enough && pad.CountdownEnd < 0f)
        {
            pad.CountdownEnd = now + Config.CountdownSeconds;
            _hub.LogLine($"{pad.Config.Id}: countdown ({pad.InLineup} in the lineup)");
        }
        else if (!enough && pad.CountdownEnd >= 0f)
        {
            pad.CountdownEnd = -1f;
            _hub.LogLine($"{pad.Config.Id}: countdown stopped");
        }

        if (pad.CountdownEnd >= 0f && now >= pad.CountdownEnd)
        {
            pad.CountdownEnd = -1f;
            Start(pad, now);
        }

        pad.State = pad.CountdownEnd >= 0f ? HubPadState.Countdown
            : now < pad.StartingUntil ? HubPadState.Starting
            : pad.Queue.Count > 0 ? HubPadState.Waiting
            : HubPadState.Idle;
    }

    private void Start(HubPad pad, float now)
    {
        var lineup = new List<HubQueueEntry>();
        foreach (HubQueueEntry e in pad.Queue)
        {
            if (e.Side >= 0)
            {
                lineup.Add(e);
            }
        }

        if (lineup.Count == 0 || Launcher is null)
        {
            return;
        }

        var people = new List<(string Name, int Team)>();
        foreach (HubQueueEntry e in lineup)
        {
            people.Add((e.Player.Slot.Name, e.Side));
        }

        var request = new HubMatchRequest(pad.Config, people, HubMatchLauncher.NewPassword());
        IHubMatch match;
        try
        {
            match = Launcher.Start(request);
        }
        catch (InvalidOperationException e)
        {
            pad.RetryAt = now + RetrySeconds;
            _hub.LogLine($"{pad.Config.Id}: the match did not start: {e.Message}");
            foreach (HubQueueEntry q in lineup)
            {
                Send(q.Player.Slot, HubProtocol.Message($"The match of {PadName(pad)} could not start: {e.Message} Trying again in {RetrySeconds:F0} s."));
            }

            return;
        }

        _matches.Add(match);
        pad.MatchesRunning++;
        pad.StartingUntil = now + StartingSeconds;
        _hub.LogLine($"{pad.Config.Id}: match started on UDP port {match.Port}: {HubMatchLauncher.Describe(request)}");
        foreach (HubQueueEntry e in lineup)
        {
            var offer = new HubMatchOffer(pad.Config.Id, match.Port, e.Side, request.Password, pad.Config.Map, pad.Config.Mode, Config.MatchHost);
            Send(e.Player.Slot, HubProtocol.Match(offer));
            e.Player.DepartedAt = now;
            e.Player.Pad = null;
            e.Player.QueueLine = string.Empty;
            pad.Queue.Remove(e);
        }

        _hub.Say($"{PadName(pad)}: {pad.Config.Title} {pad.Config.Format} is starting with {lineup.Count} player(s).");
        PadChanged?.Invoke(pad);
    }

    private void DrainMatches()
    {
        for (int i = _matches.Count - 1; i >= 0; i--)
        {
            IHubMatch m = _matches[i];
            while (m.TryTakeLog(out string line))
            {
                _hub.LogLine($"{m.Request.Pad.Id} match: {line}");
            }

            if (m.Status is HubMatchStatus.Ended or HubMatchStatus.Failed)
            {
                _hub.LogLine($"{m.Request.Pad.Id}: match on port {m.Port} {(m.Status == HubMatchStatus.Ended ? "ended" : "failed: " + m.Failure)}");
                _matches.RemoveAt(i);
                if (Find(m.Request.Pad.Id) is { } pad)
                {
                    pad.MatchesRunning = Math.Max(0, pad.MatchesRunning - 1);
                }

                m.Dispose();
            }
        }
    }

    /// <summary>The pad's sign and, when it changed, its <c>hub pad</c> line to everyone.</summary>
    private void Publish(HubPad pad, float now)
    {
        HubPadInfo info = Info(pad, now);

        // The sign shows more than the line carries (who is ready, who is next), so it is set every tick; HubSigns
        // only sends it on when its text changed.
        _hub.Signs.Set("Pad." + pad.Config.Id, pad.Area.Anchor, SignText(pad, info), Colour(pad.State), 1f, pad.Area.SignFace);
        string line = HubProtocol.Pad(info);
        if (line == pad.LastLine)
        {
            return;
        }

        bool configChanged = pad.LastLine.Length == 0;
        pad.LastLine = line;
        _hub.Host.Broadcast(line);
        if (configChanged)
        {
            PadChanged?.Invoke(pad);
        }
    }

    private HubPadInfo Info(HubPad pad, float now = float.NaN)
    {
        float t = float.IsNaN(now) ? _hub.World.TimeSeconds : now;
        int seconds = pad.CountdownEnd >= 0f ? Math.Max(0, (int)MathF.Ceiling(pad.CountdownEnd - t)) : 0;
        HubPadConfig c = pad.Config;
        return new HubPadInfo
        {
            Id = c.Id, State = pad.State, Seconds = seconds, Map = c.Map, Mode = c.Mode, TeamSize = c.TeamSize,
            Spies = pad.Spies, SpySlots = c.SpySlots, Mercs = pad.Mercs, MercSlots = c.MercSlots,
            LoX = pad.Area.LoX, LoY = pad.Area.LoY, HiX = pad.Area.HiX, HiY = pad.Area.HiY, FloorZ = pad.Area.FloorZ,
            Title = c.Title.Length > 0 ? c.Title : c.Map,
        };
    }

    /// <summary>
    /// The sign over a pad, as every client draws it: "PAD 1", the map and the format, the lineup, the state.
    /// <code>
    /// PAD 1
    /// Enhanced Steel Squat - Story 2v2
    /// Spies 1/2   Mercs 2/2
    /// Starting in 7...
    /// </code>
    /// </summary>
    public static string SignText(HubPad pad, HubPadInfo info)
    {
        HubPadConfig c = pad.Config;
        var sb = new StringBuilder();
        sb.Append("PAD ").Append(pad.Number.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(info.Title).Append(" - ").Append(c.Format).Append('\n');
        if (info.State == HubPadState.Off)
        {
            sb.Append("Not available: this hub does not have the map");
            return sb.ToString();
        }

        sb.Append(c.MercsOnly
            ? string.Create(CultureInfo.InvariantCulture, $"Players {info.Mercs}/{info.MercSlots}")
            : string.Create(CultureInfo.InvariantCulture, $"Spies {info.Spies}/{info.SpySlots}   Mercs {info.Mercs}/{info.MercSlots}"));
        if (pad.InLineup > 0)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"   Ready {pad.ReadyCount}/{pad.InLineup}"));
        }

        int waiting = pad.Queue.Count - pad.InLineup;
        if (waiting > 0)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"   (+{waiting} next)"));
        }

        sb.Append('\n');
        sb.Append(info.State switch
        {
            HubPadState.Countdown => string.Create(CultureInfo.InvariantCulture, $"Starting in {info.Seconds}..."),
            HubPadState.Starting => "Match starting!",
            _ => Missing(pad, c),
        });
        if (pad.MatchesRunning > 0 && info.State != HubPadState.Starting)
        {
            sb.Append('\n').Append(pad.MatchesRunning == 1 ? "1 match playing" : $"{pad.MatchesRunning} matches playing");
        }

        return sb.ToString();
    }

    /// <summary>"Stand here to play", "Waiting for 1 merc", "Waiting for 2 more (bots fill the rest)".</summary>
    private static string Missing(HubPad pad, HubPadConfig c)
    {
        if (pad.Queue.Count == 0)
        {
            return "Stand here to play";
        }

        if (pad.InLineup >= c.Needed && !pad.AllReady)
        {
            int unready = pad.InLineup - pad.ReadyCount;
            return unready == 1 ? "Waiting for 1 player to ready up (U)" : $"Waiting for {unready} players to ready up (U)";
        }

        if (c.Needed < c.Slots)
        {
            int more = c.Needed - pad.InLineup;
            return string.Create(CultureInfo.InvariantCulture, $"Waiting for {more} more (bots fill the rest)");
        }

        int spies = c.SpySlots - pad.Spies;
        int mercs = c.MercSlots - pad.Mercs;
        var parts = new List<string>(2);
        if (spies > 0)
        {
            parts.Add(spies == 1 ? "1 spy" : $"{spies} spies");
        }

        if (mercs > 0)
        {
            parts.Add(c.MercsOnly ? (mercs == 1 ? "1 player" : $"{mercs} players") : mercs == 1 ? "1 merc" : $"{mercs} mercs");
        }

        return "Waiting for " + string.Join(" and ", parts);
    }

    private static uint Colour(HubPadState state) => state switch
    {
        HubPadState.Waiting => WaitingColour,
        HubPadState.Countdown => CountdownColour,
        HubPadState.Starting => StartingColour,
        HubPadState.Off => OffColour,
        _ => IdleColour,
    };

    /// <summary>The phrase with its first letter in lower case (it follows a comma in the queue line).</summary>
    private static string FirstLower(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string PadName(HubPad pad) => "Pad " + pad.Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The player's own <c>hub queue</c> line, when it changed.</summary>
    private void SendQueueLine(HubPlayer p, float now)
    {
        string line;
        if (p.Pad is not { } pad || pad.Queue.Find(e => ReferenceEquals(e.Player, p)) is not { } entry)
        {
            line = HubProtocol.Queue(null, -1, string.Empty);
        }
        else
        {
            HubPadConfig c = pad.Config;
            string side = entry.Side switch { 0 => " as a spy", 1 => c.MercsOnly ? string.Empty : " as a merc", _ => string.Empty };
            string count = string.Create(CultureInfo.InvariantCulture, $"{pad.InLineup}/{c.Slots}");
            string text;
            if (entry.Side < 0)
            {
                int place = 0;
                foreach (HubQueueEntry e in pad.Queue)
                {
                    if (e.Side < 0)
                    {
                        place++;
                    }

                    if (ReferenceEquals(e, entry))
                    {
                        break;
                    }
                }

                text = string.Create(CultureInfo.InvariantCulture, $"On {PadName(pad)}: this match is full ({count}); you are next in line (#{place})");
            }
            else if (pad.State == HubPadState.Countdown)
            {
                int s = Math.Max(0, (int)MathF.Ceiling(pad.CountdownEnd - now));
                text = string.Create(CultureInfo.InvariantCulture, $"Queued on {PadName(pad)}{side}: {count}, starting in {s} s");
            }
            else
            {
                string you = entry.Ready ? "you are ready (U to unready)" : "press U to ready up";
                text = $"Queued on {PadName(pad)}{side}: {count}, {you}; {FirstLower(Missing(pad, c))}";
            }

            line = HubProtocol.Queue(c.Id, entry.Side, text);
        }

        if (line != p.QueueLine)
        {
            p.QueueLine = line;
            Send(p.Slot, line);
        }
    }

    private static void Send(PlayerSlot slot, string line) => slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(line));
}
