using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net;

/// <summary>What the interest layer decided for one actor and one viewer in a server pass.</summary>
/// <param name="Cull">Treat the actor as not relevant to this viewer (its channel closes after the relevancy timeout).</param>
/// <param name="MinUpdateSeconds">Seconds that must pass after the channel's last update before it may update again (0: every pass).</param>
public readonly record struct NetInterestDecision(bool Cull, float MinUpdateSeconds)
{
    /// <summary>No change to the stock pass.</summary>
    public static readonly NetInterestDecision Full = new(false, 0f);
}

/// <summary>
/// Interest management on top of the UE2 relevancy and update rules: an OpenSCCT addition for sessions far larger
/// than the original's (the hub, spec/additions/hub.md). A driver without one (<see cref="NetDriverSettings.Interest"/>
/// null, the default) runs the stock pass unchanged. With one, the server pass asks it about every candidate after the
/// owner test and before the frequency gate: a culled actor counts as not relevant, and an actor whose channel updated
/// less than <see cref="NetInterestDecision.MinUpdateSeconds"/> ago waits for a later pass (its changes stay dirty and go
/// then). Other players' pawn position samples are spaced by <see cref="ProxySampleScale"/>.
/// </summary>
public interface INetInterest
{
    /// <summary>The decision for <paramref name="actor"/> seen by <paramref name="viewer"/>; <paramref name="open"/> is true when its channel is open.</summary>
    NetInterestDecision Evaluate(Actor actor, in NetViewer viewer, bool open);

    /// <summary>How much farther apart than the decoded schedule this viewer's position samples of <paramref name="pawn"/> go (1: as decoded).</summary>
    float ProxySampleScale(Actor pawn, in NetViewer viewer);
}

/// <summary>One distance band: actors at least <see cref="Distance"/> from the viewer update at most every <see cref="MinUpdateSeconds"/>.</summary>
public readonly record struct InterestBand(float Distance, float MinUpdateSeconds, float SampleScale);

/// <summary>Settings of <see cref="DistanceInterest"/>. Distances in uu from the viewer's view point.</summary>
public sealed record DistanceInterestSettings
{
    /// <summary>
    /// The bands, nearest first. Below the first band's distance an actor replicates as in the stock pass. The defaults
    /// are the hub's (docs/HUB.md): full rate within 2500 uu (the engine's always-relevant radius), then slower.
    /// </summary>
    public IReadOnlyList<InterestBand> Bands { get; init; } =
    [
        new InterestBand(2500f, 0.5f, 10f),
        new InterestBand(4500f, 0.75f, 14f),
        new InterestBand(6500f, 1.0f, 18f),
    ];

    /// <summary>Beyond this an actor that has no channel to the viewer is culled.</summary>
    public float CullDistance { get; init; } = 7000f;

    /// <summary>An open channel is kept until the actor is this far (hysteresis, so a walker on the edge does not flicker).</summary>
    public float KeepDistance { get; init; } = 8000f;

    /// <summary>Actors that are always relevant (the game state, the players' replication infos) update at most this often.</summary>
    public float AlwaysRelevantMinSeconds { get; init; } = 0.5f;

    /// <summary>
    /// Inside the first band, only this many of the viewer's nearest other pawns (and what they carry) replicate at the
    /// full rate; the rest of a crowd within the band goes at the first band's pace. 0 gives every pawn inside the
    /// first band the full rate.
    /// </summary>
    public int FullRatePawns { get; init; } = 4;
}

/// <summary>
/// Interest by distance: the actor's anchor (the pawn that carries, owns or bases it, else the actor) against the
/// viewer's view point. Actors the viewer owns, its own pawn and controller, and level actors (bStatic or bNoDelete)
/// are never culled; always-relevant actors are only spaced. Counts what it did in <see cref="Culled"/> and
/// <see cref="Throttled"/> (decisions that culled or spaced an actor; the driver counts the passes they skipped).
/// </summary>
public sealed class DistanceInterest : INetInterest
{
    private readonly float _cull2;
    private readonly float _keep2;
    private readonly float _near2;
    private readonly InterestBand[] _bands;
    private readonly Dictionary<int, Nearest> _nearest = new();
    private readonly List<(float D2, int Id)> _scratch = new();
    private readonly List<int> _stale = new();

    /// <summary>The ids of one viewer's nearest pawns inside the first band, at one level time.</summary>
    private sealed class Nearest
    {
        public float Time = float.NegativeInfinity;
        public readonly HashSet<int> Ids = new();
    }

    public DistanceInterest(DistanceInterestSettings? settings = null)
    {
        Settings = settings ?? new DistanceInterestSettings();
        _bands = [.. Settings.Bands];
        for (int i = 1; i < _bands.Length; i++)
        {
            if (_bands[i].Distance < _bands[i - 1].Distance)
            {
                throw new ArgumentException("Interest bands must be ordered nearest first.", nameof(settings));
            }
        }

        if (Settings.KeepDistance < Settings.CullDistance)
        {
            throw new ArgumentException("KeepDistance must not be below CullDistance.", nameof(settings));
        }

        _cull2 = Settings.CullDistance * Settings.CullDistance;
        _keep2 = Settings.KeepDistance * Settings.KeepDistance;
        _near2 = _bands.Length > 0 ? _bands[0].Distance * _bands[0].Distance : float.PositiveInfinity;
    }

    public DistanceInterestSettings Settings { get; }

    /// <summary>Decisions that culled an actor.</summary>
    public long Culled { get; private set; }

