using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScctGame.Audio;

public readonly record struct PortalTransmission(float Gain, float HF)
{
    public static PortalTransmission Open => new(1, 1);
}

/// <summary>A real opening polygon between BSP zones, in UE space.</summary>
public sealed class AcousticPortal(int back, int front, FVector normal, FVector[] vertices)
{
    public int Back { get; } = back;
    public int Front { get; } = front;
    public FVector Normal { get; } = normal.SafeNormal();
    public FVector[] Vertices { get; } = vertices;
    public FVector Center { get; } = CenterOf(vertices);
    public float Area { get; }=AreaOf(vertices);
    private static float AreaOf(FVector[] points)
    {
        float area=0;
        for (int i=1;i+1<points.Length;i++) area+=((points[i]-points[0])^(points[i+1]-points[0])).Size()*0.5f;
        return area;
    }
    private static FVector CenterOf(FVector[] points)
    {
        FVector center = default;
        foreach (FVector point in points) center += point;
        return center / Math.Max(1, points.Length);
    }
    public FVector ClosestPoint(FVector point)
    {
        FVector projected = point - Normal * ((point - Center) | Normal);
        bool inside = true;
        FVector closest = Center;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < Vertices.Length; i++)
        {
            FVector a = Vertices[i], edge = Vertices[(i + 1) % Vertices.Length] - a;
            FVector inward = Normal ^ edge;
            if ((inward | (Center - a)) < 0) inward = -inward;
            if ((inward | (projected - a)) < -0.01f) inside = false;
            float t = edge.SizeSquared() > 0 ? Math.Clamp(((projected - a) | edge) / edge.SizeSquared(), 0, 1) : 0;
            FVector candidate = a + edge * t;
            float d = FVector.DistSquared(candidate, point);
            if (d < distance) { closest = candidate; distance = d; }
        }
        return inside ? projected : closest;
    }
}

public readonly record struct AcousticPath(bool Valid, float Distance, float Gain, float HF, int Hops,
    FVector FirstOpening, FVector LastOpening);

/// <summary>
/// Listener-centric, bounded room coupling through actual portal polygons. The
/// existing listener EAX renderer supplies the room's reflected/diffuse field.
/// No gameplay hearing decisions are made here.
/// </summary>
public sealed class AcousticScene
{
    private readonly AcousticPortal[] _portals;
    private readonly Dictionary<string, int> _zones;
    private readonly List<int>[] _links;
    private readonly PortalTransmission[] _transmission;
    private readonly AcousticPath[] _paths;
    private readonly float[] _cost;
    private readonly bool[] _visited;
    private int _nextPortal, _zone = -1;
    private double _nextPaths;
    private double _nextProbes;
    private FVector _listener;
    public int PortalCount => _portals.Length;
    public int ListenerZone => _zone;
    public long PortalQueries { get; private set; }

    public AcousticScene(AcousticPortal[] portals, IReadOnlyDictionary<string, int> zones)
    {
        _portals = portals;
        _zones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int count = 1;
        foreach (var pair in zones) { _zones[pair.Key] = pair.Value; count = Math.Max(count, pair.Value + 1); }
        foreach (AcousticPortal portal in portals) count = Math.Max(count, Math.Max(portal.Back, portal.Front) + 1);
        count = Math.Clamp(count, 1, 256);
        _links = new List<int>[count];
        for (int i = 0; i < count; i++) _links[i] = [];
        _transmission = new PortalTransmission[portals.Length];
        for (int i = 0; i < portals.Length; i++)
        {
            AcousticPortal portal = portals[i];
            if (portal.Back < 0 || portal.Front < 0 || portal.Back >= count || portal.Front >= count) continue;
            _links[portal.Back].Add(i); _links[portal.Front].Add(i);
            // Unknown apertures do not leak sound before their first geometry
            // probe (notably when joining inside behind a closed door).
            _transmission[i] = new PortalTransmission(0, 1);
        }
        _paths = new AcousticPath[count]; _cost = new float[count]; _visited = new bool[count];
    }

    public bool TryZone(string id, out int index) => _zones.TryGetValue(id, out index);
    public AcousticPath PathTo(int zone) => zone >= 0 && zone < _paths.Length ? _paths[zone] : default;

