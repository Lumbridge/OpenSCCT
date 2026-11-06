using System;
using System.Diagnostics;

namespace OpenScct.Game.Equipment;

/// <summary>Cumulative opt-in measurements; allocation counts cover the executing managed thread.</summary>
public readonly record struct ResponsiveSmokeStageDiagnostics(long Calls, double Milliseconds, long AllocatedBytes);

public readonly record struct ResponsiveSmokeWorldDiagnostics(
    ResponsiveSmokeStageDiagnostics Register,
    ResponsiveSmokeStageDiagnostics GeometryRefresh,
    ResponsiveSmokeStageDiagnostics ClassifyFront,
    ResponsiveSmokeStageDiagnostics Exposure,
    ResponsiveSmokeStageDiagnostics Capture,
    ResponsiveSmokeStageDiagnostics Apply);

public sealed partial class ResponsiveSmokeWorld
{
    /// <summary>Enable before the measured throw; read cumulative values on the simulation thread or at its frame barrier.</summary>
    public bool DiagnosticsEnabled { get; set; }
    private ResponsiveSmokeStageDiagnostics _registerDiagnostics, _geometryDiagnostics, _frontDiagnostics,
        _exposureDiagnostics, _captureDiagnostics, _applyDiagnostics;
    public ResponsiveSmokeWorldDiagnostics Diagnostics => new(_registerDiagnostics, _geometryDiagnostics,
        _frontDiagnostics, _exposureDiagnostics, _captureDiagnostics, _applyDiagnostics);

    // Wall time is opt-in observation only. These counters never feed the simulation clock or gameplay state.
#pragma warning disable RS0030
    private long DiagnosticsStart(out long allocated)
    {
        allocated = DiagnosticsEnabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
        return DiagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    private static void DiagnosticsEnd(ref ResponsiveSmokeStageDiagnostics counters, long start, long allocated)
    {
        if (start == 0) return;
        counters = new(counters.Calls + 1,
            counters.Milliseconds + (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency),
            counters.AllocatedBytes + GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
#pragma warning restore RS0030
}
