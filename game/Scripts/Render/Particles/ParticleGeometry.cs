using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Particles;

/// <summary>The camera the particles face, in UE space (location and unit right, up and forward).</summary>
public readonly record struct ParticleCamera(FVector Location, FVector Right, FVector Up, FVector Forward)
{
    public static ParticleCamera From(FVector location, FRotator rotation)
    {
        rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        return new ParticleCamera(location, y, z, x);
    }
}

/// <summary>
/// One sprite quad: corners in UE space in the order L - A - B, L - A + B, L + A - B, L + A + B; the cell's texture
/// rectangle (u0, v0) to (u1, v1), the next cell's when blending, and the vertex colour (its alpha is the blend weight
/// with BlendBetweenSubdivisions).
/// </summary>
public struct SpriteQuad
{
    public FVector C0;
    public FVector C1;
    public FVector C2;
    public FVector C3;
    public float U0;
    public float V0;
    public float U1;
    public float V1;
    public float BU0;
    public float BV0;
    public float BU1;
    public float BV1;
    public FxColor Color;
}

/// <summary>One spark trail segment (UE space), with its v range along the trail.</summary>
public readonly record struct SparkSegment(FVector From, FVector To, float V0, float V1, FxColor Color);

/// <summary>One mesh particle: where the static mesh is drawn.</summary>
public readonly record struct MeshInstance(FVector Location, FRotator Rotation, FVector Scale, FxColor Color);

/// <summary>
/// What each particle looks like (spec/native/emitters.md rules 17 to 25): the sprite quads of USpriteEmitter's vertex
/// fill, the spark trails, the mesh placements and the beam strips, in UE space. Godot-free.
/// </summary>
[PortOf("eng:USpriteEmitter::FillVertexBuffer", Spec = "spec/native/emitters.md#rules")]
public static class ParticleGeometry
{
    /// <summary>Where a particle is in the world (PTCS_Relative particles live in the owner's frame).</summary>
    public static FVector World(ParticleParams p, FVector location, in ParticleOwner owner) =>
        p.CoordinateSystem == 1 ? owner.Location + ParticleEmitterSim.Rotate(location, owner.Rotation) : location;

    private static FVector WorldDirection(ParticleParams p, FVector direction, in ParticleOwner owner) =>
        p.CoordinateSystem == 1 ? ParticleEmitterSim.Rotate(direction, owner.Rotation) : direction;

    /// <summary>The sprite quads of every alive particle, in slot order.</summary>
    [Provenance(ProvenanceKind.Decoded, "USpriteEmitter vertex fill 0x110eebf0 (editor twin): axes per UseDirectionAs, spin about the view axis, cell rectangles down a column first, blend cells")]
    public static void Sprites(ParticleEmitterSim e, in ParticleCamera camera, in ParticleOwner owner, List<SpriteQuad> into)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(into);
        ParticleParams p = e.Params;
        FVector rawNormal = p.ProjectionNormal.SafeNormal();
        FVector normal = p.UseRotationFrom == 1 ? ParticleEmitterSim.Rotate(rawNormal, owner.Rotation) : rawNormal;

