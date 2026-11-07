using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Content.Loading;
using OpenScctGame.Render;
using N2 = System.Numerics.Vector2;

namespace OpenScctGame.Views;

/// <summary>Client-only, shaped pane fragments. Damage, sounds, triggers and collision remain authoritative SVitre behavior.</summary>
public sealed partial class GlassBreakViews : Node3D
{
    private sealed class Pane(string id, SVitre actor, MeshInstance3D node, GlassPaneGeometry geometry)
    {
        public string Id { get; } = id;
        public SVitre Actor { get; } = actor;
        public MeshInstance3D Node { get; } = node;
        public GlassPaneGeometry Geometry { get; } = geometry;
        public bool Broken { get; set; } = IsBroken(actor);
    }
    private sealed class Piece(MeshInstance3D node, Vector3 velocity, Vector3 spin, float delay, Vector3 planeNormal, float restAngle)
    {
        public MeshInstance3D Node { get; } = node;
        public Vector3 Velocity = velocity;
        public Vector3 Spin = spin;
        public float Delay = delay;
        public int Bounces;
        public Vector3 PlaneNormal = planeNormal;
        public float RestAngle = restAngle;
    }
    private sealed class Burst(Node3D root, Material? material)
    {
        public Node3D Root { get; } = root;
        public Material? Material { get; } = material;
        public List<Piece> Pieces { get; } = [];
        public float Age;
    }
    private static readonly ConditionalWeakTable<Level, List<WeakReference<SVitre>>> s_supported = new();
    private readonly List<Pane> _panes = [];
    private readonly List<Burst> _bursts = [];
    private RenderMap? _map;
    public int Started { get; private set; }
    public int LiveFragments { get { int n = 0; foreach (Burst b in _bursts) n += b.Pieces.Count; return n; } }
    public GlassBreakViews() { Name = "GlassFragments"; }

