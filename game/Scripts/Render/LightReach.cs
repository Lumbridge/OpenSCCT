using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>
/// Chooses, per merged map mesh and frame, between six sets of materials. For each of three light states: the full ones,
/// their twins without the spot shadow light term, and their twins without the spot term and the dynamic world light term
/// (<see cref="MaterialCompiler.GetUnreached"/>); each with the thermal and EMF code, or while the vision is neither,
/// without it (<see cref="MaterialCompiler.GetNormalVision"/>). A mesh drops a light term only while no light of that
/// kind that is on can reach any point of its world box: there the term is exactly zero, so the pixels stay the same and
/// the GPU runs a smaller shader. The spot code is the expensive part to have in the shader and spot slots reach few
/// meshes, so most meshes draw without it even where dynamic lights (large radii) reach them. Each light keeps the list
/// of meshes it reaches and works it out again only when it moves or switches; a mesh changes its materials only when its
/// count of reaching spots or of reaching dynamic lights goes to or from zero, or the vision goes to or from thermal or
/// EMF. Runs on <see cref="RenderingServer.FramePreDraw"/>, after every node has moved, every light was written and the
/// frame's vision was set (<see cref="MaterialCompiler.Vision"/>), so the first frame drawn in a vision has its materials.
/// </summary>
/// <remarks>
/// The tests, against what the template reads (svm_world.gdshaderinc):
/// <list type="bullet">
/// <item>a spot slot adds nothing at a point outside its row 8 sphere (svm_spot_slot's first test), and nothing at all
/// while svm_spot_bits leaves it out (<see cref="SpotShadowLight.ReachSphere"/>);</item>
/// <item>a dynamic world light adds nothing at a point as far from it as its radius x svm_dyn_tune.y (svm_one_light's
/// attenuation and ambient are both 0 there), except on a static mesh with a light sphere: there it adds nothing only
/// while its centre is as far as that radius from the sphere (svm_mesh_light), so a mesh is tested by its own box
/// merged with the box around its instances' spheres; a sun (w below 0) reaches everything. A map light counts while it is
/// switched on, whatever its light type does to its brightness, so pulsing, blinking and flickering lights never swap
/// materials; lights spawned during play (flashes) count while they have a row.</item>
/// </list>
/// Anything that cannot be decided (non-finite rows) reaches everything. SVM_REACH_STATS=1 prints the meshes in each
/// state and the lights on, at most once a second.
/// </remarks>
public sealed class LightReach
{
    /// <summary>
    /// The twins of one surface's material, each null where the surface keeps its own material in that state. With the
    /// thermal and EMF code: without the spot term (<see cref="NoSpot"/>), and without the spot and dynamic terms
    /// (<see cref="Neither"/>; the same material where the surface has no dynamic term). Without it, for the other
    /// visions: every term (<see cref="Normal"/>), no spot (<see cref="NormalNoSpot"/>) and neither
    /// (<see cref="NormalNeither"/>).
    /// </summary>
    public readonly record struct Twins(Material? NoSpot, Material? Neither, Material? Normal = null, Material? NormalNoSpot = null,
        Material? NormalNeither = null)
    {
        public bool Any => NoSpot is not null || Neither is not null || Normal is not null || NormalNoSpot is not null
            || NormalNeither is not null;
    }

    /// <summary>A mesh's materials: its light state, plus <see cref="Normal"/> outside thermal and EMF.</summary>
    private enum State : byte
    {
        /// <summary>A spot reaches the mesh (or it has no world box): its own materials.</summary>
        Full,

        /// <summary>No spot reaches, a dynamic light does: the twins without the spot term.</summary>
        NoSpot,

        /// <summary>Neither reaches: the twins without either term.</summary>
        Neither,

        /// <summary>Full outside thermal and EMF: the twins without their code. The light states follow in order.</summary>
        Normal,

        NormalNoSpot,

        NormalNeither,
    }

    private const int States = 6;

    // Float noise between this test and the template's (positions interpolated across triangles, sums in float): far
    // below the size of any light, far above the error.
    private const float Margin = 0.01f;
    private const float RelativeMargin = 1e-4f;

    // One frame in each state, full first.
    private const int WarmFrames = States;

    private static readonly bool Stats = System.Environment.GetEnvironmentVariable("SVM_REACH_STATS") == "1";

    private sealed class Source(bool spot)
    {
        /// <summary>A spot shadow slot; otherwise a dynamic world light.</summary>
        public readonly bool Spot = spot;

        /// <summary>The light as last placed: sphere centre and radius (a sun: w below 0); unused while off.</summary>
        public Vector4 Key;

        public bool On;

        /// <summary>A sun, or a row that cannot be decided: it reaches every mesh.</summary>
        public bool Everywhere;

