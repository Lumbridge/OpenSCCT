using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;

namespace OpenScct.Game.AI;

public sealed partial class NavBuilder
{
    private void AddJumps()
    {
        // Only launch near a break in standing walk connectivity. Ordinary floor must not acquire thousands of
        // redundant jumps, and every takeoff needs a supported run-up behind it.
        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
        float gravity = -OpenScct.Physics.K.DefaultGravity;
        float peak = _profile.JumpZ * _profile.JumpZ / (2f * gravity);
        for (int from = 0; from < _nodes.Count; from++)
        {
            if (!_nodes[from].Standing) continue;
            FVector a = _nodes[from].Floor;
            int ix = (int)MathF.Round((a.X - _min.X) / _profile.Spacing);
            int iy = (int)MathF.Round((a.Y - _min.Y) / _profile.Spacing);
            foreach ((int dx, int dy) in directions)
            {
                bool connected = false;
                if (_columns.TryGetValue((ix + dx * 3, iy + dy * 3), out var ahead))
                {
                    foreach (int node in ahead)
                        if (HasWalkChain(from, node, 3)) connected = true;
                }
                if (connected) continue;
                FVector direction = new FVector(dx, dy, 0f).SafeNormal();
                FVector runup = a - direction * 80f;
                bool? runupClear = null;
                int maxCells = (int)MathF.Floor(_profile.JumpSpeed * 2f * _profile.JumpZ / gravity / _profile.Spacing);
                for (int k = 3; k <= maxCells; k++)
                {
                    if (runupClear == false) break;
                    if (!_columns.TryGetValue((ix + dx * k, iy + dy * k), out var column)) continue;
                    foreach (int to in column)
                    {
                        FVector b = _nodes[to].Floor;
                        float rise = b.Z - a.Z;
                        if (!_nodes[to].Standing || rise < -_profile.StepUp || rise > peak - 16f) continue;
                        float time = (_profile.JumpZ + MathF.Sqrt(_profile.JumpZ * _profile.JumpZ - 2f * gravity * rise)) / gravity;
                        float distance = (b - a).Size2D();
                        // The destination is near the descending portion of a full-speed jump. Require floor beyond
                        // it too, so a small timing difference cannot turn a planned jump into an unsafe drop.
                        if (distance < time * _profile.JumpSpeed * 0.7f || distance > time * _profile.JumpSpeed - 12f) continue;
                        // Reject tall walls before the more expensive support queries. The same takeoff is reused
                        // for all destinations in this direction, so validate its run-up only once, on demand.
                        if (CrossesLadder(runup, b, a.Z, peak)) continue;
                        if (!JumpArcClear(a, direction, time, gravity)) continue;
                        runupClear ??= ContinuousStep(runup, a, 3) && Sweep(runup, a, a.Z, _profile.StandingHeight);
                        if (!runupClear.Value) break;
                        if (!SupportedLanding(b) || !SupportedLanding(b + direction * 128f) ||
                            !ContinuousStep(b, b + direction * 128f, 4) ||
                            !Sweep(b, b + direction * 128f, b.Z, _profile.StandingHeight)) continue;
                        AddLink(new NavLink(NavEdgeKind.Jump, from, to, a, FaceYaw(direction), b, -1, rise),
                            distance + 2f * AiK.CrouchSpeed);
                        break;
                    }
                }
            }
        }
    }

    // The ladders in play (their line, bottom first), noted before the jumps are planned.
    private readonly List<(FVector Bottom, FVector Top)> _ladders = new();

    private void NoteLadders(IReadOnlyList<GERecord>? ges, byte mask)
    {
        if (ges is null) return;
        foreach (GERecord ge in ges)
        {
            if (ge.Kind != GEKind.LADDER || !ge.IsActive(mask) || ge.Platform == GEPlatform.PS2Only) continue;
            _ladders.Add(ge.P0.Z <= ge.P1.Z ? (ge.P0, ge.P1) : (ge.P1, ge.P0));
        }
    }

