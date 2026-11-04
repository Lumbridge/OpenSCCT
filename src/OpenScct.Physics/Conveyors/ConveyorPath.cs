using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Physics.Conveyors;

/// <summary>A point of a conveyor path on its riding surface: a sharp corner (radius 0) or a rounded one.</summary>
public readonly record struct ConveyorPathPoint(FVector Location, float Radius);

/// <summary>What a point projects to on a conveyor path (<see cref="ConveyorPath.Project"/>).</summary>
public readonly record struct ConveyorProjection(float Along, float Lateral, float Distance, FVector Surface, FVector Tangent);

/// <summary>
/// The centre line of a conveyor in world space (spec/additions/conveyors.md#path): straight runs between the points,
/// with an arc of the point's radius at every rounded corner, fitted in the horizontal plane and climbing linearly.
/// Lengths and positions along the path are measured on the riding surface. Pure float32 maths with
/// <see cref="SvmMath"/>, so the server, every client and the editor agree on every value.
/// </summary>
public sealed class ConveyorPath
{
    private readonly Piece[] _pieces;

    private ConveyorPath(Piece[] pieces, bool loop)
    {
        _pieces = pieces;
        IsLoop = loop;
        float total = 0f;
        foreach (Piece p in pieces)
        {
            total += p.Length;
        }

        Length = total;
    }

    /// <summary>Total length along the riding surface.</summary>
    public float Length { get; }

    /// <summary>The path closes from its last point back to its first.</summary>
    public bool IsLoop { get; }

    /// <summary>Number of straight runs and arcs.</summary>
    public int PieceCount => _pieces.Length;

    /// <summary>
    /// Builds the path through <paramref name="points"/>. End points of an open path are never rounded. A radius is
    /// reduced so its arc takes at most half of each run it joins. Fewer than two distinct points give an empty path.
    /// </summary>
    public static ConveyorPath Build(IReadOnlyList<ConveyorPathPoint> points, bool loop)
    {
        ArgumentNullException.ThrowIfNull(points);
        var pts = new List<ConveyorPathPoint>(points.Count);
        foreach (ConveyorPathPoint p in points)
        {
            if (pts.Count == 0 || (p.Location - pts[^1].Location).SizeSquared2D() > MinRunSq)
            {
                pts.Add(p);
            }
        }

        if (loop && pts.Count > 2 && (pts[^1].Location - pts[0].Location).SizeSquared2D() <= MinRunSq)
        {
            pts.RemoveAt(pts.Count - 1);
        }

        loop &= pts.Count > 2;
        if (pts.Count < 2)
        {
            return new ConveyorPath([], false);
        }

        int n = pts.Count;
        // Where each corner's arc starts and ends; equal to the corner itself for sharp ones.
        var arcIn = new FVector[n];
        var arcOut = new FVector[n];
        var arcs = new Piece?[n];
        for (int i = 0; i < n; i++)
        {
            arcIn[i] = arcOut[i] = pts[i].Location;
            bool end = !loop && (i == 0 || i == n - 1);
            if (end || pts[i].Radius <= 0f)
            {
                continue;
            }

            FVector prev = pts[(i + n - 1) % n].Location;
            FVector at = pts[i].Location;
            FVector next = pts[(i + 1) % n].Location;
            arcs[i] = Fillet(prev, at, next, pts[i].Radius, out arcIn[i], out arcOut[i]);
        }

        var pieces = new List<Piece>();
        int runs = loop ? n : n - 1;
        for (int i = 0; i < runs; i++)
        {
            int j = (i + 1) % n;
            // A loop starts after its first corner's arc and ends with it.
            if (i > 0 && arcs[i] is { } arc)
            {
                pieces.Add(arc);
            }

            if ((arcIn[j] - arcOut[i]).SizeSquared2D() > MinRunSq)
            {
                pieces.Add(Piece.Line(arcOut[i], arcIn[j]));
            }
        }

        if (loop && arcs[0] is { } first)
        {
            pieces.Add(first);
        }

        return new ConveyorPath([.. pieces], loop);
    }

    /// <summary>
    /// The nearest place on the path to <paramref name="point"/> in the horizontal plane. <paramref name="halfWidth"/>
    /// limits how far to the side it may be; an open path also ends square at its two end points. False when the
    /// point is off the path.
    /// </summary>
    public bool Project(FVector point, float halfWidth, out ConveyorProjection projection) =>
        Project(point, halfWidth, float.MaxValue, float.MaxValue, 0f, out projection);

