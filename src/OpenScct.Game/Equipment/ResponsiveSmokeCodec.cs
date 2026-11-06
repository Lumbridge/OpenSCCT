using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

/// <summary>Bounded binary cloud payloads. The network transport supplies reliable chunking and revision ordering.</summary>
public static class ResponsiveSmokeCodec
{
    public const int MaximumPayloadBytes = 2 * 1024 * 1024;
    private const byte Version = 2;

    public static byte[] Encode(ResponsiveSmokeSnapshot snapshot)
    {
        Validate(snapshot);
        return Pack(w => { w.Write(Version); WriteHeader(w, snapshot); WriteBits(w, snapshot.Cells);
            foreach (ushort tick in snapshot.ActivationTicks) w.Write(tick); WriteCarves(w, snapshot.Carves); WriteBoundaries(w, snapshot.Boundaries); });
    }
    public static ResponsiveSmokeSnapshot Decode(byte[] payload) => Unpack(payload, r =>
    {
        CheckVersion(r); var h = ReadHeader(r); byte[] cells = ReadBits(r);
        var activation = new ushort[ResponsiveSmokeField.CellCount];
        for (int i = 0; i < activation.Length; i++) activation[i] = r.ReadUInt16();
        var result = new ResponsiveSmokeSnapshot(h.Revision, h.Created, h.Now, h.Origin, h.Center, cells, activation, ReadCarves(r), ReadBoundaries(r));
        Validate(result); return result;
    });
    public static byte[] EncodeDelta(ResponsiveSmokeSnapshot previous, ResponsiveSmokeSnapshot next)
    {
        Validate(previous); Validate(next);
        return Pack(w =>
        {
            w.Write(Version); w.Write(previous.Revision); WriteHeader(w, next);
            int changes = 0;
            for (int i = 0; i < next.Cells.Length; i++) if (previous.Cells[i] != next.Cells[i] || previous.ActivationTicks[i] != next.ActivationTicks[i]) changes++;
            w.Write(changes);
            for (int i = 0; i < next.Cells.Length; i++)
                if (previous.Cells[i] != next.Cells[i] || previous.ActivationTicks[i] != next.ActivationTicks[i])
                { w.Write((ushort)i); w.Write(next.Cells[i]); w.Write(next.ActivationTicks[i]); }
            WriteCarves(w, next.Carves);
            WriteBoundaryDelta(w, previous.Boundaries, next.Boundaries);
        });
    }
    public static ResponsiveSmokeSnapshot DecodeDelta(ResponsiveSmokeSnapshot previous, byte[] payload) => Unpack(payload, r =>
    {
        Validate(previous); CheckVersion(r);
        if (r.ReadUInt32() != previous.Revision) throw new InvalidDataException("Smoke delta baseline mismatch.");
        var h = ReadHeader(r);
        byte[] cells = (byte[])previous.Cells.Clone(); ushort[] activation = (ushort[])previous.ActivationTicks.Clone();
        int count = r.ReadInt32(); if (count < 0 || count > cells.Length) throw new InvalidDataException("Invalid smoke delta size.");
        int prior = -1;
        for (int i = 0; i < count; i++)
        {
            int index = r.ReadUInt16(); if (index <= prior || index >= cells.Length) throw new InvalidDataException("Invalid smoke delta index.");
            cells[index] = r.ReadByte(); activation[index] = r.ReadUInt16(); prior = index;
        }
        var result = new ResponsiveSmokeSnapshot(h.Revision, h.Created, h.Now, h.Origin, h.Center, cells, activation, ReadCarves(r), ReadBoundaryDelta(r, previous.Boundaries));
        Validate(result); return result;
    });
    public static byte[] EncodeCarves(IReadOnlyList<ResponsiveSmokeCarve> carves) => Pack(w => { w.Write(Version); WriteCarves(w, carves); });
    public static ResponsiveSmokeCarve[] DecodeCarves(byte[] payload) => Unpack(payload, r => { CheckVersion(r); return ReadCarves(r); });

