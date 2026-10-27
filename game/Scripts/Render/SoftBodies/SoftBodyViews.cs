using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Audio;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.SoftBodies;

/// <summary>
/// The map's soft bodies (ESBPatchActor cloth and ESBStripDoorActor strip curtains): each one a mesh whose vertices
/// follow its <see cref="SoftBodySim"/>, stepped on this machine only. Pawns push them aside (collActor), rifle
/// tracers through them push the hit cell, and they play their collSound and shotSound. Drawn fullbright, as
/// UESoftBody::Render makes no lighting calls. docs/RENDERING.md "Soft bodies".
/// </summary>
public sealed class SoftBodyViews
{
    private readonly Node3D _root;
    private readonly List<Body> _bodies = [];
    private readonly List<SoftBodyCollider> _colliders = [];
    private readonly HashSet<SFireTrace> _traces = [];
    private readonly List<SFireTrace> _gone = [];
    private float _bank;

    internal SoftBodyViews(Node3D parent)
    {
        _root = new Node3D { Name = "SoftBodies" };
        parent.AddChild(_root);
    }

    /// <summary>Soft bodies drawn.</summary>
    public int Count => _bodies.Count;

    /// <summary>Steps run so far (all bodies together).</summary>
    public long Steps { get; private set; }

    /// <summary>
    /// Adds the soft body of a placed instance: <paramref name="prim"/> is its library mesh, the body at rest with
    /// vertex i (and i + N for the back face) on particle i. False when the block or the mesh does not match, and
    /// the instance is then drawn as a plain mesh.
    /// </summary>
    internal bool Add(string actor, JsonElement softBody, in Transform3D transform, FVector location, MeshPrimitive[] prims,
        MaterialCompiler compiler, Vector4 vision)
    {
        if (prims.Length != 1 || SoftBodySim.Read(softBody) is not { } sim || prims[0].VertexCount != sim.Count * 2)
        {
            return false;
        }

        MeshPrimitive prim = prims[0];
        Material? material = compiler.Get(prim.Material, LightSource.None);
        var body = new Body(actor, sim, transform, location, prim, material);
        if (body.Front.Count == 0)
        {
            return false;
        }

        body.Node.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, vision);
        _root.AddChild(body.Node);
        body.Rebuild();
        body.Notifier.Aabb = body.Mesh.GetAabb().Grow(UeSpace.LengthToGodot(SoftBodyK.PositionClamp) * 0.25f);
        body.Node.AddChild(body.Notifier);
        _bodies.Add(body);
        return true;
    }

    /// <summary>
    /// Each frame: banks <paramref name="dt"/> and, once a step's worth (1/120 s) is in, steps every body drawn in the
    /// last 5 s with the banked time against the pawns of <paramref name="level"/>. Tracers that cross a body push it.
    /// </summary>
    public void Update(float dt, Level? level, SoundSystem? sound)
    {
        if (_bodies.Count == 0)
        {
            return;
        }

        float now = level?.TimeSeconds ?? 0f;
        if (level is not null)
        {
            TrackTracers(level, sound);
        }

        _bank = MathF.Min(_bank + dt, SoftBodyK.MaxStep);
        if (_bank < SoftBodyK.StepInterval)
        {
            return;
        }

        float step = _bank;
        _bank = 0f;
        foreach (Body body in _bodies)
        {
            if (body.Notifier.IsOnScreen())
            {
                body.LastSeen = now;
                body.Seen = true;
            }

            if (!body.Seen || now - body.LastSeen > SoftBodyK.DrawnRecently)
            {
                continue;
            }

            Colliders(body, level);
            if (body.Sim.Step(step, _colliders) && now > body.LastCollSound + SoftBodyK.CollSoundInterval)
            {
                body.LastCollSound = now;
                Play(sound, body.Sim.CollSound, body.Location);
            }

            body.Rebuild();
            Steps++;
        }
    }

    /// <summary>The pawns near <paramref name="body"/> in its local space (slot 0x94, 0x1099a850).</summary>
    [Provenance(ProvenanceKind.Decoded, "0x1099a850: actors overlapping the body's bounds, tested at Location + Velocity * 0.08 with their CollisionRadius and CollisionHeight")]
    [Provenance(ProvenanceKind.Estimated, "Only pawns with bCollideActors are taken (the original takes any actor whose two collision flags at +0x2f0 are set)")]
    private void Colliders(Body body, Level? level)
    {
        _colliders.Clear();
        if (level is null || !body.Sim.CollActor)
        {
            return;
        }

        foreach (Pawn p in level.AllActors<Pawn>())
        {
            if (p.bDeleteMe || !p.bCollideActors)
            {
                continue;
            }

            FVector local = body.ToLocal(p.Location + (p.Velocity * SoftBodyK.ColliderLookAhead));
            float reach = p.CollisionRadius + p.CollisionHeight;
            if (local.X < body.Min.X - reach || local.X > body.Max.X + reach || local.Y < body.Min.Y - reach ||
                local.Y > body.Max.Y + reach || local.Z < body.Min.Z - reach || local.Z > body.Max.Z + reach)
            {
                continue;
            }

            _colliders.Add(new SoftBodyCollider(local, body.DirectionToLocal(p.Velocity), p.CollisionRadius, p.CollisionHeight,
                p.Velocity.SizeSquared() > 0f));
        }
    }

    /// <summary>
    /// SBBulletHit / BulletWentTru for the rifle tracers this machine draws: a tracer's line from the muzzle to its
    /// impact that crosses a body pushes the cell it crosses and plays shotSound.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Shots are seen through their SFireTrace tracers only (a share of the rifle's rounds); the push takes the shot's direction at full BulletHitFactor length, as the original sets the momentum's length to it")]
    private void TrackTracers(Level level, SoundSystem? sound)
    {
        _gone.Clear();
        foreach (SFireTrace t in _traces)
        {
            if (t.bDeleteMe)
            {
                _gone.Add(t);
            }
        }

        foreach (SFireTrace t in _gone)
        {
            _traces.Remove(t);
        }

        Span<int> corners = stackalloc int[4];
        foreach (SFireTrace t in level.AllActors<SFireTrace>())
        {
            if (t.bDeleteMe || t.ImpactLoc.IsZero() || !_traces.Add(t))
            {
                continue;
            }

            FVector dir = t.ImpactLoc - t.Location;
            foreach (Body body in _bodies)
            {
                FVector a = body.ToLocal(t.Location);
                FVector b = body.ToLocal(t.ImpactLoc);
                if (body.Sim.LineCheck(a, b, body.Front, corners, out _))
                {
                    body.Sim.BulletHit(body.DirectionToLocal(dir), corners);
                    Play(sound, body.Sim.ShotSound, body.Location);
                }
            }
        }
    }

    private static void Play(SoundSystem? sound, string path, FVector location)
    {
        if (sound is not null && path.Length > 0)
        {
            sound.PlaySound(0, path, 0, false, location, 1f, SoftBodyK.SoundRadius, 1f);
        }
    }

    private sealed class Body
    {
        private readonly int _n;
        private readonly Vector3[] _verts;
        private readonly Vector3[] _normals;
        private readonly Vector2[] _uvs;
        private readonly int[] _indices;
        private readonly Material? _material;
        private readonly Transform3D _toLocal;

        public Body(string actor, SoftBodySim sim, in Transform3D transform, FVector location, MeshPrimitive prim, Material? material)
        {
            Sim = sim;
            Location = location;
            _n = sim.Count;
            _material = material;
            _toLocal = transform.AffineInverse();
            _verts = new Vector3[_n * 2];
            _normals = new Vector3[_n * 2];
            _uvs = new Vector2[_n * 2];
            for (int i = 0; i < _n * 2; i++)
            {
                _uvs[i] = prim.Uvs is null ? Vector2.Zero : new Vector2(prim.Uvs[i * 2], prim.Uvs[(i * 2) + 1]);
            }

            // glTF triangles are counter-clockwise, Godot's front faces clockwise (as SurfaceBuilder swaps them).
            _indices = new int[prim.Indices.Length];
            for (int t = 0; t + 2 < prim.Indices.Length; t += 3)
            {
                _indices[t] = prim.Indices[t];
                _indices[t + 1] = prim.Indices[t + 2];
                _indices[t + 2] = prim.Indices[t + 1];
                if (prim.Indices[t] < _n && prim.Indices[t + 1] < _n && prim.Indices[t + 2] < _n)
                {
                    Front.Add((prim.Indices[t], prim.Indices[t + 1], prim.Indices[t + 2]));
                }
            }

            Mesh = new ArrayMesh { ResourceName = "SoftBody_" + actor };
            Node = new MeshInstance3D { Name = actor.Length > 0 ? actor : "SoftBody", Mesh = Mesh, Transform = transform };
            Notifier = new VisibleOnScreenNotifier3D { Name = "Seen" };
            Min = new FVector(float.MaxValue, float.MaxValue, float.MaxValue);
            Max = new FVector(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < _n; i++)
            {
                FVector p = sim.Position(i);
                Min = new FVector(MathF.Min(Min.X, p.X), MathF.Min(Min.Y, p.Y), MathF.Min(Min.Z, p.Z));
                Max = new FVector(MathF.Max(Max.X, p.X), MathF.Max(Max.Y, p.Y), MathF.Max(Max.Z, p.Z));
            }

            // The rest bounds grow by the strip length: a free edge swings that far at most.
            float swing = MathF.Max(Max.X - Min.X, Max.Z - Min.Z);
            Min -= new FVector(0f, swing, 0f);
            Max += new FVector(0f, swing, 0f);
        }

        public SoftBodySim Sim { get; }

        public FVector Location { get; }

        public ArrayMesh Mesh { get; }

        public MeshInstance3D Node { get; }

        public VisibleOnScreenNotifier3D Notifier { get; }

        /// <summary>Front-face triangles in the original's order (glTF winding), particle indices.</summary>
        public List<(int A, int B, int C)> Front { get; } = [];

        /// <summary>Local bounds the pawns are gathered in (rest bounds widened by the body's swing).</summary>
        public FVector Min { get; }

        public FVector Max { get; }

        public float LastSeen { get; set; }

        public bool Seen { get; set; }

        public float LastCollSound { get; set; } = float.MinValue;

        public FVector ToLocal(FVector world) => UeSpace.ToUe(_toLocal * UeSpace.ToGodot(world));

        public FVector DirectionToLocal(FVector world) => UeSpace.DirectionToUe(_toLocal.Basis * UeSpace.DirectionToGodot(world));

        /// <summary>Writes the particles into the mesh (both faces) with normals from the front triangles.</summary>
        public void Rebuild()
        {
            for (int i = 0; i < _n; i++)
            {
                FVector p = Sim.Position(i);
                Vector3 g = UeSpace.ToGodot(p);
                _verts[i] = g;
                _verts[i + _n] = g;
                _normals[i] = Vector3.Zero;
            }

            for (int t = 0; t + 2 < _indices.Length; t += 3)
            {
                int a = _indices[t], b = _indices[t + 1], c = _indices[t + 2];
                if (a >= _n || b >= _n || c >= _n)
                {
                    continue;
                }

                Vector3 n = (_verts[c] - _verts[a]).Cross(_verts[b] - _verts[a]);
                _normals[a] += n;
                _normals[b] += n;
                _normals[c] += n;
            }

            for (int i = 0; i < _n; i++)
            {
                Vector3 n = _normals[i].LengthSquared() > 1e-12f ? _normals[i].Normalized() : Vector3.Up;
                _normals[i] = n;
                _normals[i + _n] = -n;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = _verts;
            arrays[(int)Godot.Mesh.ArrayType.Normal] = _normals;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = _uvs;
            arrays[(int)Godot.Mesh.ArrayType.Index] = _indices;
            Mesh.ClearSurfaces();
            Mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            if (_material is not null)
            {
                Mesh.SurfaceSetMaterial(0, _material);
            }
        }
    }
}
