using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;

namespace OpenScct.Game.AI;

public sealed partial class NavBuilder
{
    // SBasePawn's standing hand-over-hand zone and vertical-pipe hold offset.
    private const float HohHandsOffset = 128f;
    private const float PipeHoldOffset = 37f;

    private void AddTraversalChains(IReadOnlyList<GERecord> ges, byte mask)
    {
        if (!_profile.Traversal) return;
        foreach (GERecord bar in ges)
        {
            if (!Active(bar, mask) || bar.Kind != GEKind.HOH || MathF.Abs(bar.P0.Z - bar.P1.Z) > 16f) continue;
            float length = (bar.P1 - bar.P0).Size2D();
            if (length < 2f * AiK.HohGrabIn + 32f) continue;
            for (int way = 0; way < 2; way++)
            {
                FVector near = way == 0 ? bar.P0 : bar.P1;
                FVector far = way == 0 ? bar.P1 : bar.P0;
                FVector direction = (far - near).SafeNormal();
                FVector grab = near + direction * AiK.HohGrabIn;
                FVector drop = far - direction * (AiK.HohEndStop + 8f);
                if (!HangingClear(grab, drop)) continue;

                // An ordinary jump onto the bar can continue onto a cable even when no floor lies under its end.
                int entry = -1;
                FVector start = default;
                if (FloorNear(grab, grab.Z - AiK.HohJumpMin + 20f, grab.Z - AiK.HohJumpMax) is { } gz &&
                    grab.Z - gz >= AiK.HohJumpMin)
                {
                    start = new FVector(grab.X, grab.Y, gz);
                    entry = NearestTo(start, 24f);
                    if (entry >= 0 && (!_nodes[entry].Standing ||
                        !ContinuousStep(start - direction * AiK.HohRunIn, start, 2))) entry = -1;
                }
                foreach (GERecord zip in ges)
                {
                    if (!JoinsZip(zip, far, direction, mask) || ZipLanding(zip) is not { } landing) continue;
                    if (entry >= 0)
                    {
                        AddLink(new NavLink(NavEdgeKind.BarToZip, entry, landing, start, FaceYaw(direction),
                            _nodes[landing].Floor, bar.Index, far.Z - start.Z, zip.Index, -1, far, FaceYaw(direction)),
                            (length / AiK.HohSpeed + (zip.P1 - zip.P0).Size() / AiK.ZipSpeed + 7f) * AiK.CrouchSpeed);
                    }
                }

                // A pipe entry needs no top floor: its hands transfer directly onto the nearby end of the bar.
                foreach (GERecord pipe in ges)
                {
                    if (!Active(pipe, mask) || pipe.Kind != GEKind.PIPE || pipe.Points.Length < 3) continue;
                    FVector bottom = pipe.P0.Z <= pipe.P1.Z ? pipe.P0 : pipe.P1;
                    FVector top = pipe.P0.Z <= pipe.P1.Z ? pipe.P1 : pipe.P0;
                    FVector outward = new FVector(pipe.Points[2].X - pipe.P0.X, pipe.Points[2].Y - pipe.P0.Y, 0f).SafeNormal();
                    if (outward.Size2D() < 0.5f || (direction | -outward) < 0.5f) continue;
                    FVector hold = bottom + outward * PipeHoldOffset;
                    if ((near - hold).Size2D() > 20f || near.Z < bottom.Z + 200f || near.Z > top.Z + 20f) continue;
                    FVector approach = bottom + outward * 100f;
                    if (FloorNear(approach, bottom.Z + 60f, bottom.Z - 60f) is not { } bz) continue;
                    approach.Z = bz;
                    int from = NearestTo(approach, 40f);
                    if (from < 0 || !_nodes[from].Standing || !HangingClear(near, grab)) continue;
                    // Check the climb's occupied corridor, leaving the pipe itself in front of the body.
                    FVector low = new(hold.X, hold.Y, bz + _profile.StandingHeight + 2f);
                    FVector high = new(hold.X, hold.Y, near.Z - HohHandsOffset);
                    if (_world.LineCheck(out _, CollisionProbe, high, low,
                        new FVector(_profile.Radius, _profile.Radius, _profile.StandingHeight), WorldFlags)) continue;
                    float cost = ((near.Z - bz) / AiK.PipeClimbSpeed + length / AiK.HohSpeed + 7f) * AiK.CrouchSpeed;
                    if (BarLanding(drop) is { } land)
                    {
                        AddLink(new NavLink(NavEdgeKind.PipeToBar, from, land, approach, FaceYaw(-outward),
                            _nodes[land].Floor, pipe.Index, near.Z - bz, bar.Index, -1, drop, FaceYaw(direction)), cost);
                    }
                    foreach (GERecord zip in ges)
                    {
                        if (!JoinsZip(zip, far, direction, mask) || ZipLanding(zip) is not { } landZip) continue;
                        AddLink(new NavLink(NavEdgeKind.PipeToBarToZip, from, landZip, approach, FaceYaw(-outward),
                            _nodes[landZip].Floor, pipe.Index, near.Z - bz, bar.Index, zip.Index, far, FaceYaw(direction)),
                            cost + (zip.P1 - zip.P0).Size() / AiK.ZipSpeed * AiK.CrouchSpeed);
                    }
                }
            }
        }
    }

    private static bool Active(GERecord ge, byte mask) => ge.IsActive(mask) && ge.Platform != GEPlatform.PS2Only;

    private bool HangingClear(FVector a, FVector b) => !_world.LineCheck(out _, CollisionProbe,
        b - new FVector(0f, 0f, HohHandsOffset), a - new FVector(0f, 0f, HohHandsOffset),
        new FVector(_profile.Radius, _profile.Radius, _profile.StandingHeight), WorldFlags);

    private int? BarLanding(FVector end)
    {
        if (FloorNear(end, end.Z - 150f, end.Z - HohHandsOffset - _profile.StandingHeight - _profile.MaxDrop) is not { } z) return null;
        int node = NearestTo(new FVector(end.X, end.Y, z), 48f);
        return node >= 0 && _nodes[node].Standing ? node : null;
    }

    private static bool JoinsZip(GERecord zip, FVector barEnd, FVector direction, byte mask)
    {
        if (!Active(zip, mask) || zip.Kind != GEKind.ZL) return false;
        FVector high = zip.P0.Z >= zip.P1.Z ? zip.P0 : zip.P1;
        FVector low = zip.P0.Z >= zip.P1.Z ? zip.P1 : zip.P0;
        return (high - barEnd).Size2D() < 20f && MathF.Abs(high.Z - barEnd.Z) < 16f &&
            ((low - high).SafeNormal() | direction) > 0.5f;
    }

    private int? ZipLanding(GERecord zip)
    {
        FVector high = zip.P0.Z >= zip.P1.Z ? zip.P0 : zip.P1;
        FVector low = zip.P0.Z >= zip.P1.Z ? zip.P1 : zip.P0;
        float flat = (low - high).Size2D();
        if (flat < 200f) return null;
        FVector down = new FVector(low.X - high.X, low.Y - high.Y, 0f) / flat;
        foreach (float back in new[] { 60f, 100f, 150f })
        {
            FVector q = low - down * back;
            float hang = low.Z + (high.Z - low.Z) * back / flat - AiK.LedgeHangDrop - _profile.StandingHeight;
            if (FloorNear(q, hang + 40f, hang - _profile.MaxDrop) is not { } z) continue;
            int node = NearestTo(new FVector(q.X, q.Y, z), 48f);
            if (node >= 0 && _nodes[node].Standing && HangingClear(high, low)) return node;
        }
        return null;
    }
}
