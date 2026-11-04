using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Game.Svm;

/// <summary>
/// The items riding a level's conveyors (spec/additions/conveyors.md#items), stepped one server tick at a time from
/// the conveyors' phases. Items move with their belt and keep their distance: an item stops behind the one in front
/// (a queue builds behind a stopped belt or a busy merge). An open end hands its items to the conveyor its
/// <see cref="Conveyor.Next"/> names where its end meets that one, else they shrink away there; an end nothing feeds
/// brings new items in. The flow starts at tick 0 with the items spread along each belt and is a pure function of the
/// conveyors' phases, so every client and the editor's preview show the same items given the same belt history. It
/// draws only: items never collide with pawns.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: spec/additions/conveyors.md#items")]
public sealed class ConveyorItemFlow
{
    // An end of a conveyor this close (along it) to where another conveyor's end meets it counts as end to end.
    private const float EndJoin = 16f;

    // How far (flat) another conveyor's end may be outside this one's strip, and above or below its surface, to join.
    private const float JoinReach = 32f;

    private readonly Lane[] _lanes;
    private readonly Dictionary<Conveyor, int> _index = new(ReferenceEqualityComparer.Instance);
    private int _tick;
    private bool _started;

    /// <param name="conveyors">The level's conveyors in map order (<see cref="Conveyor.Next"/> links between them count).</param>
    /// <param name="itemLength">
    /// The length an item of each conveyor takes up along the belt, at its <see cref="Conveyor.ItemScale"/> (its mesh's
    /// width): items stop this far apart, centre to centre (the mean of the two lengths for items of two conveyors).
    /// </param>
    public ConveyorItemFlow(IReadOnlyList<Conveyor> conveyors, Func<Conveyor, float> itemLength)
    {
        ArgumentNullException.ThrowIfNull(conveyors);
        ArgumentNullException.ThrowIfNull(itemLength);
        _lanes = new Lane[conveyors.Count];
        for (int i = 0; i < conveyors.Count; i++)
        {
            Conveyor c = conveyors[i];
            _lanes[i] = new Lane(i, c, c.WorldPath, SvmMath.Max(1f, itemLength(c)));
            _index[c] = i;
        }

        foreach (Lane lane in _lanes)
        {
            Link(lane);
        }

        foreach (Lane lane in _lanes)
        {
            lane.Fill();
        }
    }

    /// <summary>The server tick the items are at.</summary>
    public int Tick => _tick;

    /// <summary>Items riding now, for reports.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (Lane lane in _lanes)
            {
                n += lane.Items.Count;
            }

