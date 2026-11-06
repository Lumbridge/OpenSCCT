using System;
using System.Collections.Generic;
using OpenScct.Game.Equipment;

namespace OpenScctGame.Render;

/// <summary>Tracks when the analytic activation atlas can change; carves are a separate shader input.</summary>
internal sealed class SmokeGrowthCache
{
    private sealed class Layer(SmokeRenderCloud cloud, float now, float matureAge)
    { public SmokeRenderCloud Cloud = cloud; public float Time = now; public float MatureAge = matureAge; }
    private readonly List<Layer?> _layers = [];
    private object? _resource;
    private int _quality;
    private bool _layoutChanged;

    public void Begin(int count, object resource, int quality)
    {
        _layoutChanged = !ReferenceEquals(_resource,resource) || _quality != quality || _layers.Count != count;
        _resource = resource; _quality = quality;
        if (_layers.Count > count) _layers.RemoveRange(count,_layers.Count-count);
        while (_layers.Count < count) _layers.Add(null);
    }

    public bool Update(int layer, SmokeRenderCloud cloud, float now)
    {
        Layer? old = _layers[layer];
        bool geometry = old is null || !ReferenceEquals(old.Cloud.Identity,cloud.Identity)
            || old.Cloud.SnapshotRevision != cloud.SnapshotRevision || old.Cloud.CreatedAt != cloud.CreatedAt
            // Authority/standalone fields have no received-baseline stamp. Compare immutable spans only on
            // a replacement snapshot; fast carve-only copies compare equal and never rebuild the atlas.
            || !ReferenceEquals(old.Cloud,cloud) && (!old.Cloud.Cells.SequenceEqual(cloud.Cells)
                || !old.Cloud.ActivationTicks.SequenceEqual(cloud.ActivationTicks));
        float matureAge = old?.MatureAge ?? float.NegativeInfinity;
        if (geometry)
        {
            matureAge = float.NegativeInfinity;
            ReadOnlySpan<byte> cells = cloud.Cells;
            ReadOnlySpan<ushort> activation = cloud.ActivationTicks;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != 0 && activation[i] != ResponsiveSmokeField.Unreached)
                    matureAge = Math.Max(matureAge,MathF.BitIncrement(activation[i]/ResponsiveSmokeField.TickRate+ResponsiveSmokeField.GrowthFade));
        }
        // Compare in the same age coordinates as the atlas writer. Adding a short ramp to a large absolute
        // server time can round its end down and freeze a not-quite-mature density on long-running servers.
        bool changed = _layoutChanged || geometry || old is not null &&
            (now < old.Time || now != old.Time && (now-cloud.CreatedAt < matureAge || old.Time-cloud.CreatedAt < matureAge));
        if (old is null) _layers[layer] = new(cloud,now,matureAge);
        else { old.Cloud = cloud; old.Time = now; old.MatureAge = matureAge; }
        return changed;
    }

    public void Clear() { _layers.Clear(); _resource = null; }
}
