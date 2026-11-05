using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Game.Svm;

/// <summary>
/// One face of an escalator's steps: corners A, B, C, D in order round its edge (A to B runs across the belt from its
/// left edge to its right), the way it faces, and the texture V at A and B (<see cref="V0"/>) and at C and D
/// (<see cref="V1"/>). U runs 0 at the left edge to <c>width / BeltTile</c> at the right.
/// </summary>
public readonly record struct ConveyorStepFace(FVector A, FVector B, FVector C, FVector D, FVector Normal, float V0, float V1);

/// <summary>
/// An escalator's moving steps (spec/additions/conveyors.md#look): <see cref="ConveyorLayout.StepLength"/> long along
/// the path, carried along it by the belt's phase. Each step's tread is level at its higher end and its riser stands
/// at its lower end, so the slope the pawns walk on runs through every step's inner corner wherever the steps are. At
/// the path's ends a step is cut off, its tread held at the landing's height: steps rise out of the lower landing and
/// sink into the upper one. The map builder bakes the steps at phase 0, the game moves them, and riders' feet are
/// drawn on the tread under them.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: design values, spec/additions/conveyors.md#look")]
public static class ConveyorSteps
{
    // Slivers shorter than this at the ends are left out.
    private const float MinStep = 0.5f;

    // A riser lower than this is not built (a flat stretch of the path).
    private const float MinRise = 0.5f;

    /// <summary>The faces of every step at belt <paramref name="phase"/> (uu travelled), into <paramref name="faces"/>.</summary>
    public static void Build(ConveyorPath path, float halfWidth, double phase, List<ConveyorStepFace> faces)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(faces);
        faces.Clear();
        float length = path.Length;
        const float step = ConveyorLayout.StepLength;
        if (length <= 0f)
        {
            return;
        }

        float offset = Offset(phase);
        for (int k = -1; offset + k * step < length; k++)
        {
            float b = offset + k * step;
            float s0 = SvmMath.Max(b, 0f), s1 = SvmMath.Min(b + step, length);
            if (s1 - s0 < MinStep)
            {
                continue;
            }

            path.Evaluate(s0, out FVector p0, out _);
            path.Evaluate(s1, out FVector p1, out _);
            // Each end's direction from just inside the step, so a step that starts a curve turns with it.
            path.Evaluate(s0 + 0.05f, out _, out FVector t0);
            path.Evaluate(s1 - 0.05f, out _, out FVector t1);
            float high = Tread(path, b);
            FVector r0 = RightOf(t0) * halfWidth, r1 = RightOf(t1) * halfWidth;
            var q0 = new FVector(p0.X, p0.Y, high);
            var q1 = new FVector(p1.X, p1.Y, high);
            float v0 = (s0 - b) / step, v1 = (s1 - b) / step;
            faces.Add(new ConveyorStepFace(q0 - r0, q0 + r0, q1 + r1, q1 - r1, new FVector(0f, 0f, 1f), v0, v1));

            // The riser stands at the step's lower end, from the slope up to the tread, facing down the slope.
            bool climbing = Height(path, b + step) >= Height(path, b);
            FVector foot = climbing ? p0 : p1;
            float rise = high - foot.Z;
            if (rise < MinRise)
            {
                continue;
            }

            FVector r = climbing ? r0 : r1;
            FVector top = climbing ? q0 : q1;
            FVector facing = Flat(climbing ? t0 : t1) * (climbing ? -1f : 1f);
            float vt = climbing ? v0 : v1, vb = vt - rise / step;
            var down = new FVector(0f, 0f, rise);
            faces.Add(new ConveyorStepFace(top - r - down, top + r - down, top + r, top - r, facing, vb, vt));
        }
    }

    /// <summary>
    /// Height of the tread over <paramref name="along"/> at belt <paramref name="phase"/>: the higher end of the step
    /// that covers it, held at the landing past the path's ends.
    /// </summary>
    public static float TreadAt(ConveyorPath path, float along, double phase)
    {
        ArgumentNullException.ThrowIfNull(path);
        const float step = ConveyorLayout.StepLength;
        float offset = Offset(phase);
        float b = offset + step * SvmMath.Floor((along - offset) / step);
        return Tread(path, b);
    }

    /// <summary>Where the step boundaries are at <paramref name="phase"/>: one sits this far along the path, in [0, step).</summary>
    private static float Offset(double phase)
    {
        const double step = ConveyorLayout.StepLength;
        float offset = (float)(phase - step * Math.Floor(phase / step));
        return offset >= ConveyorLayout.StepLength ? 0f : offset;
    }

    /// <summary>The tread of the step starting <paramref name="b"/> along: level at its higher end.</summary>
    private static float Tread(ConveyorPath path, float b) => SvmMath.Max(Height(path, b), Height(path, b + ConveyorLayout.StepLength));

    /// <summary>The path's surface height <paramref name="along"/> it, held at the ends past them.</summary>
    private static float Height(ConveyorPath path, float along)
    {
        path.Evaluate(SvmMath.Clamp(along, 0f, path.Length), out FVector p, out _);
        return p.Z;
    }

    /// <summary>The flat right-hand side of a direction of travel (+Y of +X in UE's frame).</summary>
    private static FVector RightOf(FVector t)
    {
        var r = new FVector(-t.Y, t.X, 0f);
        return r.SizeSquared2D() > 1e-12f ? r.SafeNormal() : new FVector(0f, 1f, 0f);
    }

    private static FVector Flat(FVector v)
    {
        var f = new FVector(v.X, v.Y, 0f);
        return f.SizeSquared2D() > 1e-12f ? f.SafeNormal() : new FVector(1f, 0f, 0f);
    }
}
