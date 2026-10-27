using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.SoftBodies;

/// <summary>A pawn as a soft body sees it this step: an upright cylinder in the body's local space.</summary>
internal readonly record struct SoftBodyCollider(FVector Location, FVector Velocity, float Radius, float Height, bool Moving);

/// <summary>
/// One soft body's simulation (UESoftBody / UESBPatch in the game; strip doors use the same code), in the actor's
/// local space, uu. Settings and the rest state come from the converter (scctpkg level.softbody_record), which reads
/// the body the map saved. Purely cosmetic: each machine runs its own and nothing blocks on it (the primitive's
/// point check always passes, 0x1099e980), so pawns walk through and push it aside.
/// docs/RENDERING.md "Soft bodies".
/// </summary>
internal sealed class SoftBodySim
{
    private readonly FVector[] _pos;
    private readonly FVector[] _old;
    private readonly FVector[] _accel;
    private readonly FVector[] _wind;
    private readonly FVector[] _hit;
    private readonly float[] _u;
    private readonly float[] _v;
    private readonly bool[] _fixed;
    private readonly (int A, int B, float Length)[] _links;
    private float _windTimerU;
    private float _windTimerV;
    private float _dt;

    private SoftBodySim(int count, (int, int, float)[] links)
    {
        _pos = new FVector[count];
        _old = new FVector[count];
        _accel = new FVector[count];
        _wind = new FVector[count];
        _hit = new FVector[count];
        _u = new float[count];
        _v = new float[count];
        _fixed = new bool[count];
        _links = links;
    }

    public int Count => _pos.Length;

    public bool IsStrip { get; private init; }

    public int NbU { get; private init; }

    public int NbV { get; private init; }

    public FVector Gravity { get; private init; }

    public FVector WindMin { get; private init; }

    public FVector WindMax { get; private init; }

    public float WindUScale { get; private init; }

    public float WindVScale { get; private init; }

    public float WindUPan { get; private init; }

    public float WindVPan { get; private init; }

    public int NbIter { get; private init; }

    public int NbNormalizeIter { get; private init; }

    public bool CollActor { get; private init; }

    public float EnergyFactor { get; private init; }

    public float HitDamping { get; private init; }

    public int BulletHitFactor { get; private init; }

    public float Mass { get; private init; }

    public string CollSound { get; private init; } = "";

    public string ShotSound { get; private init; } = "";

    public FVector Position(int i) => _pos[i];

    public bool IsFixed(int i) => _fixed[i];

    /// <summary>The settings and rest state of an instance's softBody block, or null when it does not read.</summary>
    public static SoftBodySim? Read(JsonElement body)
    {
        float[] points = Floats(body, "points");
        float[] uvs = Floats(body, "uvs");
        int n = points.Length / 3;
        if (n == 0 || uvs.Length != n * 2)
        {
            return null;
        }

        var links = new List<(int, int, float)>();
        if (body.TryGetProperty("links", out JsonElement ls))
        {
            foreach (JsonElement l in ls.EnumerateArray())
            {
                int a = l[0].GetInt32();
                int b = l[1].GetInt32();
                if (a >= 0 && a < n && b >= 0 && b < n)
                {
                    links.Add((a, b, l[2].GetSingle()));
                }
            }
        }

        var sim = new SoftBodySim(n, [.. links])
        {
            IsStrip = Str(body, "kind") == "strip",
            NbU = Int(body, "nb_u", 1),
            NbV = Int(body, "nb_v", 1),
            Gravity = Vec(body, "gravity"),
            WindMin = Vec(body, "wind_min"),
            WindMax = Vec(body, "wind_max"),
            WindUScale = Float(body, "wind_u_scale"),
            WindVScale = Float(body, "wind_v_scale"),
            WindUPan = Float(body, "wind_u_pan"),
            WindVPan = Float(body, "wind_v_pan"),
            NbIter = Int(body, "nb_iter", 1),
            NbNormalizeIter = Int(body, "nb_normalize_iter", 1),
            CollActor = body.TryGetProperty("coll_actor", out JsonElement ca) && ca.ValueKind == JsonValueKind.True,
            EnergyFactor = Float(body, "energy_factor"),
            HitDamping = Float(body, "hit_damping"),
            BulletHitFactor = Int(body, "bullet_hit_factor", 0),
            Mass = Float(body, "mass"),
            CollSound = Str(body, "coll_sound") ?? "",
            ShotSound = Str(body, "shot_sound") ?? "",
        };
        for (int i = 0; i < n; i++)
        {
            sim._pos[i] = new FVector(points[i * 3], points[(i * 3) + 1], points[(i * 3) + 2]);
            sim._old[i] = sim._pos[i];
            sim._u[i] = uvs[i * 2];
            sim._v[i] = uvs[(i * 2) + 1];
        }

        if (body.TryGetProperty("fixed", out JsonElement fx))
        {
            foreach (JsonElement f in fx.EnumerateArray())
            {
                int i = f.GetInt32();
                if (i >= 0 && i < n)
                {
                    sim._fixed[i] = true;
                }
            }
        }

        return sim;
    }