    /// <summary>
    /// As <see cref="Project(FVector, float, out ConveyorProjection)"/>, counting only places where the point is at most
    /// <paramref name="under"/> below the surface and at most <paramref name="over"/> plus <paramref name="liftPerSlope"/>
    /// times the slope (climb per flat uu) above it. A path that passes over itself, or a conveyor on the floor above,
    /// then finds the level the point is on.
    /// </summary>
    /// <remarks>
    /// Past a sharp corner, on its outside, the run that ends there and the run that starts there both clamp to the
    /// corner point at the same distance. The run that starts there wins, so a body in that pocket turns the corner
    /// instead of being pushed on into the outer wall.
    /// </remarks>
    public bool Project(FVector point, float halfWidth, float under, float over, float liftPerSlope, out ConveyorProjection projection)
    {
        projection = default;
        bool found = false, foundPastEnd = false;
        float along = 0f;
        for (int i = 0; i < _pieces.Length; i++)
        {
            Piece piece = _pieces[i];
            bool openStart = !IsLoop && i == 0;
            bool openEnd = !IsLoop && i == _pieces.Length - 1;
            if (piece.Project(point, openStart, openEnd, out float s, out float lateral, out float distance, out FVector surface,
                    out FVector tangent, out bool pastEnd) && distance <= halfWidth && InBand(point, surface, tangent, under, over, liftPerSlope) &&
                (!found || distance < projection.Distance - TieDistance ||
                 (distance <= projection.Distance + TieDistance && foundPastEnd && !pastEnd)))
            {
                projection = new ConveyorProjection(along + s, lateral, distance, surface, tangent);
                found = true;
                foundPastEnd = pastEnd;
            }

            along += piece.Length;
        }

        return found;
    }

    // Distances this close are a tie (float rounding of the shared corner point).
    private const float TieDistance = 0.01f;

    private static bool InBand(FVector point, FVector surface, FVector tangent, float under, float over, float liftPerSlope)
    {
        float height = point.Z - surface.Z;
        if (height < -under)
        {
            return false;
        }

        float flat = SvmMath.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        float slope = flat > 0f ? SvmMath.Abs(tangent.Z) / flat : 0f;
        return height <= over + liftPerSlope * slope;
    }

    /// <summary>
    /// The centre-line point and the unit direction of travel at <paramref name="along"/> (wrapped on a loop, clamped
    /// to the ends otherwise).
    /// </summary>
    public void Evaluate(float along, out FVector position, out FVector tangent)
    {
        if (_pieces.Length == 0)
        {
            position = tangent = default;
            return;
        }

        along = IsLoop ? Wrap(along, Length) : SvmMath.Clamp(along, 0f, Length);
        for (int i = 0; i < _pieces.Length; i++)
        {
            Piece piece = _pieces[i];
            if (along <= piece.Length || i == _pieces.Length - 1)
            {
                piece.Evaluate(SvmMath.Min(along, piece.Length), out position, out tangent);
                return;
            }

            along -= piece.Length;
        }

        position = tangent = default;
    }

    /// <summary>The straight runs and arcs in order, for drawing and export.</summary>
    public IEnumerable<ConveyorPieceInfo> Pieces()
    {
        foreach (Piece p in _pieces)
        {
            yield return new ConveyorPieceInfo(p.IsArc, p.Start, p.End, p.Length, p.Centre, p.Radius, p.Sweep);
        }
    }

    /// <summary><paramref name="value"/> wrapped into [0, <paramref name="period"/>).</summary>
    public static float Wrap(float value, float period)
    {
        if (period <= 0f)
        {
            return 0f;
        }

        float r = value - period * SvmMath.Floor(value / period);
        return r >= period ? 0f : r;
    }

    // Runs shorter than this are dropped (0.1 uu).
    private const float MinRunSq = 0.01f;

    private static Piece? Fillet(FVector prev, FVector at, FVector next, float radius, out FVector arcIn, out FVector arcOut)
    {
        arcIn = arcOut = at;
        FVector dIn = Flat(at - prev);
        FVector dOut = Flat(next - at);
        float lenIn = dIn.Size2D();
        float lenOut = dOut.Size2D();
        dIn *= 1f / lenIn;
        dOut *= 1f / lenOut;
        float cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
        float dot = SvmMath.Clamp(dIn.X * dOut.X + dIn.Y * dOut.Y, -1f, 1f);
        float turn = SvmMath.Atan2(SvmMath.Abs(cross), dot);
        if (turn < MinTurn || turn > SvmMath.PI - MinTurn)
        {
            return null;
        }

        float half = SvmMath.Tan(turn * 0.5f);
        float tangentLength = SvmMath.Min(radius * half, SvmMath.Min(lenIn, lenOut) * 0.5f);
        float r = tangentLength / half;
        arcIn = at - (at - prev) * (tangentLength / lenIn);
        arcOut = at + (next - at) * (tangentLength / lenOut);
        float side = cross > 0f ? 1f : -1f;
        var across = new FVector(-dIn.Y, dIn.X, 0f);
        FVector centre = arcIn + across * (r * side);
        return Piece.Arc(arcIn, arcOut, centre, r, turn * side);
    }

    // Corners straighter than this (radians) need no arc; sharper than pi minus it fold back on themselves.
    private const float MinTurn = 0.001f;

    private static FVector Flat(FVector v) => new(v.X, v.Y, 0f);