    public int ReverbApertures(int zone,FVector origin,Span<FVector> points,Span<float> weights)
    {
        if (zone<0 || zone>=_links.Length) return 0;
        int count=0;
        foreach (int index in _links[zone])
        {
            AcousticPortal portal=_portals[index];
            FVector delta=portal.Center-origin;
            float w=portal.Area*Math.Abs(delta.SafeNormal()|portal.Normal)/(4*MathF.PI*Math.Max(64*64,delta.SizeSquared()));
            int duplicate=-1;
            for (int i=0;i<count;i++) if (FVector.DistSquared(points[i],portal.Center)<8*8) { duplicate=i; break; }
            if (duplicate>=0) { weights[duplicate]=Math.Max(weights[duplicate],w); continue; }
            if (count<points.Length) { points[count]=portal.Center; weights[count++]=w; }
            else
            {
                int smallest=0;
                for (int i=1;i<count;i++) if (weights[i]<weights[smallest]) smallest=i;
                if (w>weights[smallest]) { points[smallest]=portal.Center; weights[smallest]=w; }
            }
        }
        return count;
    }

    public void Update(int listenerZone, FVector listener, double now, Func<FVector, FVector, PortalTransmission> passage)
    {
        if (listenerZone < 0 || listenerZone >= _links.Length) return;
        // Four aperture probes per update, not per source. The actual collision
        // query sees doors/movers/glass in their current state. Duplicate zone
        // polygons contribute alternative routes, never summed duplicate energy.
        for (int n = 0; now >= _nextProbes && n < Math.Min(4, _portals.Length); n++)
        {
            int index = _nextPortal++ % _portals.Length;
            AcousticPortal portal = _portals[index];
            FVector point = portal.ClosestPoint(listener) * 0.75f + portal.Center * 0.25f;
            _transmission[index] = passage(point + portal.Normal * 16, point - portal.Normal * 16);
            PortalQueries++;
        }
        if (now >= _nextProbes) _nextProbes = now + 1.0 / 60;
        if (_zone == listenerZone && now < _nextPaths && FVector.DistSquared(listener, _listener) < 32 * 32) return;
        _zone = listenerZone; _listener = listener; _nextPaths = now + 0.1;
        Array.Clear(_paths); Array.Clear(_visited); Array.Fill(_cost, float.PositiveInfinity);
        _paths[listenerZone] = new(true, 0, 1, 1, 0, listener, listener); _cost[listenerZone] = 0;
        for (int iteration = 0; iteration < _links.Length; iteration++)
        {
            int selected = -1;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < _cost.Length; i++)
                if (!_visited[i] && _cost[i] < lowest) { lowest = _cost[i]; selected = i; }
            if (selected < 0) break;
            _visited[selected] = true;
            AcousticPath current = _paths[selected];
            if (current.Hops >= 4) continue;
            foreach (int index in _links[selected])
            {
                AcousticPortal portal = _portals[index];
                int next = portal.Front == selected ? portal.Back : portal.Front;
                if (_visited[next]) continue;
                FVector opening = portal.ClosestPoint(current.LastOpening);
                float length = current.Distance + FVector.Dist(current.LastOpening, opening);
                float gain = current.Gain * _transmission[index].Gain * (current.Hops == 0 ? 1 : 0.7f);
                if (length > 6000 || gain < 0.001f) continue;
                float cost = length - MathF.Log(Math.Max(gain, 0.00001f)) * 600;
                if (cost >= _cost[next]) continue;
                _cost[next] = cost;
                _paths[next] = new(true, length, gain, current.HF * _transmission[index].HF * 0.9f,
                    current.Hops + 1, current.Hops == 0 ? opening : current.FirstOpening, opening);
            }
        }
    }

    public static float AmbientGain(AcousticPath path) => !path.Valid ? 0 : path.Hops == 0 ? 1
        : path.Gain * 0.85f / (1 + path.Distance / 650);

    // A room's diffuse field radiates from the opening, rather than falling off
    // a second time from the original tiny point emitter. The camera crossing
    // an open portal starts at unity on both sides of its plane.
    public static float ReflectedGain(AcousticPath path) => !path.Valid ? 0 : path.Hops==0 ? 1
        : path.Gain / (1+path.Distance/650);
}

public static class AmbientTransitions
{
    public static float Approach(float value, float target, float delta) =>
        value + (target - value) * (1 - MathF.Exp(-Math.Max(delta, 0) / 0.35f));
    public static float RangeGain(float distance, float range)
    {
        if (!(range > 0)) return 0;
        float t = Math.Clamp((range - distance) / Math.Max(64, range * 0.2f), 0, 1);
        return t * t * (3 - 2 * t);
    }
    public static float CutGain(float gain, float cutoff)
    {
        float t = Math.Clamp((gain - cutoff * 0.5f) / cutoff, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