    public static void Validate(ResponsiveSmokeSnapshot snapshot)
    {
        if (snapshot.Cells.Length != ResponsiveSmokeField.CellCount || snapshot.ActivationTicks.Length != ResponsiveSmokeField.CellCount ||
            !float.IsFinite(snapshot.CreatedAt) || !float.IsFinite(snapshot.CapturedAt) || snapshot.Revision == 0 ||
            !Finite(snapshot.Origin) || !Finite(snapshot.Center)) throw new InvalidDataException("Invalid smoke snapshot.");
        foreach (byte cell in snapshot.Cells) if (cell > 1) throw new InvalidDataException("Invalid smoke occupancy.");
        ValidateBoundaries(snapshot.Boundaries, snapshot.Cells);
        ValidateCarves(snapshot.Carves);
    }
    private static void ValidateCarves(IReadOnlyList<ResponsiveSmokeCarve> carves)
    {
        if (carves.Count > ResponsiveSmokeField.MaxCarves) throw new InvalidDataException("Too many smoke disturbances.");
        foreach (ResponsiveSmokeCarve c in carves)
        {
            if (c.Sequence == 0 || c.Kind > ResponsiveSmokeCarveKind.Blast || !Finite(c.Start) || !Finite(c.End) ||
                !float.IsFinite(c.CreatedAt) || !float.IsFinite(c.Radius) || c.Radius <= 0 || c.Radius > 300 ||
                !float.IsFinite(c.Feather) || c.Feather <= 0 || c.Feather > 25 || !float.IsFinite(c.Hold) || c.Hold < 0 || c.Hold > 1 ||
                !float.IsFinite(c.Refill) || c.Refill <= 0 || c.Refill > 2 ||
                c.Kind == ResponsiveSmokeCarveKind.Blast && c.AffectedCells?.Length != ResponsiveSmokeField.CellCount ||
                c.Kind == ResponsiveSmokeCarveKind.Bullet && c.AffectedCells is not null)
                throw new InvalidDataException("Invalid smoke disturbance.");
            if (c.AffectedCells is { } mask) foreach (byte cell in mask) if (cell > 1) throw new InvalidDataException("Invalid smoke blast mask.");
        }
    }
    private static bool Finite(FVector p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) &&
        MathF.Abs(p.X) <= 10_000_000 && MathF.Abs(p.Y) <= 10_000_000 && MathF.Abs(p.Z) <= 10_000_000;
    private static void WriteHeader(BinaryWriter w, ResponsiveSmokeSnapshot s)
    { w.Write(s.Revision); w.Write(s.CreatedAt); w.Write(s.CapturedAt); WriteVector(w, s.Origin); WriteVector(w, s.Center); }
    private static (uint Revision, float Created, float Now, FVector Origin, FVector Center) ReadHeader(BinaryReader r) =>
        (r.ReadUInt32(), r.ReadSingle(), r.ReadSingle(), ReadVector(r), ReadVector(r));
    private static void WriteVector(BinaryWriter w, FVector p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
    private static FVector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static void ValidateBoundaries(ResponsiveSmokeBoundaryCell[]? boundaries, byte[] cells)
    {
        if (boundaries is null) return;
        if (boundaries.Length > ResponsiveSmokeField.CellCount) throw new InvalidDataException("Too many smoke boundaries.");
        int prior = -1, total = 0;
        foreach (ResponsiveSmokeBoundaryCell b in boundaries)
        {
            if (b.Cell <= prior || b.Cell >= cells.Length || cells[b.Cell] != 0 || b.Planes.Length is < 1 or > ResponsiveSmokeField.MaxBoundaryPlanes)
                throw new InvalidDataException("Invalid smoke boundary cell.");
            prior = b.Cell; total += b.Planes.Length;
            foreach (ResponsiveSmokePlane p in b.Planes)
                if (!Finite(p.Normal) || MathF.Abs(p.Normal.SizeSquared() - 1f) > .002f ||
                    !float.IsFinite(p.Distance) || MathF.Abs(p.Distance) > 22f)
                    throw new InvalidDataException("Invalid smoke boundary plane.");
        }
        if (total > ResponsiveSmokeField.MaxTotalBoundaryPlanes) throw new InvalidDataException("Too many smoke boundary planes.");
    }
    private static void WriteBoundaries(BinaryWriter writer, ResponsiveSmokeBoundaryCell[]? boundaries)
    {
        writer.Write((ushort)(boundaries?.Length ?? 0));
        foreach (ResponsiveSmokeBoundaryCell b in boundaries ?? []) WriteBoundary(writer, b.Cell, b.Planes);
    }
    private static void WriteBoundary(BinaryWriter writer, int cell, ResponsiveSmokePlane[] planes)
    {
        writer.Write((ushort)cell); writer.Write((byte)planes.Length);
        foreach (ResponsiveSmokePlane plane in planes) { WriteVector(writer, plane.Normal); writer.Write(plane.Distance); }
    }
    private static ResponsiveSmokeBoundaryCell[] ReadBoundaries(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        if (count > ResponsiveSmokeField.CellCount) throw new InvalidDataException("Too many smoke boundaries.");
        var result = new ResponsiveSmokeBoundaryCell[count];
        int total = 0;
        for (int i = 0; i < count; i++) result[i] = ReadBoundary(reader, ref total, false);
        return result;
    }
    private static ResponsiveSmokeBoundaryCell ReadBoundary(BinaryReader reader, ref int total, bool allowRemoved)
    {
        ushort cell = reader.ReadUInt16(); int count = reader.ReadByte(); total += count;
        if (cell >= ResponsiveSmokeField.CellCount || count > ResponsiveSmokeField.MaxBoundaryPlanes ||
            count == 0 && !allowRemoved || total > ResponsiveSmokeField.MaxTotalBoundaryPlanes)
            throw new InvalidDataException("Invalid smoke boundary record.");
        var planes = new ResponsiveSmokePlane[count];
        for (int i = 0; i < count; i++) planes[i] = new(ReadVector(reader), reader.ReadSingle());
        return new(cell, planes);
    }
    private static void WriteBoundaryDelta(BinaryWriter writer, ResponsiveSmokeBoundaryCell[]? previous, ResponsiveSmokeBoundaryCell[]? next)
    {
        var before = new Dictionary<int, ResponsiveSmokePlane[]>();
        var after = new Dictionary<int, ResponsiveSmokePlane[]>();
        foreach (var b in previous ?? []) before.Add(b.Cell, b.Planes);
        foreach (var b in next ?? []) after.Add(b.Cell, b.Planes);
        var changes = new List<int>();
        for (int i = 0; i < ResponsiveSmokeField.CellCount; i++)
        {
            before.TryGetValue(i, out var a); after.TryGetValue(i, out var b);
            if (!(a ?? []).AsSpan().SequenceEqual(b ?? [])) changes.Add(i);
        }
        writer.Write((ushort)changes.Count);
        foreach (int cell in changes) WriteBoundary(writer, cell, after.GetValueOrDefault(cell) ?? []);
    }
    private static ResponsiveSmokeBoundaryCell[] ReadBoundaryDelta(BinaryReader reader, ResponsiveSmokeBoundaryCell[]? previous)
    {
        int count = reader.ReadUInt16();
        if (count > ResponsiveSmokeField.CellCount) throw new InvalidDataException("Too many smoke boundary changes.");
        var cells = new SortedDictionary<int, ResponsiveSmokePlane[]>();
        foreach (var b in previous ?? []) cells.Add(b.Cell, (ResponsiveSmokePlane[])b.Planes.Clone());
        int total = 0, prior = -1;
        for (int i = 0; i < count; i++)
        {
            ResponsiveSmokeBoundaryCell b = ReadBoundary(reader, ref total, true);
            if (b.Cell <= prior) throw new InvalidDataException("Invalid smoke boundary delta order.");
            prior = b.Cell;
            if (b.Planes.Length == 0) cells.Remove(b.Cell); else cells[b.Cell] = b.Planes;
        }
        var result = new ResponsiveSmokeBoundaryCell[cells.Count]; int j = 0;
        foreach (var b in cells) result[j++] = new((ushort)b.Key, b.Value);
        return result;
    }
    private static void WriteCarves(BinaryWriter w, IReadOnlyList<ResponsiveSmokeCarve> carves)
    {
        ValidateCarves(carves); w.Write((ushort)carves.Count);
        foreach (ResponsiveSmokeCarve c in carves)
        {
            w.Write(c.Sequence); w.Write((byte)c.Kind); WriteVector(w, c.Start); WriteVector(w, c.End);
            w.Write(c.Radius); w.Write(c.Feather); w.Write(c.CreatedAt); w.Write(c.Hold); w.Write(c.Refill);
            if (c.AffectedCells is { } mask) WriteBits(w, mask);
        }
    }
    private static ResponsiveSmokeCarve[] ReadCarves(BinaryReader r)
    {
        int count = r.ReadUInt16(); if (count > ResponsiveSmokeField.MaxCarves) throw new InvalidDataException("Too many smoke disturbances.");
        var result = new ResponsiveSmokeCarve[count];
        for (int i = 0; i < count; i++)
        {
            uint sequence = r.ReadUInt32(); var kind = (ResponsiveSmokeCarveKind)r.ReadByte();
            FVector start = ReadVector(r), end = ReadVector(r);
            float radius = r.ReadSingle(), feather = r.ReadSingle(), created = r.ReadSingle(), hold = r.ReadSingle(), refill = r.ReadSingle();
            result[i] = new(sequence, kind, start, end, radius, feather, created, hold, refill, kind == ResponsiveSmokeCarveKind.Blast ? ReadBits(r) : null);
        }
        ValidateCarves(result); return result;
    }
    private static void WriteBits(BinaryWriter w, byte[] cells)
    {
        for (int i = 0; i < cells.Length; i += 8)
        {
            byte bits = 0; for (int j = 0; j < 8 && i + j < cells.Length; j++) bits |= (byte)(cells[i + j] << j);
            w.Write(bits);
        }
    }
    private static byte[] ReadBits(BinaryReader r)
    {
        var cells = new byte[ResponsiveSmokeField.CellCount];
        for (int i = 0; i < cells.Length; i += 8)
        {
            byte bits = r.ReadByte(); for (int j = 0; j < 8 && i + j < cells.Length; j++) cells[i + j] = (byte)((bits >> j) & 1);
        }
        return cells;
    }
    private static void CheckVersion(BinaryReader r) { if (r.ReadByte() != Version) throw new InvalidDataException("Unsupported smoke payload version."); }
    private static byte[] Pack(Action<BinaryWriter> write)
    {
        using var raw = new MemoryStream(); using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true)) write(writer);
        if (raw.Length > MaximumPayloadBytes) throw new InvalidDataException("Smoke payload is too large.");
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(compressor); }
        return output.ToArray();
    }
    private static T Unpack<T>(byte[] payload, Func<BinaryReader, T> read)
    {
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Invalid smoke payload size.");
        try
        {
            using var input = new MemoryStream(payload, false); using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
            using var raw = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = decompressor.Read(buffer)) != 0)
            { if (raw.Length + count > MaximumPayloadBytes) throw new InvalidDataException("Smoke payload expands beyond its limit."); raw.Write(buffer, 0, count); }
            raw.Position = 0; using var reader = new BinaryReader(raw); T value = read(reader);
            if (raw.Position != raw.Length) throw new InvalidDataException("Trailing smoke payload data."); return value;
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Incomplete smoke payload.", e); }
    }
}