    /// <summary>
    /// UESoftBody::Update (0x1099a6f0): one step of <paramref name="dt"/> seconds against <paramref name="colliders"/>
    /// (already in local space). Returns true when a moving collider touched the body (CollidedBy, the collSound).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a6f0 and the UESBPatch vtable slots 0x94 to 0xbc it calls in order")]
    public bool Step(float dt, IReadOnlyList<SoftBodyCollider> colliders)
    {
        dt = Math.Clamp(dt, SoftBodyK.MinStep, SoftBodyK.MaxStep);
        _dt = (dt * SoftBodyK.StepSmoothing) + ((1f - SoftBodyK.StepSmoothing) * _dt);
        Wind();
        Forces();
        Integrate();
        bool touched = false;
        for (int it = 0; it < NbIter; it++)
        {
            if (CollActor)
            {
                touched |= Collide(colliders);
            }

            SatisfyLinks();
        }

        for (int i = 0; i < _pos.Length; i++)
        {
            _pos[i] = Clamp(_pos[i]);
            _old[i] = Clamp(_old[i]);
        }

        return touched;
    }

    /// <summary>
    /// ESoftBodyActor.SBBulletHit through UESBPatch (0x10b00c50): the momentum times hitDamping, held to
    /// BulletHitFactor * 100 uu, pushes the hit particles (as a collider's push: divided by the smoothed step).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x10b00c50: momentum * hitDamping, local, length cap BulletHitFactor * 100, then slot 0xc0 on each stored particle")]
    public void BulletHit(FVector localMomentum, ReadOnlySpan<int> particles)
    {
        FVector push = localMomentum * HitDamping;
        float cap = BulletHitFactor * SoftBodyK.BulletHitScale;
        float size = push.Size();
        if (size >= 1e-8f)
        {
            push *= cap / size;
        }

        foreach (int p in particles)
        {
            AddHit(p, push);
        }
    }

    /// <summary>
    /// The particles a segment (local space) passes through: the corners of the first cell or strip quad it crosses,
    /// and the crossing parameter along the segment. False when it misses.
    /// </summary>
    public bool LineCheck(FVector start, FVector end, IReadOnlyList<(int A, int B, int C)> triangles, Span<int> corners, out float time)
    {
        time = 2f;
        int best = -1;
        for (int t = 0; t < triangles.Count; t++)
        {
            (int a, int b, int c) = triangles[t];
            if (SegmentTriangle(start, end, _pos[a], _pos[b], _pos[c], out float s) && s < time)
            {
                time = s;
                best = t;
            }
        }

        if (best < 0)
        {
            return false;
        }

        // Triangles come in pairs, one cell (or strip quad) each: its four corners take the push.
        int first = best & ~1;
        (int a0, int b0, int c0) = triangles[first];
        (int a1, int b1, int c1) = triangles[Math.Min(first + 1, triangles.Count - 1)];
        corners[0] = a0;
        corners[1] = b0;
        corners[2] = c0;
        corners[3] = a1 != a0 && a1 != b0 && a1 != c0 ? a1 : b1 != a0 && b1 != b0 && b1 != c0 ? b1 : c1;
        return true;
    }

