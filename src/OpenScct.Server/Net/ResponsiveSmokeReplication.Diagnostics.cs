using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenScct.Game.Equipment;

namespace OpenScct.Server;

/// <summary>Flush is inclusive of server capture/encoding; codec stages isolate that work and must not be added to Flush.</summary>
public readonly record struct ResponsiveSmokeReplicationDiagnostics(
    ResponsiveSmokeStageDiagnostics Flush,
    ResponsiveSmokeStageDiagnostics BaselineEncode,
    ResponsiveSmokeStageDiagnostics DeltaEncode,
    ResponsiveSmokeStageDiagnostics CarveEncode,
    ResponsiveSmokeStageDiagnostics BaselineDecode,
    ResponsiveSmokeStageDiagnostics DeltaDecode,
    ResponsiveSmokeStageDiagnostics CarveDecode);

public sealed partial class ResponsiveSmokeReplication
{
    /// <summary>Enable before measuring; take snapshots on the network thread or at its frame barrier.</summary>
    public bool DiagnosticsEnabled { get; set; }
    private ResponsiveSmokeStageDiagnostics _flushDiagnostics, _baselineEncodeDiagnostics, _deltaEncodeDiagnostics,
        _carveEncodeDiagnostics, _baselineDecodeDiagnostics, _deltaDecodeDiagnostics, _carveDecodeDiagnostics;
    public ResponsiveSmokeReplicationDiagnostics Diagnostics => new(_flushDiagnostics, _baselineEncodeDiagnostics,
        _deltaEncodeDiagnostics, _carveEncodeDiagnostics, _baselineDecodeDiagnostics, _deltaDecodeDiagnostics, _carveDecodeDiagnostics);

    // Wall time is opt-in observation only. These counters never affect replication or simulation decisions.
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

    private byte[] EncodeBaseline(ResponsiveSmokeSnapshot snapshot)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.Encode(snapshot); }
        finally { DiagnosticsEnd(ref _baselineEncodeDiagnostics, start, allocated); }
    }
    private byte[] EncodeDelta(ResponsiveSmokeSnapshot previous, ResponsiveSmokeSnapshot current)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.EncodeDelta(previous, current); }
        finally { DiagnosticsEnd(ref _deltaEncodeDiagnostics, start, allocated); }
    }
    private byte[] EncodeCarves(IReadOnlyList<ResponsiveSmokeCarve> carves)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.EncodeCarves(carves); }
        finally { DiagnosticsEnd(ref _carveEncodeDiagnostics, start, allocated); }
    }
    private ResponsiveSmokeSnapshot DecodeBaseline(byte[] bytes)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.Decode(bytes); }
        finally { DiagnosticsEnd(ref _baselineDecodeDiagnostics, start, allocated); }
    }
    private ResponsiveSmokeSnapshot DecodeDelta(ResponsiveSmokeSnapshot previous, byte[] bytes)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.DecodeDelta(previous, bytes); }
        finally { DiagnosticsEnd(ref _deltaDecodeDiagnostics, start, allocated); }
    }
    private ResponsiveSmokeCarve[] DecodeCarves(byte[] bytes)
    {
        long start = DiagnosticsStart(out long allocated);
        try { return ResponsiveSmokeCodec.DecodeCarves(bytes); }
        finally { DiagnosticsEnd(ref _carveDecodeDiagnostics, start, allocated); }
    }
}