    /// <summary>
    /// The run-up and flight from <paramref name="from"/> to <paramref name="to"/> pass a ladder at the body's
    /// height. A pawn that runs onto a ladder's top or brushes it in the air takes hold of it, so a jump planned
    /// across a hatch with a ladder in it (MoonD's tunnel shafts) sent the bot back down the ladder it had just
    /// climbed, again and again.
    /// </summary>
    private bool CrossesLadder(FVector from, FVector to, float floorZ, float peak)
    {
        float reach = _profile.Radius + 24f;
        var along = new FVector(to.X - from.X, to.Y - from.Y, 0f);
        float length = along.Size();
        if (length < 1f) return false;
        along *= 1f / length;
        foreach ((FVector bottom, FVector top) in _ladders)
        {
            if (top.Z < floorZ - 2f * _profile.StandingHeight || bottom.Z > floorZ + peak + 2f * _profile.StandingHeight) continue;
            float t = Math.Clamp((bottom.X - from.X) * along.X + (bottom.Y - from.Y) * along.Y, 0f, length);
            float dx = from.X + along.X * t - bottom.X;
            float dy = from.Y + along.Y * t - bottom.Y;
            if (dx * dx + dy * dy < reach * reach) return true;
        }

        return false;
    }
    private bool SupportedLanding(FVector floor)
    {
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                if (FloorNear(floor + new FVector(x * _profile.Radius, y * _profile.Radius, 0f),
                    floor.Z + 8f, floor.Z - 8f) is null) return false;
        return true;
    }

    private bool JumpArcClear(FVector floor, FVector direction, float time, float gravity)
    {
        FVector body = new(_profile.Radius, _profile.Radius, _profile.StandingHeight);
        FVector origin = floor + new FVector(0f, 0f, _profile.StandingHeight + 2f);
        FVector previous = origin;
        int samples = (int)MathF.Ceiling(time * _profile.JumpSpeed / 12f);
        for (int i = 1; i <= samples; i++)
        {
            float t = time * i / samples;
            FVector next = origin + direction * (_profile.JumpSpeed * t) +
                new FVector(0f, 0f, _profile.JumpZ * t - 0.5f * gravity * t * t);
            if (_world.LineCheck(out _, CollisionProbe, next, previous, body, WorldFlags)) return false;
            previous = next;
        }
        return true;
    }

    private void AddBarrierHops(GERecord ge)
    {
        FVector along = ge.P1 - ge.P0;
        float length = along.Size2D();
        float margin = _profile.Radius + 24f;
        if (length < 2f * margin || MathF.Abs(along.Z) > 8f) return;
        along *= 1f / length;
        FVector side = new(-along.Y, along.X, 0f);
        int samples = Math.Max(1, (int)MathF.Ceiling((length - 2f * margin) / 96f) + 1);
        for (int i = 0; i < samples; i++)
        {
            FVector at = ge.P0 + along * (samples == 1 ? length * 0.5f : margin + (length - 2f * margin) * i / (samples - 1));
            for (int way = -1; way <= 1; way += 2)
            {
                FVector forward = side * way;
                FVector start = at - forward * 45f;
                if (FloorNear(start, at.Z - 75f, at.Z - 135f) is not { } z) continue;
                float height = at.Z - z;
                // CheckBarrier accepts the rail between centre - 5 and centre + 40. The animation moves 200 forward.
                if (height < _profile.StandingHeight - 5f || height > _profile.StandingHeight + 38f) continue;
                start.Z = z;
                FVector end = start + forward * 200f;
                if (FloorNear(end, z + 20f, z - _profile.StepUp) is not { } ez) continue;
                end.Z = ez;
                int from = NearestTo(start, 40f), to = NearestTo(end, 40f);
                if (from < 0 || to < 0 || !_nodes[from].Standing || !_nodes[to].Standing) continue;
                FVector bodyStart = start + new FVector(0f, 0f, Lift(_profile.StandingHeight));
                if (!_world.LineCheck(out _, CollisionProbe, bodyStart + forward * 65f, bodyStart,
                    Body(_profile.StandingHeight), WorldFlags)) continue;
                // No solid wall above the rail, no thick platform in place of a vaultable barrier.
                if (!Sweep(start, end, at.Z + 4f, _profile.StandingHeight) || !Fits(end, _profile.StandingHeight)) continue;
                AddLink(new NavLink(NavEdgeKind.BarrierHop, from, to, start, FaceYaw(forward),
                    _nodes[to].Floor, ge.Index, height), 4f * AiK.CrouchSpeed);
            }
        }
    }
}