    /// <summary>Decisions that spaced an actor's updates.</summary>
    public long Throttled { get; private set; }

    public NetInterestDecision Evaluate(Actor actor, in NetViewer viewer, bool open)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (NetRelevancy.IsOwnedBy(actor, viewer.Viewer) || NetRelevancy.IsOwnedBy(actor, viewer.RealViewer) ||
            ReferenceEquals(actor, viewer.Viewer) || ReferenceEquals(actor.Instigator, viewer.Viewer))
        {
            return NetInterestDecision.Full;
        }

        if (actor.bAlwaysRelevant || actor is LevelInfo)
        {
            if (Settings.AlwaysRelevantMinSeconds <= 0f)
            {
                return NetInterestDecision.Full;
            }

            Throttled++;
            return new NetInterestDecision(false, Settings.AlwaysRelevantMinSeconds);
        }

        Actor anchor = AnchorOf(actor);
        if (ReferenceEquals(anchor, viewer.Viewer) || NetRelevancy.IsOwnedBy(anchor, viewer.RealViewer))
        {
            return NetInterestDecision.Full;
        }

        float d2 = (anchor.Location - viewer.Source).SizeSquared();
        bool levelActor = actor.bStatic || actor.bNoDelete;
        if (!levelActor && d2 > (open ? _keep2 : _cull2))
        {
            // An open channel waits out the relevancy timeout at the farthest band's pace, then closes.
            Culled++;
            return new NetInterestDecision(true, _bands.Length > 0 ? _bands[^1].MinUpdateSeconds : 0f);
        }

        int band = BandOf(anchor, d2, viewer);
        if (band < 0)
        {
            return NetInterestDecision.Full;
        }

        Throttled++;
        return new NetInterestDecision(false, _bands[band].MinUpdateSeconds);
    }

    public float ProxySampleScale(Actor pawn, in NetViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        int band = BandOf(pawn, (pawn.Location - viewer.Source).SizeSquared(), viewer);
        return band < 0 ? 1f : Math.Max(1f, _bands[band].SampleScale);
    }

    /// <summary>
    /// The band of an anchor at <paramref name="d2"/> (squared uu) from the viewer, or -1 for the full rate: beyond the
    /// first band by distance; inside it, a pawn that is not one of the viewer's <see cref="DistanceInterestSettings.FullRatePawns"/>
    /// nearest takes the first band.
    /// </summary>
    private int BandOf(Actor anchor, float d2, in NetViewer viewer)
    {
        for (int i = _bands.Length - 1; i >= 0; i--)
        {
            if (d2 >= _bands[i].Distance * _bands[i].Distance)
            {
                return i;
            }
        }

        if (_bands.Length == 0 || Settings.FullRatePawns <= 0 || anchor is not Pawn)
        {
            return -1;
        }

        return NearestOf(viewer).Contains(anchor.ActorId) ? -1 : 0;
    }

    /// <summary>The viewer's nearest other pawns inside the first band, worked out once per level time.</summary>
    private HashSet<int> NearestOf(in NetViewer viewer)
    {
        Actor key = viewer.RealViewer;
        Level? level = key.XLevel;
        float now = level?.TimeSeconds ?? 0f;
        if (!_nearest.TryGetValue(key.ActorId, out Nearest? n))
        {
            Prune(now);
            n = new Nearest();
            _nearest.Add(key.ActorId, n);
        }

        if (n.Time == now)
        {
            return n.Ids;
        }

        n.Time = now;
        n.Ids.Clear();
        if (level is null)
        {
            return n.Ids;
        }

        _scratch.Clear();
        foreach (Pawn p in level.DynamicActors<Pawn>())
        {
            if (p.bDeleteMe || ReferenceEquals(p, viewer.Viewer) || NetRelevancy.IsOwnedBy(p, viewer.RealViewer))
            {
                continue;
            }

            float d2 = (p.Location - viewer.Source).SizeSquared();
            if (d2 < _near2)
            {
                _scratch.Add((d2, p.ActorId));
            }
        }

        // Nearest first; equal distances by actor id, so every run picks the same pawns.
        _scratch.Sort(static (a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Id.CompareTo(b.Id));
        int count = Math.Min(Settings.FullRatePawns, _scratch.Count);
        for (int i = 0; i < count; i++)
        {
            n.Ids.Add(_scratch[i].Id);
        }

        return n.Ids;
    }

    /// <summary>Forgets viewers not seen for 10 s (players who left).</summary>
    private void Prune(float now)
    {
        _stale.Clear();
        foreach (KeyValuePair<int, Nearest> kv in _nearest)
        {
            if (now - kv.Value.Time > 10f)
            {
                _stale.Add(kv.Key);
            }
        }

        foreach (int id in _stale)
        {
            _nearest.Remove(id);
        }
    }

    /// <summary>
    /// Where an actor is for interest: a pawn's own location; for anything else the pawn it is based on or owned by
    /// (an attached weapon, an inventory item), a controller's pawn, else the actor itself.
    /// </summary>
    public static Actor AnchorOf(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Actor a = actor;
        for (int depth = 0; depth < 4 && a is not Pawn; depth++)
        {
            if (a.Base is Pawn based)
            {
                return based;
            }

            if (a is Controller { Pawn: { } controlled })
            {
                return controlled;
            }

            if (a.Owner is not { } owner)
            {
                break;
            }

            a = owner;
        }

        return a is Controller { Pawn: { } p } ? p : a;
    }
}