        public readonly List<int> Hits = [];
    }

    private readonly Node3D _root;
    private readonly WorldLights _lights;
    private readonly List<MeshInstance3D> _nodes = [];
    private readonly List<Rid> _instances = [];

    // Per mesh, per surface: the twins (held so their RIDs stay valid) and, at surface x States + state, the RID drawn
    // in each state, default where the surface keeps its own material.
    private readonly List<Twins[]> _twins = [];
    private readonly List<Rid[]> _rids = [];
    private Aabb[] _boxes = [];
    // Per mesh: the box around the light spheres of its instances in its own space (null without any), and the world
    // box dynamic lights are tested against (the mesh's box merged with it).
    private readonly List<Aabb?> _spheres = [];
    private Aabb[] _dynBoxes = [];
    private bool[] _placed = [];
    private int[] _spotReach = [];
    private int[] _dynReach = [];
    private State[] _state = [];
    private bool[] _marked = [];
    private readonly List<int> _markedList = [];
    private bool _markedAll;
    private int _boxed;
    private int _spotEverywhere;
    private int _dynEverywhere;
    private bool _normal = true;
    private readonly int[] _inState = new int[States];

    private readonly Source[] _spots = new Source[SpotShadowLight.ReachSlots];
    private Source[] _mapLights = [];
    private readonly Source[] _spawned = new Source[WorldLights.MaxSpawned];
    private int _stamp = -1;
    private float _tune = float.NaN;
    private int _warm = WarmFrames;
    private bool _subscribed;
    private ulong _statsAt;