    /// <summary>Slot 0x98 (0x1099aa00): each free particle's wind from the level noise at its u, v and the wind timers.</summary>
    private void Wind()
    {
        bool x = WindMax.X != 0f || WindMin.X != 0f;
        bool y = WindMax.Y != 0f || WindMin.Y != 0f;
        bool z = WindMax.Z != 0f || WindMin.Z != 0f;
        FVector span = WindMax - WindMin;
        for (int i = 0; i < _pos.Length; i++)
        {
            if (_fixed[i])
            {
                continue;
            }

            float u = Wrap((_windTimerU + _u[i]) * WindUScale);
            float v = Wrap((_windTimerV + _v[i]) * WindVScale);
            float wx = 0f, wy = 0f, wz = 0f;
            if (x)
            {
                wx = (SoftBodyNoise.Noise2(u, v) + 1f) * 0.5f;
            }

            if (y)
            {
                u = Wrap(u + SoftBodyK.WindYOffsetU);
                v = Wrap(v + SoftBodyK.WindYOffsetV);
                wy = (SoftBodyNoise.Noise2(u, v) + 1f) * 0.5f;
            }

            if (z)
            {
                u = Wrap(u + SoftBodyK.WindZOffsetU);
                v = Wrap(v + SoftBodyK.WindZOffsetV);
                wz = (SoftBodyNoise.Noise2(u, v) + 1f) * 0.5f;
            }

            _wind[i] = new FVector((span.X * wx) + WindMin.X, (span.Y * wy) + WindMin.Y, (span.Z * wz) + WindMin.Z);
        }

        _windTimerU = Wrap((WindUPan * _dt) + _windTimerU);
        _windTimerV = Wrap((WindVPan * _dt) + _windTimerV);
    }

    /// <summary>Slot 0x9c (0x1099b040): acceleration = gravity + (wind + hits) * nbU * nbV / Mass; the hits are spent.</summary>
    private void Forces()
    {
        float scale = Mass != 0f ? NbU * NbV / Mass : 0f;
        for (int i = 0; i < _pos.Length; i++)
        {
            if (_fixed[i])
            {
                continue;
            }

            _accel[i] = ((_hit[i] + _wind[i]) * scale) + Gravity;
            _hit[i] = default;
        }
    }

    /// <summary>
    /// Slot 0xa0 (0x1099b130): Verlet with the smoothed step; the old position then moves EnergyFactor of the way to the
    /// new one, which takes that share of the speed away (0, the class default, keeps all of it).
    /// </summary>
    private void Integrate()
    {
        float dt2 = _dt * _dt;
        for (int i = 0; i < _pos.Length; i++)
        {
            if (_fixed[i])
            {
                continue;
            }

            FVector p = _pos[i];
            FVector next = (p * 2f) - _old[i] + (_accel[i] * dt2);
            FVector old = p + ((next - p) * EnergyFactor);
            _pos[i] = Clamp(next);
            _old[i] = Clamp(old);
        }
    }

    /// <summary>
    /// Slot 0xa8 (0x1099b630) for the actor colliders, with slot 0xac (0x1099b310) per particle: a particle inside a
    /// collider's cylinder leaves by the nearest of its top, its bottom or its side, and takes the collider's velocity
    /// over the smoothed step as a push. The map's bodies have no collision planes, spheres or boxes.
    /// </summary>
    private bool Collide(IReadOnlyList<SoftBodyCollider> colliders)
    {
        bool touched = false;
        for (int c = 0; c < colliders.Count; c++)
        {
            SoftBodyCollider col = colliders[c];
            bool hit = false;
            float top = col.Location.Z + col.Height;
            float bottom = col.Location.Z - col.Height;
            for (int i = 0; i < _pos.Length; i++)
            {
                if (_fixed[i])
                {
                    continue;
                }

                FVector p = _pos[i];
                float dx = p.X - col.Location.X;
                float dy = p.Y - col.Location.Y;
                float d = MathF.Sqrt((dx * dx) + (dy * dy));
                if (p.Z >= top || p.Z <= bottom || d >= col.Radius)
                {
                    continue;
                }

                float side = col.Radius - d;
                float up = top - p.Z;
                float down = p.Z - bottom;
                if (up < side && up < down)
                {
                    p.Z = top;
                }
                else if (down < side && down < up)
                {
                    p.Z = bottom;
                }
                else
                {
                    FVector n = new FVector(dx, dy, 0f).SafeNormal();
                    p.X += n.X * side;
                    p.Y += n.Y * side;
                }

                _pos[i] = Clamp(p);
                AddHit(i, col.Velocity);
                hit = true;
            }

            touched |= hit && col.Moving;
        }

        return touched;
    }