            return n;
        }
    }

    /// <summary>Steps the items up to server tick <paramref name="tick"/> (never back).</summary>
    public void AdvanceTo(int tick)
    {
        if (!_started)
        {
            _started = true;
            foreach (Lane lane in _lanes)
            {
                lane.LastPhase = lane.Conveyor.PhaseAt(_tick);
            }
        }

        while (_tick < tick)
        {
            Step();
        }
    }

    /// <summary>
    /// Every item at <paramref name="tick"/>, a fractional server tick at most <see cref="Tick"/> and less than one
    /// before it: each item between where it was a tick before and where it is now.
    /// </summary>
    public void Place(double tick, List<ConveyorItemPlace> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        float frac = (float)Math.Clamp(tick - (_tick - 1), 0.0, 1.0);
        foreach (Lane lane in _lanes)
        {
            foreach (Item item in lane.Items)
            {
                float along = item.Prev + (item.Along - item.Prev) * frac;
                into.Add(lane.PlaceOf(item, _lanes[item.Origin].Conveyor, along));
            }
        }
    }

    /// <summary>Finds where each conveyor hands its items on and where it is handed items.</summary>
    private void Link(Lane lane)
    {
        if (lane.Path.IsLoop || lane.Path.Length <= 0f || lane.Conveyor.Next is not { } next || ReferenceEquals(next, lane.Conveyor) ||
            !_index.TryGetValue(next, out int target) || _lanes[target].Path.Length <= 0f)
        {
            return;
        }

        Lane to = _lanes[target];
        lane.Path.Evaluate(lane.Path.Length, out FVector end, out _);
        if (!to.Path.Project(end, to.Conveyor.HalfWidth + JoinReach, JoinReach, JoinReach, 0f, out ConveyorProjection at))
        {
            return;
        }

        lane.OutEnd = new Exit(target, at.Along);
        if (!to.Path.IsLoop && at.Along <= EndJoin)
        {
            // End to end: running backwards, the next conveyor hands its items back.
            lane.EndToEnd = true;
            to.OutStart ??= new Exit(Array.IndexOf(_lanes, lane), lane.Path.Length);
        }
    }

    private void Step()
    {
        int tick = ++_tick;
        foreach (Lane lane in _lanes)
        {
            double phase = lane.Conveyor.PhaseAt(tick);
            lane.Move = (float)(phase - lane.LastPhase);
            lane.LastPhase = phase;
            if (lane.Move != 0f)
            {
                lane.Forward = lane.Move > 0f;
            }
        }

        foreach (Lane lane in _lanes)
        {
            foreach (Item item in lane.Items)
            {
                if (item.Stamp != tick)
                {
                    item.Prev = item.Along;
                }
            }

            if (lane.Move != 0f && lane.Path.Length > 0f)
            {
                MoveLane(lane, tick);
                lane.Spawn(tick);
            }
        }
    }

    /// <summary>Moves a conveyor's items front first, each stopping behind the one ahead; the front one may leave.</summary>
    private void MoveLane(Lane lane, int tick)
    {
        List<Item> items = lane.Items;
        float d = lane.Move, length = lane.Path.Length;
        bool forward = d > 0f;
        int n = items.Count;
        // On a loop the front item is held back by the rearmost one, a lap ahead.
        Item? ahead = lane.Path.IsLoop && n > 1 ? items[forward ? 0 : n - 1] : null;
        float wrap = lane.Path.IsLoop ? (forward ? length : -length) : 0f;
        for (int k = 0; k < n; k++)
        {
            int i = forward ? n - 1 - k : k;
            Item item = items[i];
            if (item.Stamp == tick)
            {
                ahead = item;
                wrap = 0f;
                continue;
            }

            item.Stamp = tick;
            float target = item.Along + d;
            if (ahead is not null)
            {
                float gap = Gap(item, ahead);
                float limit = ahead.Along + wrap - (forward ? gap : -gap);
                // Never pushed back: items handed on too close only stop.
                target = forward ? SvmMath.Min(target, SvmMath.Max(item.Along, limit)) : SvmMath.Max(target, SvmMath.Min(item.Along, limit));
            }

            wrap = 0f;
            item.Along = target;
            ahead = item;
            if (lane.Path.IsLoop)
            {
                item.Along = ConveyorPath.Wrap(item.Along, length);
                continue;
            }

            if (target > length || target < 0f)
            {
                Exit? exit = forward ? lane.OutEnd : lane.OutStart;
                if (exit is null)
                {
                    item.Gone = true;
                    ahead = null;
                }
                else if (TryHandOn(item, exit.Value, tick))
                {
                    ahead = null;
                }
                else
                {
                    item.Along = SvmMath.Clamp(target, 0f, length);
                }
            }
        }

        items.RemoveAll(static i => i.Gone || i.Moved);
        if (lane.Path.IsLoop)
        {
            items.Sort(static (a, b) => a.Along.CompareTo(b.Along));
        }
    }

    /// <summary>
    /// Puts <paramref name="item"/> on the conveyor <paramref name="exit"/> names, where it joins, when there is room
    /// there and that conveyor would not carry it straight back off.
    /// </summary>
    private bool TryHandOn(Item item, Exit exit, int tick)
    {
        Lane to = _lanes[exit.Lane];
        float length = to.Path.Length;
        if (!to.Path.IsLoop && ((exit.Along >= length - EndJoin && to.Forward && to.Move != 0f) ||
                                (exit.Along <= EndJoin && !to.Forward && to.Move != 0f)))
        {
            return false;
        }

        foreach (Item other in to.Items)
        {
            float apart = SvmMath.Abs(other.Along - exit.Along);
            if (to.Path.IsLoop)
            {
                apart = SvmMath.Min(apart, length - apart);
            }

            if (apart < Gap(item, other))
            {
                return false;
            }
        }

        var moved = new Item(item.Origin, item.Id, item.Length) { Along = exit.Along, Prev = exit.Along, Stamp = tick };
        to.Insert(moved);
        item.Moved = true;
        return true;
    }

    /// <summary>How close two items' centres come: half of each one's length.</summary>
    private static float Gap(Item a, Item b) => (a.Length + b.Length) * 0.5f;

    /// <summary>Where an item leaving through an end goes: a conveyor and the distance along it.</summary>
    private readonly record struct Exit(int Lane, float Along);

    private sealed class Item(int origin, uint id, float length)
    {
        /// <summary>The conveyor it came from: its meshes, size and seed.</summary>
        public int Origin { get; } = origin;

        /// <summary>Its number on that conveyor, which seeds its mesh, turn and spacing.</summary>
        public uint Id { get; } = id;

        /// <summary>The length it takes up along a belt.</summary>
        public float Length { get; } = length;

        public float Along;
        public float Prev;
        public int Stamp = -1;
        public bool Gone;
        public bool Moved;
    }

    private sealed class Lane(int index, Conveyor conveyor, ConveyorPath path, float itemLength)
    {
        private float _sinceSpawn;
        private uint _spawned;

        public Conveyor Conveyor { get; } = conveyor;
        public ConveyorPath Path { get; } = path;
        public float ItemLength { get; } = itemLength;

        /// <summary>Items in order along the path.</summary>
        public List<Item> Items { get; } = [];

        public Exit? OutEnd;
        public Exit? OutStart;

        /// <summary>The end hands on to the start of the next conveyor, so that one hands items back when reversed.</summary>
        public bool EndToEnd;

        public double LastPhase;
        public float Move;
        public bool Forward = true;

        private bool StartIsSource => OutStart is null;

        private bool EndIsSource => !EndToEnd;

        /// <summary>The items at tick 0: spread along the path as the spacing and seed say, none closer than they fit.</summary>
        public void Fill()
        {
            int count = ConveyorLayout.ItemCount(Conveyor, Path);
            float length = Path.Length;
            for (uint i = 0; i < count; i++)
            {
                float at = i * (length / count) + Jitter(i);
                Items.Add(new Item(index, i, ItemLength) { Along = Path.IsLoop ? ConveyorPath.Wrap(at, length) : SvmMath.Clamp(at, 0f, length) });
            }

            Items.Sort(static (a, b) => a.Along.CompareTo(b.Along));
            for (int k = Items.Count - 1; k > 0; k--)
            {
                if (Items[k].Along - Items[k - 1].Along < ItemLength)
                {
                    Items.RemoveAt(k);
                }
            }

            foreach (Item item in Items)
            {
                item.Prev = item.Along;
            }

            _spawned = (uint)count;
        }

        /// <summary>A new item at an end nothing feeds, when the belt has moved one spacing since the last and there is room.</summary>
        public void Spawn(int tick)
        {
            if (Path.IsLoop || Conveyor.ItemMeshes.Count == 0 || Conveyor.ItemSpacing <= 0f || (Forward ? !StartIsSource : !EndIsSource))
            {
                return;
            }

            _sinceSpawn += SvmMath.Abs(Move);
            float spacing = SvmMath.Max(ItemLength, Conveyor.ItemSpacing + Jitter(_spawned));
            if (_sinceSpawn < spacing)
            {
                return;
            }

            float entry = Forward ? 0f : Path.Length;
            float into = SvmMath.Min(_sinceSpawn - spacing, Path.Length);
            Item? nearest = Items.Count == 0 ? null : Forward ? Items[0] : Items[^1];
            if (nearest is not null && SvmMath.Abs(nearest.Along - entry) - into < (ItemLength + nearest.Length) * 0.5f)
            {
                // The queue reaches the entry: the next item waits.
                _sinceSpawn = spacing;
                return;
            }

            _sinceSpawn -= spacing;
            float at = Forward ? into : Path.Length - into;
            Insert(new Item(index, _spawned++, ItemLength) { Along = at, Prev = at, Stamp = tick });
        }

        public void Insert(Item item)
        {
            int i = Items.FindIndex(o => o.Along > item.Along);
            Items.Insert(i < 0 ? Items.Count : i, item);
        }

        /// <summary>An item of <paramref name="origin"/> at <paramref name="along"/> this path, as it is drawn.</summary>
        public ConveyorItemPlace PlaceOf(Item item, Conveyor origin, float along)
        {
            Path.Evaluate(along, out FVector location, out FVector tangent);
            float flat = SvmMath.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
            var belt = new FRotator(SvmMath.Round(SvmMath.Atan2(tangent.Z, flat) * SvmMath.RadToUnr) & 0xFFFF,
                SvmMath.Round(SvmMath.Atan2(tangent.Y, tangent.X) * SvmMath.RadToUnr) & 0xFFFF, 0);
            uint h = ConveyorLayout.Hash((uint)origin.ItemSeed, item.Id);
            int spin = (int)(ConveyorLayout.Unit(ConveyorLayout.Hash(h, 1u)) * origin.ItemYawJitter) & 0xFFFF;
            int mesh = (int)(ConveyorLayout.Hash(h, 2u) % (uint)Math.Max(1, origin.ItemMeshes.Count));
            return new ConveyorItemPlace(location, belt, spin, origin.ItemScale * Fade(along), mesh, origin);
        }

        /// <summary>
        /// 0 to 1 by the distance to an end where items appear or vanish the way the belt runs (over
        /// <see cref="Conveyor.ItemFade"/>); ends that hand items on or are handed them do not shrink them.
        /// </summary>
        private float Fade(float along)
        {
            float fade = Conveyor.ItemFade;
            if (Path.IsLoop || fade <= 0f)
            {
                return 1f;
            }

            bool atStart = Forward ? StartIsSource : OutStart is null;
            bool atEnd = Forward ? OutEnd is null : EndIsSource;
            float near = SvmMath.Min(atStart ? along : float.MaxValue, atEnd ? Path.Length - along : float.MaxValue);
            return SvmMath.Clamp(near / fade, 0f, 1f);
        }

        // Spacing jitter per item from the seed (a quarter of the spacing either way), as the spread at tick 0.
        private float Jitter(uint id) => (ConveyorLayout.Unit(ConveyorLayout.Hash((uint)Conveyor.ItemSeed, id)) - 0.5f) * Conveyor.ItemSpacing * 0.5f;
    }
}
