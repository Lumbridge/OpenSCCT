using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// Server-side lag compensation (Reloaded's rl:net.lagcomp, spec/net/lag-compensation.md, docs/NETCODE.md "Lag
/// compensation"). After every world tick the server records each pawn's hit pills (and where it stood); a combat call
/// from a client carries the server time that client was showing other players (<see cref="ClientSession.ViewTimeFixed"/>),
/// and while the call runs the bone traces it makes (the merc guns, the spy pistol, the shotgun's pellets) see the
/// other pawns at that time, never further back than <see cref="CeilingMs"/>. Nothing is moved: the trace only reads the
/// recorded pills, so the world, the physics and every other actor stay as they are.
/// <para>Off by default; a server setting (<see cref="ServerSettings.LagCompensation"/>, the console's <c>lagcomp</c>
/// and the chat command <c>!lagcomp</c>).</para>
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Our design of Reloaded's server-side lag compensation; only its switch, its ceiling and its messages are observed")]
public sealed class LagCompensation : IPillHistory
{
    /// <summary>The ceiling a fresh server (and <c>!lagcomp reset</c>) uses.</summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded's default ceiling was not decoded; 250 ms covers a 200 ms ping plus the view delay")]
    public const int DefaultCeilingMs = 250;

    /// <summary>The largest ceiling accepted (the history holds a little more than this).</summary>
    public const int MaxCeilingMs = 1000;

    /// <summary>Recorded ticks per pawn: one second at 60 Hz and a few more.</summary>
    private const int Frames = 64;

    private readonly Level _level;
    private readonly Dictionary<SBasePawn, Track> _tracks = new(ReferenceEqualityComparer.Instance);
    private readonly List<SBasePawn> _gone = [];
    private int _ceilingMs = DefaultCeilingMs;
    private bool _enabled;
    private long _targetFixed = -1;

    private LagCompensation(Level level)
    {
        _level = level;
    }

    /// <summary>Installs lag compensation on a server level (off until <see cref="Enabled"/> is set).</summary>
    public static LagCompensation Install(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var lag = new LagCompensation(level);
        level.SetService(lag);
        level.SetService<IPillHistory>(lag);
        return lag;
    }