    /// <summary>
    /// Slot 0xb4 (0x1099bd80), nbNormalizeIter passes over the links in order: each link back to its rest length,
    /// split half and half between two free particles, all on the free one when the other is fixed.
    /// </summary>
    private void SatisfyLinks()
    {
        for (int pass = 0; pass < NbNormalizeIter; pass++)
        {
            foreach ((int a, int b, float rest) in _links)
            {
                FVector delta = _pos[b] - _pos[a];
                float len = delta.Size();
                bool fa = _fixed[a];
                bool fb = _fixed[b];
                if (len <= 0f)
                {
                    if (!fa)
                    {
                        _pos[a].X += SoftBodyK.ZeroLinkNudge;
                    }
                    else
                    {
                        _pos[b].X += SoftBodyK.ZeroLinkNudge;
                    }

                    continue;
                }

                if (!fa && !fb)
                {
                    FVector move = delta * ((len - rest) * 0.5f / len);
                    _pos[a] += move;
                    _pos[b] -= move;
                }
                else if (!fa)
                {
                    _pos[a] += delta * ((len - rest) / len);
                }
                else if (!fb)
                {
                    _pos[b] -= delta * ((len - rest) / len);
                }
            }
        }
    }

    /// <summary>Slot 0xc0 (0x1099ca30): a push of <paramref name="velocity"/> over the smoothed step.</summary>
    private void AddHit(int i, FVector velocity)
    {
        if (_dt > 0f && i >= 0 && i < _hit.Length)
        {
            _hit[i] += velocity * (1f / _dt);
        }
    }

    private static float Wrap(float x) => x % SoftBodyK.WindWrap;

    private static FVector Clamp(FVector v)
    {
        const float m = SoftBodyK.PositionClamp;
        return new FVector(Math.Clamp(v.X, -m, m), Math.Clamp(v.Y, -m, m), Math.Clamp(v.Z, -m, m));
    }

    private static bool SegmentTriangle(FVector s, FVector e, FVector a, FVector b, FVector c, out float t)
    {
        t = 0f;
        FVector dir = e - s;
        FVector e1 = b - a;
        FVector e2 = c - a;
        FVector h = dir.Cross(e2);
        float det = e1.Dot(h);
        if (MathF.Abs(det) < 1e-6f)
        {
            return false;
        }

        float inv = 1f / det;
        FVector q = s - a;
        float bu = q.Dot(h) * inv;
        if (bu < 0f || bu > 1f)
        {
            return false;
        }

        FVector r = q.Cross(e1);
        float bv = dir.Dot(r) * inv;
        if (bv < 0f || bu + bv > 1f)
        {
            return false;
        }

        t = e2.Dot(r) * inv;
        return t >= 0f && t <= 1f;
    }

    private static float[] Floats(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement a) || a.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var out_ = new float[a.GetArrayLength()];
        int i = 0;
        foreach (JsonElement x in a.EnumerateArray())
        {
            out_[i++] = x.GetSingle();
        }

        return out_;
    }

    private static FVector Vec(JsonElement e, string name)
    {
        float[] v = Floats(e, name);
        return v.Length == 3 ? new FVector(v[0], v[1], v[2]) : default;
    }

    private static float Float(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : 0f;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement x) && x.ValueKind == JsonValueKind.Number ? (int)x.GetDouble() : fallback;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
}