        // Under PTCS_Relative the engine builds each quad in the owner's frame (the camera moved into it) and draws it
        // through the owner's transform, so the projection normal and the best-axis pick turn with the owner too.
        FVector sideNormal = WorldDirection(p, rawNormal, owner);
        ReadOnlySpan<Particle> particles = e.Particles;
        int used = Math.Min(e.ActiveParticles, particles.Length);
        for (int i = 0; i < used; i++)
        {
            ref readonly Particle q = ref particles[i];
            if (!q.Alive)
            {
                continue;
            }

            FVector at = World(p, q.Location, owner);
            FVector localD = p.UseDirectionAs == 4 ? normal : (q.Location - q.OldLocation).SafeNormal();
            FVector d = WorldDirection(p, localD, owner);
            // UpAndNormal and RightAndNormal cross with the stored normal, not the one PTRS_Actor turned.
            FVector c = p.UseDirectionAs is 5 or 6 ? d ^ sideNormal : (d ^ (at - camera.Location)).SafeNormal();
            FVector a;
            FVector b;
            FVector spinAxis = camera.Forward;
            float sx = q.Size.X;
            float sy = q.Size.Y;
            switch (p.UseDirectionAs)
            {
                case 0:
                    a = camera.Up * sx;
                    b = camera.Right * sx;
                    break;
                case 7:
                    a = camera.Up * sy;
                    b = camera.Right * sx;
                    break;
                case 1:
                case 5:
                    a = d * sy;
                    b = c * sx;
                    break;
                case 2:
                case 6:
                    a = c * sy;
                    b = d * sx;
                    break;
                case 3:
                case 4:
                    // The best axis is picked in the frame the quad is built in (the owner's under PTCS_Relative).
                    FVector axis = MathF.Abs(localD.X) > ParticleK.BestAxisThreshold ? new FVector(0f, 1f, 0f)
                        : MathF.Abs(localD.Y) > ParticleK.BestAxisThreshold ? new FVector(0f, 0f, 1f) : new FVector(1f, 0f, 0f);
                    FVector localA = (localD ^ axis) * sy;
                    a = WorldDirection(p, localA, owner);
                    b = WorldDirection(p, (localA ^ localD).SafeNormal() * sx, owner);
                    spinAxis = d;
                    break;
                default:
                    a = default;
                    b = default;
                    break;
            }

            if (p.SpinParticles)
            {
                float angle = SpinAngle(q.StartSpin.X, q.SpinsPerSecond.X, q.Time);
                a = ParticleEmitterSim.RotateAngleAxis(a, angle, spinAxis);
                b = ParticleEmitterSim.RotateAngleAxis(b, angle, spinAxis);
            }

            var quad = new SpriteQuad
            {
                C0 = at - a - b,
                C1 = at - a + b,
                C2 = at + a - b,
                C3 = at + a + b,
                Color = q.Color,
            };
            Cells(p, q, ref quad);
            into.Add(quad);
        }
    }

    /// <summary>Rule 19: the spin angle in radians (the engine works in rotator units, 65536 a turn).</summary>
    public static float SpinAngle(float startSpin, float spinsPerSecond, float time)
    {
        int units = spinsPerSecond >= 0f
            ? (int)MathF.Round(startSpin + (time * spinsPerSecond)) & 0xffff
            : 0xffff - ((int)MathF.Round(startSpin - (time * spinsPerSecond)) & 0xffff);
        return units * (MathF.Tau / 65536f);
    }

    /// <summary>Rule 20: the cell rectangle (and the next cell with blending).</summary>
    public static void Cells(ParticleParams p, in Particle q, ref SpriteQuad quad)
    {
        quad.U0 = 0f;
        quad.V0 = 0f;
        quad.U1 = 1f;
        quad.V1 = 1f;
        quad.BU0 = 0f;
        quad.BV0 = 0f;
        quad.BU1 = 1f;
        quad.BV1 = 1f;
        int us = p.TextureUSubdivisions;
        int vs = p.TextureVSubdivisions;
        if (us == 0 || vs == 0)
        {
            return;
        }

        int index;
        float t = q.MaxLifetime == 0f ? 0f : Math.Clamp(q.Time / q.MaxLifetime, 0f, 1f);
        int total = us * vs;
        bool byAge = q.Subdivision == -1;
        if (byAge)
        {
            if (q.MaxLifetime == 0f)
            {
                return;
            }

            if (!p.UseSubdivisionScale)
            {
                index = p.SubdivisionEnd == 0
                    ? (int)MathF.Floor(total * t)
                    : (int)MathF.Floor(Math.Max(1, p.SubdivisionEnd - p.SubdivisionStart) * t) + p.SubdivisionStart;
            }
            else
            {
                index = 0;
                while (index < p.SubdivisionScale.Length && !(t < p.SubdivisionScale[index]))
                {
                    index++;
                }
            }
        }
        else
        {
            index = q.Subdivision;
        }

        index = Math.Clamp(index, 0, total - 1);
        (quad.U0, quad.V0, quad.U1, quad.V1) = Cell(index, us, vs);
        (quad.BU0, quad.BV0, quad.BU1, quad.BV1) = (quad.U0, quad.V0, quad.U1, quad.V1);
        if (byAge && p.BlendBetweenSubdivisions)
        {
            int next = index + 1;
            if (next != total)
            {
                (quad.BU0, quad.BV0, quad.BU1, quad.BV1) = Cell(next, us, vs);
            }

            float position = (p.UseSubdivisionScale && p.SubdivisionScale.Length > total) ? 0f : total * t;
            float weight = position - MathF.Floor(position);
            quad.Color = quad.Color with { A = (byte)Math.Clamp((int)(weight * 255f), 0, 255) };
        }
    }

    /// <summary>Cell <paramref name="index"/>: u from index / V, v from index mod V (down a column first).</summary>
    public static (float U0, float V0, float U1, float V1) Cell(int index, int us, int vs)
    {
        float u0 = (index / vs) * (1f / us);
        float v0 = (index % vs) * (1f / vs);
        return (u0, v0, MathF.Min(1f, u0 + (1f / us)), MathF.Min(1f, v0 + (1f / vs)));
    }

    /// <summary>Rule 22: the spark trails.</summary>
    [Provenance(ProvenanceKind.Decoded, "USparkEmitter vertex fill 0x110f3340 (editor twin): segments at Time - k TimeBetweenSegments on the path p + s v + s^2 a")]
    public static void Sparks(ParticleEmitterSim e, in ParticleOwner owner, List<SparkSegment> into)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(into);
        ParticleParams p = e.Params;
        int segments = Math.Max(1, (int)MathF.Round(p.LineSegmentsRange.Larger));
        ReadOnlySpan<Particle> particles = e.Particles;
        int used = Math.Min(e.ActiveParticles, particles.Length);
        for (int i = 0; i < used; i++)
        {
            ref readonly Particle q = ref particles[i];
            if (!q.Alive)
            {
                continue;
            }

            for (int k = 0; k < segments; k++)
            {
                float s0 = MathF.Max(0f, q.Time - (k * q.TimeBetweenSegments));
                float s1 = MathF.Max(0f, q.Time - ((k + 1) * q.TimeBetweenSegments));
                FVector a = q.SparkLocation + (q.SparkVelocity * s0) + (p.Acceleration * (s0 * s0));
                FVector b = q.SparkLocation + (q.SparkVelocity * s1) + (p.Acceleration * (s1 * s1));
                into.Add(new SparkSegment(World(p, a, owner), World(p, b, owner), k / (float)segments, k / (float)segments, q.Color));
            }
        }
    }

    /// <summary>Rule 24: the mesh placements.</summary>
    [Provenance(ProvenanceKind.Decoded, "UMeshEmitter::RenderParticles 0x110f3dc0 (editor twin): scale Size, spin rotator (Pitch Y, Yaw X, Roll Z), relative frame")]
    public static void Meshes(ParticleEmitterSim e, in ParticleOwner owner, List<MeshInstance> into)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(into);
        ParticleParams p = e.Params;
        ReadOnlySpan<Particle> particles = e.Particles;
        int used = Math.Min(e.ActiveParticles, particles.Length);
        for (int i = 0; i < used; i++)
        {
            ref readonly Particle q = ref particles[i];
            if (!q.Alive)
            {
                continue;
            }

            FRotator spin = p.SpinParticles
                ? new FRotator(
                    (int)((q.SpinsPerSecond.Y * q.Time) + q.StartSpin.Y),
                    (int)((q.SpinsPerSecond.X * q.Time) + q.StartSpin.X),
                    (int)((q.SpinsPerSecond.Z * q.Time) + q.StartSpin.Z))
                : default;
            if (p.CoordinateSystem == 1)
            {
                // The owner's frame follows; the spin is composed with it as the matrices are.
                spin = new FRotator(spin.Pitch + owner.Rotation.Pitch, spin.Yaw + owner.Rotation.Yaw, spin.Roll + owner.Rotation.Roll);
            }

            into.Add(new MeshInstance(World(p, q.Location, owner), spin, q.Size, q.Color));
        }
    }

    /// <summary>
    /// Rule 25: each beam as a camera-facing strip through its points (<see cref="ParticleEmitterSim.BeamPoints"/>),
    /// one quad between each two. At each point the strip's half width is the particle's Size.X across the line from
    /// the point before it (before the first: the first point mirrored through the second) and the camera; U runs
    /// along the beam (BeamTextureUScale x the point's share of its length), V across it (0 to BeamTextureVScale).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UBeamEmitter::RenderParticles editor 0x110f62c0: side = normalise((P - prev) x (prev - camera)) x Size.X, vertices P + side (V 0) and P - side (V BeamTextureVScale), U = BeamTextureUScale x the point's w")]
    [Provenance(ProvenanceKind.Estimated, "One sheet (SheetsUsed above 1 turns more strips about the beam) and no DynamicHFNoise; no SCCT beam sets either")]
    public static void Beams(ParticleEmitterSim e, in ParticleCamera camera, in ParticleOwner owner, List<SpriteQuad> into)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(into);
        ParticleParams p = e.Params;
        ReadOnlySpan<Particle> particles = e.Particles;
        int used = Math.Min(e.ActiveParticles, particles.Length);
        int n = e.BeamPointCount;
        if (n < 2)
        {
            return;
        }

        Span<FVector> at = stackalloc FVector[n];
        Span<FVector> side = stackalloc FVector[n];
        for (int i = 0; i < used; i++)
        {
            ref readonly Particle q = ref particles[i];
            if (!q.Alive)
            {
                continue;
            }

            ReadOnlySpan<FVector> points = e.BeamPoints(i);
            ReadOnlySpan<float> u = e.BeamU(i);
            for (int j = 0; j < n; j++)
            {
                at[j] = World(p, points[j], owner);
            }

            FVector prev = (at[0] * 2f) - at[1];
            for (int j = 0; j < n; j++)
            {
                side[j] = ((at[j] - prev) ^ (prev - camera.Location)).SafeNormal() * q.Size.X;
                prev = at[j];
            }

            for (int j = 0; j < n - 1; j++)
            {
                into.Add(new SpriteQuad
                {
                    C0 = at[j] - side[j],
                    C1 = at[j + 1] - side[j + 1],
                    C2 = at[j] + side[j],
                    C3 = at[j + 1] + side[j + 1],
                    U0 = p.BeamTextureUScale * u[j],
                    V0 = 0f,
                    U1 = p.BeamTextureUScale * u[j + 1],
                    V1 = p.BeamTextureVScale,
                    BU1 = 1f,
                    BV1 = 1f,
                    Color = q.Color,
                });
            }
        }
    }
}
