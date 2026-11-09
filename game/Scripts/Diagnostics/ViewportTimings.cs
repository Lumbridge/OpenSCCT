using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace OpenScctGame.Diagnostics;

/// <summary>
/// The auxiliary SubViewports render code creates (sky zone, spot shadows, flare cube faces), so the FPS benchmark
/// can measure their GPU and CPU render time per kind. Registering only stores the reference; freed viewports are
/// dropped through <see cref="GodotObject.IsInstanceValid"/>.
/// </summary>
public static class ViewportTimings
{
    public const string Sky = "sky";
    public const string SpotLive = "spot-live";
    public const string SpotStatic = "spot-static";
    public const string FlareFace = "flare-face";

    /// <summary>The reported kinds, in column order. Unknown kinds count as "other".</summary>
    public static IReadOnlyList<string> Kinds { get; } = [Sky, SpotLive, SpotStatic, FlareFace, "other"];

    private sealed class Entry(SubViewport viewport, int kind)
    {
        public readonly SubViewport Viewport = viewport;
        public readonly int Kind = kind;
        public Rid Rid;
        public bool Measuring;
        public double LastCpu;
        public double LastGpu;
    }

    private static readonly Lock s_lock = new();
    private static readonly List<Entry> s_entries = [];
    private static int s_pruneAt = 64;
    private static bool s_measuring;

    public static void Register(SubViewport viewport, string kind)
    {
        int index = 0;
        while (index < Kinds.Count - 1 && Kinds[index] != kind) index++;
        lock (s_lock)
        {
            if (s_entries.Count >= s_pruneAt)
            {
                s_entries.RemoveAll(e => !GodotObject.IsInstanceValid(e.Viewport));
                s_pruneAt = Math.Max(64, s_entries.Count * 2);
            }
            s_entries.Add(new Entry(viewport, index));
        }
    }

    /// <summary>The registered viewports still alive (SVM_RD_PROTO turns their rendering off, RdProtoRenderer).</summary>
    public static List<SubViewport> Alive()
    {
        lock (s_lock) return s_entries.FindAll(e => GodotObject.IsInstanceValid(e.Viewport)).ConvertAll(e => e.Viewport);
    }

    public static void Unregister(SubViewport viewport)
    {
        lock (s_lock) s_entries.RemoveAll(e => ReferenceEquals(e.Viewport, viewport));
    }

    /// <summary>Starts or stops render-time measurement on every registered viewport (later ones join at the next sample).</summary>
    public static void SetMeasuring(bool on)
    {
        lock (s_lock)
        {
            s_measuring = on;
            if (on) return;
            foreach (Entry e in s_entries)
            {
                if (e.Measuring && GodotObject.IsInstanceValid(e.Viewport))
                    RenderingServer.ViewportSetMeasureRenderTime(e.Rid, false);
                e.Measuring = false;
            }
        }
    }

    /// <summary>
    /// Adds this frame's GPU and CPU ms and the number of viewports that rendered, per kind (arrays indexed like
    /// <see cref="Kinds"/>). Godot keeps a viewport's last measured time while it is idle, so a viewport counts only
    /// when its measured time is above zero and changed since the previous sample. Godot reads timestamps back a few
    /// frames late and keeps them only for viewports that also draw in the frame of the read-back, so a viewport drawn
    /// once (spot-static) or every few frames (flare faces) may never report; its count then stays 0.
    /// </summary>
    public static void Sample(Span<double> gpuMs, Span<double> cpuMs, Span<int> count)
    {
        lock (s_lock)
        {
            if (!s_measuring) return;
            foreach (Entry e in s_entries)
            {
                if (!GodotObject.IsInstanceValid(e.Viewport)) continue;
                if (!e.Measuring)
                {
                    e.Rid = e.Viewport.GetViewportRid();
                    RenderingServer.ViewportSetMeasureRenderTime(e.Rid, true);
                    e.Measuring = true;
                    // Any value already held is from an earlier run, not this frame.
                    e.LastGpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(e.Rid);
                    e.LastCpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(e.Rid);
                    continue;
                }
                double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(e.Rid);
                double cpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(e.Rid);
                // Exact float compare on purpose: an unchanged value means no new measurement arrived.
                bool fresh = gpu > 0 && (gpu != e.LastGpu || cpu != e.LastCpu);
                e.LastGpu = gpu;
                e.LastCpu = cpu;
                if (!fresh) continue;
                gpuMs[e.Kind] += gpu;
                cpuMs[e.Kind] += cpu;
                count[e.Kind]++;
            }
        }
    }
}