    public void Bind(RenderMap map, MapLoadResult loaded)
    {
        _map = map;
        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is not SVitre actor || !map.Gameplay.TryGetValue(record.Id, out GameplayMesh? mesh)
                || GlassPaneGeometry.Read(mesh.Node.Mesh) is not { } geometry) continue;
            _panes.Add(new Pane(record.Id, actor, mesh.Node, geometry));
            s_supported.GetOrCreateValue(actor.XLevel).Add(new WeakReference<SVitre>(actor));
        }
    }

    public static bool IsBroken(SVitre actor) => actor.bAlreadyDestroyed || actor.DrawType == EDrawType.DT_None || actor.StaticMesh.IsNone;

    /// <summary>Only replace an authored sprite burst when its actual pane geometry is available.</summary>
    public static bool ReplacesEmitter(Level level, Actor emitter)
    {
        if (emitter.GetUcClass().Name != "SVitreEmitter" || !s_supported.TryGetValue(level, out var panes)) return false;
        foreach (var weak in panes)
            if (weak.TryGetTarget(out SVitre? pane) && IsBroken(pane) && FVector.DistSquared(pane.Location, emitter.Location) < 1) return true;
        return false;
    }

    public void Draw(float dt)
    {
        foreach (Pane pane in _panes)
        {
            bool broken = IsBroken(pane.Actor);
            if (broken && !pane.Broken && !pane.Actor.bDeleteMe) Start(pane);
            pane.Broken = broken;
        }
        // Bounded cosmetic work even when an explosion breaks a whole bank of panes.
        while (_bursts.Count > 6) { _bursts[0].Root.QueueFree(); _bursts.RemoveAt(0); }
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            Burst b = _bursts[i]; b.Age += Math.Max(0, dt);
            if (b.Age >= 4.5f) { b.Root.QueueFree(); _bursts.RemoveAt(i); continue; }
            if (b.Material is ShaderMaterial shader) shader.SetShaderParameter("svm_fragment_opacity", Math.Clamp((4.5f - b.Age) / .8f, 0, 1));
            int steps = Math.Clamp((int)MathF.Ceiling(dt * 90), 1, 8); float step = Math.Clamp(dt, 0, .1f) / steps;
            for (int s = 0; s < steps; s++)
            foreach (Piece p in b.Pieces)
            {
                if (b.Age < p.Delay || p.Bounces >= 3) continue;
                Vector3 from = p.Node.Position;
                p.Velocity += Vector3.Down * (9.8f * step);
                Vector3 to = from + p.Velocity * step;
                if (_panes.Count > 0 && GadgetWorld.Collision(_panes[0].Actor.Level) is { } collision
                    && collision.LineCheck(out FCheckResult hit, null, UeSpace.ToUe(to), UeSpace.ToUe(from), FVector.Zero,
                        ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry) && hit.bHit)
                {
                    Vector3 normal = UeSpace.DirectionToGodot(hit.Normal).Normalized();
                    to = UeSpace.ToGodot(hit.Location) + normal * .008f;
                    p.Velocity = p.Velocity.Bounce(normal) * .28f; p.Spin *= .45f; p.Bounces++;
                    if (p.Velocity.LengthSquared() < .015f) p.Bounces = 3;
                    if (p.Bounces >= 3)
                        p.Node.Basis = new Basis(normal,p.RestAngle) * new Basis(new Quaternion(p.PlaneNormal,normal));
                }
                p.Node.Position = to;
                float angle = p.Spin.Length() * step;
                if (p.Bounces < 3 && angle > 1e-7f) p.Node.Basis = new Basis(p.Spin.Normalized(), angle) * p.Node.Basis;
            }
        }
    }

    private void Start(Pane pane)
    {
        Transform3D world = pane.Node.GlobalTransform;
        if (MathF.Abs(world.Basis.Determinant()) < 1e-9f) return;
        Vector3 hit = pane.Actor.GlassImpactSerial > 0 ? UeSpace.ToGodot(pane.Actor.GlassImpactLocation) : world * pane.Geometry.Point(pane.Geometry.Center);
        N2 impact = pane.Geometry.Project(world.AffineInverse() * hit);
        uint seed = 2166136261;
        foreach (char c in pane.Id) seed = (seed ^ c) * 16777619;
        seed ^= (uint)pane.Actor.GlassImpactSerial * 2246822519u;
        List<GlassFracture.Fragment> fragments = GlassFracture.Break(pane.Geometry.Triangles, impact, seed);
        if (fragments.Count == 0) return;
        float Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }
        Vector3 impulse = UeSpace.DirectionToGodot(pane.Actor.GlassImpactDirection).Normalized();
        if (impulse.LengthSquared() < .001f) impulse = (world.Basis * pane.Geometry.Normal).Normalized();
        var root = new Node3D { Name = "BrokenPane", TopLevel = true }; AddChild(root); root.GlobalTransform = Transform3D.Identity;
        Material? material = pane.Geometry.Material?.Duplicate() as Material;
        var burst = new Burst(root, material); _bursts.Add(burst); Started++;
        foreach (GlassFracture.Fragment fragment in fragments)
        {
            Vector3 center = world * pane.Geometry.Point(fragment.Center);
            float distance = center.DistanceTo(hit);
            Vector3 radial = center - hit; radial -= impulse * radial.Dot(impulse);
            if (radial.LengthSquared() > .00001f) radial = radial.Normalized();
            Vector3 scatter = new(Next() - .5f, Next() - .5f, Next() - .5f);
            Vector3 speed = impulse * (1.2f + 2.8f / (1 + distance * 5)) + radial * (.4f + Next() * .8f) + scatter * .6f;
            var node = new MeshInstance3D { Mesh = pane.Geometry.MeshFor(fragment, world, material), Position = center,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = RenderLayers.World };
            root.AddChild(node);
            if (_map is not null) ActorLighting.Apply(node, _map.LightActor(UeSpace.ToUe(center), 12, 0));
            // A small separation reveals the fracture before its larger sheets tumble away.
            node.Scale = Vector3.One * .992f;
            burst.Pieces.Add(new Piece(node, speed, new Vector3(Next() - .5f, Next() - .5f, Next() - .5f) * 18,
                Math.Min(.18f, distance * .075f), (world.Basis * pane.Geometry.Normal).Normalized(), Next() * MathF.Tau));
        }
    }
}