    private readonly struct Piece
    {
        public readonly bool IsArc;
        public readonly FVector Start;
        public readonly FVector End;
        public readonly float Length;
        public readonly FVector Centre;
        public readonly float Radius;

        /// <summary>
        /// Signed turn of an arc in radians: positive turns from +X towards +Y (to the right in UE's left-handed frame).
        /// </summary>
        public readonly float Sweep;

        private readonly float _startAngle;
        private readonly float _flatLength;

        private Piece(bool arc, FVector start, FVector end, float length, FVector centre, float radius, float sweep, float startAngle,
            float flatLength)
        {
            IsArc = arc;
            Start = start;
            End = end;
            Length = length;
            Centre = centre;
            Radius = radius;
            Sweep = sweep;
            _startAngle = startAngle;
            _flatLength = flatLength;
        }

        public static Piece Line(FVector start, FVector end)
        {
            float flat = (end - start).Size2D();
            return new Piece(false, start, end, (end - start).Size(), default, 0f, 0f, 0f, flat);
        }

        public static Piece Arc(FVector start, FVector end, FVector centre, float radius, float sweep)
        {
            float flat = radius * SvmMath.Abs(sweep);
            float rise = end.Z - start.Z;
            float length = SvmMath.Sqrt(flat * flat + rise * rise);
            float startAngle = SvmMath.Atan2(start.Y - centre.Y, start.X - centre.X);
            return new Piece(true, start, end, length, centre, radius, sweep, startAngle, flat);
        }

        /// <param name="pastEnd">The nearest place was clamped to the piece's end: the point lies beyond it.</param>
        public bool Project(FVector p, bool openStart, bool openEnd, out float along, out float lateral, out float distance,
            out FVector surface, out FVector tangent, out bool pastEnd)
        {
            float u;
            FVector flatTangent;
            if (!IsArc)
            {
                FVector d = Flat(End - Start);
                float lenSq = d.SizeSquared2D();
                float raw = lenSq > 0f ? ((p.X - Start.X) * d.X + (p.Y - Start.Y) * d.Y) / lenSq : 0f;
                if ((openStart && raw < 0f) || (openEnd && raw > 1f))
                {
                    along = lateral = distance = 0f;
                    surface = tangent = default;
                    pastEnd = false;
                    return false;
                }

                pastEnd = raw > 1f;
                u = SvmMath.Clamp(raw, 0f, 1f);
                flatTangent = d * (1f / SvmMath.Sqrt(lenSq));
                surface = Start + (End - Start) * u;
            }
            else
            {
                float angle = SvmMath.Atan2(p.Y - Centre.Y, p.X - Centre.X);
                float sweep = SvmMath.Abs(Sweep);
                // Angle from the arc's start in its own turning direction, in (-pi, pi] around the arc's middle.
                float rel = (angle - _startAngle) * (Sweep > 0f ? 1f : -1f);
                rel = Wrap(rel - sweep * 0.5f + SvmMath.PI, 2f * SvmMath.PI) - SvmMath.PI + sweep * 0.5f;
                pastEnd = rel > sweep;
                u = SvmMath.Clamp(rel / sweep, 0f, 1f);
                float a = _startAngle + Sweep * u;
                float ca = SvmMath.Cos(a);
                float sa = SvmMath.Sin(a);
                surface = new FVector(Centre.X + Radius * ca, Centre.Y + Radius * sa, Start.Z + (End.Z - Start.Z) * u);
                flatTangent = Sweep > 0f ? new FVector(-sa, ca, 0f) : new FVector(sa, -ca, 0f);
            }

            float dx = p.X - surface.X;
            float dy = p.Y - surface.Y;
            distance = SvmMath.Sqrt(dx * dx + dy * dy);
            // Positive towards +Y of the direction of travel (its right-hand side in UE's frame).
            lateral = flatTangent.X * dy - flatTangent.Y * dx;
            along = Length * u;
            tangent = Tangent(flatTangent);
            return true;
        }

        public void Evaluate(float along, out FVector position, out FVector tangent)
        {
            float u = Length > 0f ? along / Length : 0f;
            if (!IsArc)
            {
                position = Start + (End - Start) * u;
                tangent = Tangent(Flat(End - Start) * (_flatLength > 0f ? 1f / _flatLength : 0f));
                return;
            }

            float a = _startAngle + Sweep * u;
            float ca = SvmMath.Cos(a);
            float sa = SvmMath.Sin(a);
            position = new FVector(Centre.X + Radius * ca, Centre.Y + Radius * sa, Start.Z + (End.Z - Start.Z) * u);
            tangent = Tangent(Sweep > 0f ? new FVector(-sa, ca, 0f) : new FVector(sa, -ca, 0f));
        }

        // The direction of travel on the surface: the flat direction tilted by the piece's climb.
        private FVector Tangent(FVector flat) => new FVector(flat.X * _flatLength, flat.Y * _flatLength, End.Z - Start.Z).SafeNormal();
    }
}

/// <summary>One straight run or arc of a <see cref="ConveyorPath"/>.</summary>
public readonly record struct ConveyorPieceInfo(bool IsArc, FVector Start, FVector End, float Length, FVector Centre, float Radius, float Sweep);
