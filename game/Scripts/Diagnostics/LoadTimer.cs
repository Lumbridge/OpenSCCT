using System;
using System.Diagnostics;
using Godot;

namespace OpenScctGame.Diagnostics;

/// <summary>
/// Always-on map load timing: one <c>SVM_LOAD</c> line per load phase, from the player starting a load to the first
/// frame he controls his pawn. Calls outside a load do nothing. Marks may come from other threads; the Godot
/// monitors are read on the thread that began the load only.
/// </summary>
public static class LoadTimer
{
    private const double MiB = 1024.0 * 1024.0;
    private static readonly object s_lock = new();
    private static bool s_running;
    private static int s_id;
    private static int s_mainThread;
    private static long s_begin;
    private static long s_last;
    private static double s_pipelines;
    private static readonly System.Collections.Generic.SortedDictionary<string, (long Ticks, int Calls)> s_sums = new(StringComparer.Ordinal);

    /// <summary>True between <see cref="Begin"/> and <see cref="End"/> (or <see cref="Abandon"/>).</summary>
    public static bool Running
    {
        get
        {
            lock (s_lock) return s_running;
        }
    }

    /// <summary>The number of the current (or last) load; <see cref="Abandon"/> takes it.</summary>
    public static int Id
    {
        get
        {
            lock (s_lock) return s_id;
        }
    }

    /// <summary>Starts timing a load of <paramref name="what"/>; a load already timed starts over.</summary>
    public static void Begin(string what)
    {
        lock (s_lock)
        {
            s_running = true;
            s_id++;
            s_mainThread = System.Environment.CurrentManagedThreadId;
            s_begin = s_last = Stopwatch.GetTimestamp();
            s_sums.Clear();
            s_pipelines = Pipelines();
            OpenScct.Server.ClientSession.LoadPhase = Mark;
            GD.Print(FormattableString.Invariant(
                $"SVM_LOAD begin {what} texmem={TextureMiB():F0} ws={WorkingSetMiB():F0}"));
        }
    }

    /// <summary>Prints the time since the last mark as <paramref name="phase"/>.</summary>
    public static void Mark(string phase)
    {
        lock (s_lock)
        {
            if (!s_running) return;
            long now = Stopwatch.GetTimestamp();
            double ms = Stopwatch.GetElapsedTime(s_last, now).TotalMilliseconds;
            double total = Stopwatch.GetElapsedTime(s_begin, now).TotalMilliseconds;
            s_last = now;
            string godot = System.Environment.CurrentManagedThreadId == s_mainThread
                ? FormattableString.Invariant($"pipelines={Pipelines() - s_pipelines:F0} texmem={TextureMiB():F0}")
                : "pipelines=- texmem=-";
            foreach ((string what, (long ticks, int calls)) in s_sums)
            {
                GD.Print(FormattableString.Invariant($"SVM_LOAD sum {what} ms={ticks * 1000.0 / Stopwatch.Frequency:F1} calls={calls}"));
            }

            s_sums.Clear();
            GD.Print(FormattableString.Invariant($"SVM_LOAD {phase} ms={ms:F1} total={total:F1} {godot} ws={WorkingSetMiB():F0}"));
        }
    }

    /// <summary>
    /// Adds the time since <paramref name="since"/> (a <see cref="Stopwatch.GetTimestamp"/>) to the load's total for
    /// <paramref name="what"/>: work done in many small calls across phases. Each <see cref="Mark"/> prints, as
    /// <c>SVM_LOAD sum</c> lines, the totals gathered since the mark before it.
    /// </summary>
    public static void Add(string what, long since)
    {
        long ticks = Stopwatch.GetTimestamp() - since;
        lock (s_lock)
        {
            if (!s_running) return;
            s_sums.TryGetValue(what, out (long Ticks, int Calls) sum);
            s_sums[what] = (sum.Ticks + ticks, sum.Calls + 1);
        }
    }

    /// <summary>Marks <paramref name="phase"/> and ends the load with a <c>done</c> line.</summary>
    public static void End(string phase)
    {
        lock (s_lock)
        {
            if (!s_running) return;
            Mark(phase);
            s_running = false;
            GD.Print(FormattableString.Invariant(
                $"SVM_LOAD done total={Stopwatch.GetElapsedTime(s_begin).TotalMilliseconds:F1}"));
        }
    }

    /// <summary>Stops load <paramref name="id"/> if it is still timed (its scene left before the player got control).</summary>
    public static void Abandon(int id)
    {
        lock (s_lock)
        {
            if (!s_running || s_id != id) return;
            s_running = false;
            GD.Print(FormattableString.Invariant(
                $"SVM_LOAD abandoned total={Stopwatch.GetElapsedTime(s_begin).TotalMilliseconds:F1}"));
        }
    }

    /// <summary>One skeletal mesh template's build times (SkeletalModel), while a load is timed.</summary>
    public static void Glb(string path, double jsonMs, double gltfMs, double materialsMs, int textures)
    {
        lock (s_lock)
        {
            if (!s_running) return;
            GD.Print(FormattableString.Invariant(
                $"SVM_LOAD glb {path} json={jsonMs:F1} gltf={gltfMs:F1} materials={materialsMs:F1} textures={textures}"));
        }
    }

    private static double Pipelines() =>
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas)
        + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)
        + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface)
        + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)
        + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);

    private static double TextureMiB() => Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / MiB;

    // Not Process.PrivateMemorySize64: that snapshots every process on the machine (milliseconds per call).
    private static double WorkingSetMiB() => System.Environment.WorkingSet / MiB;
}
