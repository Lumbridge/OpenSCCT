using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScctGame.Render;

internal readonly record struct SmokeTemporalView(ulong Camera, FVector Eye, FVector Right, FVector Up, FVector Forward,
    float ProjectionX, float ProjectionY, float Time, float Near, float Far,
    int Offset, int Width, int Height, int Slices, int Vision, int Quality, ulong Geometry);

/// <summary>History belongs to a camera and its volume layout, never to whichever camera rendered last.</summary>
internal sealed class SmokeTemporalHistory
{
    private readonly Dictionary<int, SmokeTemporalView> _views = new();
    public long AcceptedViews { get; private set; }
    public long RejectedViews { get; private set; }

    public bool Update(int slot, SmokeTemporalView current, out SmokeTemporalView previous)
    {
        bool valid = _views.TryGetValue(slot, out previous) && current.Quality > 0 &&
            previous.Camera == current.Camera && previous.Geometry == current.Geometry &&
            previous.Width == current.Width && previous.Height == current.Height && previous.Slices == current.Slices &&
            previous.Quality == current.Quality && previous.Vision == current.Vision &&
            MathF.Abs(previous.ProjectionX - current.ProjectionX) < .0001f &&
            MathF.Abs(previous.ProjectionY - current.ProjectionY) < .0001f &&
            current.Time >= previous.Time && current.Time - previous.Time <= .1f &&
            FVector.DistSquared(previous.Eye, current.Eye) <= 50f * 50f &&
            (previous.Forward | current.Forward) >= .9659258f && (previous.Up | current.Up) >= .9659258f;
        _views[slot] = current;
        if (valid) AcceptedViews++; else RejectedViews++;
        return valid;
    }

    public void Remove(int slot) => _views.Remove(slot);
    public void Clear() => _views.Clear();
}