    /// <summary>Whether clients' shots are traced at their view time. Turning it off forgets the history.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                _tracks.Clear();
            }
        }
    }

    /// <summary>The longest rewind, in milliseconds (0 to <see cref="MaxCeilingMs"/>).</summary>
    public int CeilingMs
    {
        get => _ceilingMs;
        set => _ceilingMs = Math.Clamp(value, 0, MaxCeilingMs);
    }

    /// <summary>Shots traced in the past so far, and the last one's rewind in milliseconds (diagnostics).</summary>
    public long RewoundShots { get; private set; }

    public float LastRewindMs { get; private set; }

    /// <summary>The setting in Reloaded's words: "lagcomp: on - ceiling 250ms" or "lagcomp: off".</summary>
    public string Describe() => _enabled
        ? string.Create(CultureInfo.InvariantCulture, $"lagcomp: on - ceiling {_ceilingMs}ms")
        : "lagcomp: off";

    /// <summary>
    /// Runs a <c>lagcomp</c> command (<c>on</c>, <c>off</c>, <c>reset</c> for the default ceiling, or a ceiling in ms,
    /// which also turns it on); an empty argument only reports. Returns the line to show, or null for a bad argument.
    /// </summary>
    public string? Command(string argument)
    {
        string a = (argument ?? string.Empty).Trim().ToLowerInvariant();
        switch (a)
        {
            case "":
                break;
            case "on" or "1" or "true":
                Enabled = true;
                break;
            case "off" or "0" or "false":
                Enabled = false;
                break;
            case "reset":
                CeilingMs = DefaultCeilingMs;
                break;
            default:
                if (!int.TryParse(a.TrimEnd('m', 's'), NumberStyles.None, CultureInfo.InvariantCulture, out int ms))
                {
                    return null;
                }

                CeilingMs = ms;
                Enabled = true;
                break;
        }

        return Describe();
    }

    /// <summary>Records every pawn's pills for the tick just run (call after each world step).</summary>
    public void Record()
    {
        if (!_enabled)
        {
            return;
        }

        long tick = _level.TickCount;
        IPillPose pose = _level.GetService<IPillPose>() ?? EstimatedPillPose.Instance;
        foreach (Pawn p in _level.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn { bDeleteMe: false } pawn)
            {
                continue;
            }

            if (!_tracks.TryGetValue(pawn, out Track? track))
            {
                track = new Track();
                _tracks[pawn] = track;
            }

            track.Add(tick, pawn, pose);
        }

        // Pawns that are gone (destroyed, not seen this tick) lose their history.
        _gone.Clear();
        foreach ((SBasePawn pawn, Track track) in _tracks)
        {
            if (pawn.bDeleteMe || track.NewestTick != tick)
            {
                _gone.Add(pawn);
            }
        }

        foreach (SBasePawn pawn in _gone)
        {
            _tracks.Remove(pawn);
        }
    }

    /// <summary>
    /// Starts a client's call: its traces see the pawns at <paramref name="ViewTimeFixed"/> (server ticks times
    /// <see cref="NetTimeline.Fraction"/>), at most <see cref="CeilingMs"/> back. Off, without a view time or with
    /// nothing to rewind, the live pose applies.
    /// </summary>
    public void BeginShot(long ViewTimeFixed)
    {
        _targetFixed = -1;
        if (!_enabled || ViewTimeFixed < 0)
        {
            return;
        }

        long nowFixed = _level.TickCount * NetTimeline.Fraction;
        long ceilingFixed = (long)_ceilingMs * Sim.Hz * NetTimeline.Fraction / 1000;
        long rewind = Math.Clamp(nowFixed - ViewTimeFixed, 0, ceilingFixed);
        if (rewind == 0)
        {
            return;
        }

        _targetFixed = nowFixed - rewind;
        RewoundShots++;
        LastRewindMs = rewind * 1000f / (Sim.Hz * NetTimeline.Fraction);
    }

    /// <summary>Ends the call: the live pose again.</summary>
    public void EndShot() => _targetFixed = -1;

    /// <summary>The pills at the shot's time: between the two recorded ticks around it, or the oldest one kept.</summary>
    public bool TryGetPills(SBasePawn Pawn, List<PillShape> Shapes, out FVector Origin)
    {
        Origin = default;
        return _targetFixed >= 0 && _tracks.TryGetValue(Pawn, out Track? track) && track.Sample(_targetFixed, Shapes, out Origin);
    }

    /// <summary>One pawn's recorded ticks, a ring indexed by tick.</summary>
    private sealed class Track
    {
        // A pawn that moved further than this between two ticks teleported (a respawn): no blend across it.
        private const float TeleportDistance = 200f;

        private readonly long[] _ticks = new long[Frames];
        private readonly FVector[] _origins = new FVector[Frames];
        private readonly List<PillShape>[] _pills = new List<PillShape>[Frames];

        public Track()
        {
            Array.Fill(_ticks, -1L);
            for (int i = 0; i < Frames; i++)
            {
                _pills[i] = new List<PillShape>(PillTable.Rows.Length);
            }
        }

        private long _firstTick = -1;

        public long NewestTick { get; private set; } = -1;

        /// <summary>The oldest tick still in the ring (a pawn is recorded every tick, so the ticks are contiguous).</summary>
        private long OldestTick => Math.Max(_firstTick, NewestTick - Frames + 1);

        public void Add(long tick, SBasePawn pawn, IPillPose pose)
        {
            int i = Slot(tick);
            _ticks[i] = tick;
            _origins[i] = pawn.Location;
            _pills[i].Clear();
            pose.GetPills(pawn, _pills[i]);
            NewestTick = tick;
            if (_firstTick < 0)
            {
                _firstTick = tick;
            }
        }

        public bool Sample(long targetFixed, List<PillShape> shapes, out FVector origin)
        {
            origin = default;
            if (NewestTick < 0)
            {
                return false;
            }

            long tick = targetFixed / NetTimeline.Fraction;
            int fraction = (int)(targetFixed % NetTimeline.Fraction);
            if (tick >= NewestTick)
            {
                // Not in the past at all: the live pose.
                return false;
            }

            if (tick < OldestTick)
            {
                tick = OldestTick;
                fraction = 0;
            }

            int a = Slot(tick);
            if (_ticks[a] != tick)
            {
                return false;
            }

            int b = Slot(tick + 1);
            bool blend = fraction > 0 && _ticks[b] == tick + 1 && _pills[b].Count == _pills[a].Count &&
                FVector.Dist(_origins[a], _origins[b]) < TeleportDistance;
            if (!blend)
            {
                origin = _origins[a];
                shapes.AddRange(_pills[a]);
                return true;
            }

            // Recorded ticks blended by the view time's fixed fraction (whole numbers, the same on every run).
            float u = fraction / (float)NetTimeline.Fraction;
            origin = Lerp(_origins[a], _origins[b], u);
            List<PillShape> from = _pills[a];
            List<PillShape> to = _pills[b];
            for (int i = 0; i < from.Count; i++)
            {
                PillShape s = from[i];
                shapes.Add(s with { A = Lerp(s.A, to[i].A, u), B = Lerp(s.B, to[i].B, u) });
            }

            return true;
        }

        private static int Slot(long tick) => (int)(tick % Frames);

        private static FVector Lerp(FVector a, FVector b, float u) => a + (b - a) * u;
    }
}