    public LightReach(Node3D root, WorldLights lights)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(lights);
        _root = root;
        _lights = lights;
        for (int i = 0; i < _spots.Length; i++) _spots[i] = new Source(spot: true);
        for (int i = 0; i < _spawned.Length; i++) _spawned[i] = new Source(spot: false);
    }

    /// <summary>Meshes registered with at least one twin.</summary>
    public int Meshes => _nodes.Count;

    /// <summary>Meshes drawing their twins without the spot term this frame.</summary>
    public int NoSpotMeshes => _inState[(int)State.NoSpot] + _inState[(int)State.NormalNoSpot];

    /// <summary>Meshes drawing their twins without the spot and dynamic terms this frame.</summary>
    public int NeitherMeshes => _inState[(int)State.Neither] + _inState[(int)State.NormalNeither];

    /// <summary>
    /// True until every registered mesh has drawn one frame in each state after a build or <see cref="Warm"/>: loading
    /// waits for it (GameplayFxWarmup) behind its cover.
    /// </summary>
    public bool Warming => _subscribed && _warm > 0;

    /// <summary>
    /// Registers a merged map mesh (static: it never moves) with the twins of each surface's material (none for a
    /// surface that keeps its own). A mesh without any twin is left alone.
    /// </summary>
    /// <param name="spheres">The box around the light spheres of the mesh's instances, in the mesh's space (SurfaceBuilder.SphereBounds).</param>
    public void Add(MeshInstance3D node, IReadOnlyList<Twins> twins, Aabb? spheres = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(twins);
        // Headless draws nothing and emits no FramePreDraw, so the registry would never see its map freed: none there.
        if (node.Mesh is not { } mesh || mesh.GetSurfaceCount() != twins.Count || !HasAny(twins)
            || DisplayServer.GetName() == "headless")
        {
            return;
        }

        var held = new Twins[twins.Count];
        var rids = new Rid[twins.Count * States];
        for (int s = 0; s < twins.Count; s++)
        {
            Twins t = held[s] = twins[s];
            int at = s * States;
            rids[at + (int)State.NoSpot] = t.NoSpot?.GetRid() ?? default;
            rids[at + (int)State.Neither] = t.Neither?.GetRid() ?? default;
            rids[at + (int)State.Normal] = t.Normal?.GetRid() ?? default;
            rids[at + (int)State.NormalNoSpot] = t.NormalNoSpot?.GetRid() ?? default;
            rids[at + (int)State.NormalNeither] = t.NormalNeither?.GetRid() ?? default;
        }

        _nodes.Add(node);
        _spheres.Add(spheres);
        _instances.Add(node.GetInstance());
        _twins.Add(held);
        _rids.Add(rids);
        if (!_subscribed)
        {
            _subscribed = true;
            RenderingServer.FramePreDraw += OnPreDraw;
        }
    }

    /// <summary>
    /// Draws every registered mesh in each state for one frame (full, no spot, neither, then each again without the
    /// thermal and EMF code), so the pipelines of all six are made while loading (and instanced, for the meshes out of
    /// view). Those frames draw wrong light where a reaching term is left out: whoever shows the view covers it while
    /// <see cref="Warming"/> (PlayRoot's loading cover). Runs on its own after the map is built; call again when the
    /// shaders' code changes (a late responsive smoke switch).
    /// </summary>
    public void Warm() => _warm = WarmFrames;

    private static bool HasAny(IReadOnlyList<Twins> twins)
    {
        for (int i = 0; i < twins.Count; i++)
        {
            if (twins[i].Any) return true;
        }

        return false;
    }

    private void OnPreDraw()
    {
        if (!GodotObject.IsInstanceValid(_root))
        {
            RenderingServer.FramePreDraw -= OnPreDraw;
            _subscribed = false;
            return;
        }

        if (!_root.IsInsideTree())
        {
            return;
        }

        PlaceBoxes();
        if (_warm > 0)
        {
            // Full first (as built), then each set of twins; the frame after picks per mesh again.
            var state = (State)(WarmFrames - _warm--);
            for (int i = 0; i < _boxed; i++) SetState(i, state);
            _markedAll = true;
            return;
        }

        // Set by this frame's process step (FaithfulView.ApplyVision), before this draw: the meshes change with it.
        bool normal = !FaithfulView.IsSceneVision(MaterialCompiler.Vision);
        if (normal != _normal)
        {
            _normal = normal;
            _markedAll = true;
        }

        float tune = WorldLights.Tune.Y;
        bool retune = !tune.Equals(_tune);
        _tune = tune;
        for (int i = 0; i < _spots.Length; i++)
        {
            Vector4 s = SpotShadowLight.ReachSphere(i);
            // Radius squared 0: the slot is off. NaN rows reach everything (the template's test never rejects them).
            Place(_spots[i], new Vector4(s.X, s.Y, s.Z, s.W > 0f ? MathF.Sqrt(s.W) : s.W), on: !(s.W <= 0f));
        }

        if (retune || _stamp != _lights.PlaceStamp)
        {
            _stamp = _lights.PlaceStamp;
            if (_mapLights.Length != _lights.Count)
            {
                foreach (Source source in _mapLights) Remove(source);
                _mapLights = new Source[_lights.Count];
                for (int i = 0; i < _mapLights.Length; i++) _mapLights[i] = new Source(spot: false);
            }

            for (int i = 0; i < _mapLights.Length; i++)
            {
                Vector4? row = _lights.ReachOf(i);
                Place(_mapLights[i], row is { } p ? DynamicKey(p, tune) : default, row is not null);
            }
        }

        ReadOnlySpan<PackedLight> spawned = _lights.SpawnedRows;
        for (int i = 0; i < _spawned.Length; i++)
        {
            Place(_spawned[i], i < spawned.Length ? DynamicKey(spawned[i].Pos, tune) : default, i < spawned.Length);
        }

        Apply();
        if (Stats) PrintStats();
    }

    /// <summary>SVM_REACH_STATS: the meshes in each state and the lights on, at most once a second.</summary>
    private void PrintStats()
    {
        ulong now = Time.GetTicksMsec();
        if (_statsAt != 0 && now - _statsAt < 1000)
        {
            return;
        }

        _statsAt = now;
        int spots = 0, dynamic = 0;
        foreach (Source source in _spots) spots += source.On ? 1 : 0;
        foreach (Source source in _mapLights) dynamic += source.On ? 1 : 0;
        foreach (Source source in _spawned) dynamic += source.On ? 1 : 0;
        int full = _inState[(int)State.Full] + _inState[(int)State.Normal];
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_REACH meshes={_boxed} full={full} noSpot={NoSpotMeshes} neither={NeitherMeshes} normalVision={_normal} spots={spots} dynamic={dynamic}"));
    }

    /// <summary>
    /// A dynamic light row as the template reaches with it: the radius scaled by svm_dyn_tune.y and kept above 1e-5, or
    /// w below 0 (a sun) when the scaled radius is below 0 or not finite.
    /// </summary>
    private static Vector4 DynamicKey(Vector4 row, float tune)
    {
        float radius = row.W * tune;
        return new Vector4(row.X, row.Y, row.Z, float.IsFinite(radius) && radius >= 0f ? MathF.Max(radius, 1e-5f) : -1f);
    }

    /// <summary>Moves a light (centre and radius; w below 0 or anything not finite reaches everything) or switches it.</summary>
    private void Place(Source source, Vector4 key, bool on)
    {
        if (source.On == on && (!on || source.Key == key))
        {
            return;
        }

        Remove(source);
        source.On = on;
        source.Key = key;
        if (!on)
        {
            return;
        }

        if (!(key.W >= 0f) || !float.IsFinite(key.X) || !float.IsFinite(key.Y) || !float.IsFinite(key.Z) || !float.IsFinite(key.W))
        {
            source.Everywhere = true;
            if ((source.Spot ? _spotEverywhere++ : _dynEverywhere++) == 0) _markedAll = true;
            return;
        }

        int[] counts = source.Spot ? _spotReach : _dynReach;
        Aabb[] boxes = source.Spot ? _boxes : _dynBoxes;
        var centre = new Vector3(key.X, key.Y, key.Z);
        float reach = (key.W * (1f + RelativeMargin)) + Margin;
        float reach2 = reach * reach;
        for (int i = 0; i < _boxed; i++)
        {
            if (DistanceSquared(boxes[i], centre) < reach2)
            {
                source.Hits.Add(i);
                if (counts[i]++ == 0) Mark(i);
            }
        }
    }

    private void Remove(Source source)
    {
        if (!source.On)
        {
            return;
        }

        if (source.Everywhere)
        {
            source.Everywhere = false;
            if ((source.Spot ? --_spotEverywhere : --_dynEverywhere) == 0) _markedAll = true;
        }

        int[] counts = source.Spot ? _spotReach : _dynReach;
        foreach (int i in source.Hits)
        {
            if (--counts[i] == 0) Mark(i);
        }

        source.Hits.Clear();
        source.On = false;
    }

    private static float DistanceSquared(Aabb box, Vector3 p)
    {
        Vector3 end = box.End;
        float dx = p.X < box.Position.X ? box.Position.X - p.X : p.X > end.X ? p.X - end.X : 0f;
        float dy = p.Y < box.Position.Y ? box.Position.Y - p.Y : p.Y > end.Y ? p.Y - end.Y : 0f;
        float dz = p.Z < box.Position.Z ? box.Position.Z - p.Z : p.Z > end.Z ? p.Z - end.Z : 0f;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>
    /// World boxes of the meshes registered since the last frame (the map is in the tree now). New boxes make every
    /// light look at the meshes again.
    /// </summary>
    private void PlaceBoxes()
    {
        int count = _nodes.Count;
        if (_boxed == count)
        {
            return;
        }

        foreach (Source source in _spots) Remove(source);
        foreach (Source source in _mapLights) Remove(source);
        foreach (Source source in _spawned) Remove(source);
        Array.Resize(ref _boxes, count);
        Array.Resize(ref _dynBoxes, count);
        Array.Resize(ref _placed, count);
        Array.Resize(ref _spotReach, count);
        Array.Resize(ref _dynReach, count);
        Array.Resize(ref _marked, count);
        // New meshes start full, as built.
        _inState[(int)State.Full] += count - _state.Length;
        Array.Resize(ref _state, count);
        for (int i = _boxed; i < count; i++)
        {
            MeshInstance3D node = _nodes[i];
            // A mesh outside the tree has no world box: it never takes its twins.
            _placed[i] = node.IsInsideTree() && node.Mesh is not null;
            _boxes[i] = _placed[i] ? node.GlobalTransform * node.Mesh!.GetAabb() : default;
            _dynBoxes[i] = _placed[i] && _spheres[i] is { } spheres ? _boxes[i].Merge(node.GlobalTransform * spheres) : _boxes[i];
        }

        _stamp = -1;
        _boxed = count;
        _markedAll = true;
    }

    private void Mark(int i)
    {
        if (_markedAll || _marked[i]) return;
        _marked[i] = true;
        _markedList.Add(i);
    }

    private State StateOf(int i) =>
        (_spotEverywhere != 0 || _spotReach[i] != 0 ? State.Full
        : _dynEverywhere != 0 || _dynReach[i] != 0 ? State.NoSpot
        : State.Neither) + (_normal ? (byte)State.Normal : (byte)0);

    private void Apply()
    {
        if (_markedAll)
        {
            _markedAll = false;
            for (int i = 0; i < _boxed; i++) SetState(i, StateOf(i));
        }
        else
        {
            foreach (int i in _markedList) SetState(i, StateOf(i));
        }

        foreach (int i in _markedList) _marked[i] = false;
        _markedList.Clear();
    }

    private void SetState(int i, State state)
    {
        // Without a world box only the vision changes.
        if (!_placed[i]) state = state >= State.Normal ? State.Normal : State.Full;
        State was = _state[i];
        if (was == state)
        {
            return;
        }

        _state[i] = state;
        _inState[(int)was]--;
        _inState[(int)state]++;
        // The instance's own override, not the node's: the node keeps reporting the materials it was built with. A
        // surface with the same material in both states (an actor-lit one between its two twins) is left as it is.
        Rid instance = _instances[i];
        Rid[] rids = _rids[i];
        for (int at = 0, s = 0; at < rids.Length; at += States, s++)
        {
            Rid to = rids[at + (int)state];
            if (rids[at + (int)was] != to)
            {
                RenderingServer.InstanceSetSurfaceOverrideMaterial(instance, s, to);
            }
        }
    }
}
