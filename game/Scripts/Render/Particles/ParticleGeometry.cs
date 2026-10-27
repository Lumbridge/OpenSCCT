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
        FVector normal = p.ProjectionNormal.SafeNormal();
        if (p.UseRotationFrom == 1)
        {
            normal = ParticleEmitterSim.Rotate(normal, owner.Rotation);
        }

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
            FVector d = p.UseDirectionAs == 4 ? normal : WorldDirection(p, q.Location - q.OldLocation, owner).SafeNormal();
            FVector c = p.UseDirectionAs is 5 or 6 ? d ^ normal : (d ^ (at - camera.Location)).SafeNormal();
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
                    FVector axis = MathF.Abs(d.X) > ParticleK.BestAxisThreshold ? new FVector(0f, 1f, 0f)
                        : MathF.Abs(d.Y) > ParticleK.BestAxisThreshold ? new FVector(0f, 0f, 1f) : new FVector(1f, 0f, 0f);
                    a = (d ^ axis) * sy;
                    b = (a ^ d).SafeNormal() * sx;
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

    /// <summary>Rule 25 (summarized): each beam as a camera-facing strip from the particle to its end point.</summary>
    [Provenance(ProvenanceKind.Estimated, "UBeamEmitter: straight strip to the end point by DetermineEndPointBy; the noise and branches are not decoded")]
    public static void Beams(ParticleEmitterSim e, in ParticleCamera camera, in ParticleOwner owner, List<SpriteQuad> into)
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

            FVector start = World(p, q.Location, owner);
            FVector end = p.DetermineEndPointBy switch
            {
                1 => start + (WorldDirection(p, q.Velocity, owner).SafeNormal() * p.BeamDistanceRange.Average),
                2 when p.BeamEndPointOffsets.Length > 0 => start + p.BeamEndPointOffsets[0],
                5 when p.BeamEndPointOffsets.Length > 0 => p.BeamEndPointOffsets[0],
                _ => start + WorldDirection(p, q.Velocity, owner),
            };
            FVector along = end - start;
            FVector side = (along ^ (start - camera.Location)).SafeNormal() * q.Size.X;
            into.Add(new SpriteQuad
            {
                C0 = start - side,
                C1 = end - side,
                C2 = start + side,
                C3 = end + side,
                U0 = 0f,
                V0 = 0f,
                U1 = p.BeamTextureUScale,
                V1 = p.BeamTextureVScale,
                BU1 = 1f,
                BV1 = 1f,
                Color = q.Color,
            });
        }
    }
}
