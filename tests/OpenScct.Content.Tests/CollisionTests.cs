using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenScct.Content.Collision;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class CollisionTests
{
    private static byte[] TestBoxBytes() => TestPaths.TestBoxFile("sim/collision.bin");

    private static CollisionData TestBox() => CollisionReader.Read(TestBoxBytes());

    [Fact]
    public void TestBoxCounts()
    {
        CollisionData d = TestBox();
        Assert.Equal(19, d.Strings.Length);
        Assert.Equal(265, d.Vertices.Length);
        Assert.Equal(548, d.Triangles.Length);
        Assert.Equal(7, d.Materials.Length);
        Assert.Equal(12, d.Planes.Length);
        Assert.Equal(2, d.Hulls.Length);
        Assert.Empty(d.BspNodes);
        Assert.Equal(3, d.Colliders.Length);
        Assert.Equal(6, d.Cylinders.Length);
        Assert.Equal(4, d.Zones.Length);
        Assert.Empty(d.UnknownChunks);
    }

    [Fact]
    public void CollidersMaterialsCylindersAndZones()
    {
        CollisionData d = TestBox();
        Collider world = d.Colliders[0];
        Assert.Equal(ColliderKind.WorldBsp, world.Kind);
        Assert.Null(d.ActorOf(world));
        Assert.Equal(ColliderGeometry.Triangles, world.Geometry);
        Assert.True(world.HasIdentityTransform);
        Assert.Equal(548, d.TrianglesOf(world).Length);
        Assert.Equal(new FVector(-32f, -1568f, -32f), world.AabbMin);
        Assert.Equal((ColliderFlags)63, world.Flags);

        Collider mover = d.Colliders[1];
        Assert.Equal(ColliderKind.Mover, mover.Kind);
        Assert.Equal("Mover0", d.ActorOf(mover));
        Assert.Equal(ColliderGeometry.Hulls, mover.Geometry);
        Assert.Equal(new FVector(1280f, 512f, 0f), mover.Location);
        Assert.Equal(1, d.FindCollider("mover0"));

        Collider volume = d.Colliders[2];
        Assert.Equal(ColliderKind.Brush, volume.Kind);
        Assert.Equal(new FRotator(0, 16384, 0), volume.Rotation);
        Assert.Equal(ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockPlayers | ColliderFlags.BlockNonZeroExtent,
            volume.Flags);
        Assert.Equal("TestBox.Materials.Blocker", d.GetString(d.Materials[d.HullsOf(volume)[0].Material].Name));
        Assert.Equal(CollisionMaterialFlags.Invisible, d.Materials[5].Flags);
        Assert.Equal(CollisionMaterialFlags.Climbable, d.Materials[3].Flags);

        // Hull corners lie inside or on every hull plane; planes have unit normals.
        foreach (CollisionHull hull in d.Hulls)
        {
            foreach (CollisionPlane plane in d.PlanesOf(hull))
            {
                Assert.Equal(1f, plane.Normal.Size(), 5);
                foreach (FVector corner in d.CornersOf(hull))
                {
                    Assert.True(plane.PlaneDot(corner) <= 0.001f);
                }
            }
        }

        CollisionCylinder trigger = d.Cylinders[0];
        Assert.Equal("Trigger0", d.GetString(trigger.Actor));
        Assert.Equal(160f, trigger.Radius);
        Assert.Equal(64f, trigger.Height);
        Assert.Equal(ColliderFlags.CollideActors, trigger.Flags);

        Assert.Equal("LevelInfo0", d.GetString(d.Zones[0].Actor));
        Assert.Equal("ZoneInfo2", d.GetString(d.Zones[3].Actor));
        Assert.True(d.Zones[3].Contains(new FVector(512f, -1024f, 100f)));
        Assert.False(d.Zones[1].Contains(new FVector(512f, -1024f, 100f)));

        FVector n = d.TriangleNormal(0);
        Assert.Equal(0f, n.X);
        Assert.Equal(0f, n.Y);
        Assert.NotEqual(0f, n.Z);
    }

    [Fact]
    public void ActorNamesAreActorIdsOfTheGameplay()
    {
        GameplayDoc gameplay = GameplayDoc.Parse(TestPaths.TestBoxFile("sim/gameplay.json"));
        Assert.Empty(TestBox().CheckActorNames(gameplay));
    }

    [Fact]
    public void WriterReproducesTheFixtureByteForByte()
    {
        byte[] original = TestBoxBytes();
        Assert.Equal(original, CollisionWriter.Write(CollisionReader.Read(original)));
    }

    [Fact]
    public void HandBuiltDataRoundTrips()
    {
        var data = new CollisionData
        {
            Strings = ["Box0", "Mat.é"],
            Vertices = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f), new(-0f, 0.1f, 1e-30f)],
            Triangles = [new(0, 1, 2, 0, 0)],
            Materials = [new(3, CollisionMaterialFlags.Semisolid, 1)],
            Planes = [new(new FVector(0f, 0f, 1f), 2.5f)],
            Hulls = [new(0, 1, 0, 4, 0, new FVector(-1f, -1f, -1f), new FVector(1f, 1f, 1f))],
            BspNodes = [new(new CollisionPlane(new FVector(1f, 0f, 0f), 0f), -1, -1, -1, 0, 0, 1, 7, 1, 2)],
            Colliders =
            [
                new(ColliderKind.Gpo, ColliderFlags.StartsDisabled, 0, CollisionData.None, 0, 4, 0, 1, 0, 1, 0, 1,
                    new FVector(5f, 6f, 7f), new FRotator(-1, 70000, 3), new FVector(1f, 2f, 3f), new FVector(0.5f, 0f, 0f),
                    new FVector(-10f, -10f, -10f), new FVector(10f, 10f, 10f)),
            ],
            Cylinders = [new(0, ColliderFlags.CollideActors, new FVector(1f, 2f, 3f), 4f, 5f)],
            Zones = [new(CollisionData.None, CollisionZoneFlags.SkyZone, new FVector(-1f, -1f, -1f), new FVector(1f, 1f, 1f))],
        };
        byte[] bytes = CollisionWriter.Write(data);
        Assert.Equal(0, bytes.Length % 16);
        CollisionData back = CollisionReader.Read(bytes);
        Assert.Equal(data.Strings, back.Strings);
        Assert.Equal(data.Vertices, back.Vertices);
        Assert.Equal(data.Triangles, back.Triangles);
        Assert.Equal(data.Materials, back.Materials);
        Assert.Equal(data.Planes, back.Planes);
        Assert.Equal(data.Hulls, back.Hulls);
        Assert.Equal(data.BspNodes, back.BspNodes);
        Assert.Equal(data.Colliders, back.Colliders);
        Assert.Equal(data.Cylinders, back.Cylinders);
        Assert.Equal(data.Zones, back.Zones);
        Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(back.Vertices[3].X));
        Assert.Equal(bytes, CollisionWriter.Write(back));
        Assert.Equal(ColliderGeometry.Hulls, back.Colliders[0].Geometry);
    }

    [Fact]
    public void UnknownChunksAreSkippedBySize()
    {
        byte[] original = TestBoxBytes();
        byte[] withExtra = InsertChunk(original, "XTRA", 1, 3, [1, 2, 3, 4, 5]);
        CollisionData d = CollisionReader.Read(withExtra);
        CollisionUnknownChunk extra = Assert.Single(d.UnknownChunks);
        Assert.Equal("XTRA", extra.FourCC);
        Assert.Equal(3u, extra.Count);
        Assert.Equal([1, 2, 3, 4, 5], extra.Payload);
        Assert.Equal(548, d.Triangles.Length);
        Assert.Equal(original, CollisionWriter.Write(d));
    }

    [Fact]
    public void LaterMinorVersionsAreRead()
    {
        byte[] data = TestBoxBytes();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 3);
        Assert.Equal(3, CollisionReader.Read(data).Colliders.Length);
    }

    public static TheoryData<string, string> Corruptions() => new()
    {
        { "magic", "bad magic" },
        { "major", "unsupported major version" },
        { "flags", "header flags" },
        { "chunkVersion", "VERT version 2 is not supported" },
        { "duplicate", "duplicate chunk ZONE" },
        { "size", "VERT size" },
        { "padding", "padding is not zero" },
        { "vertexIndex", "triangle 0 vertex index out of range" },
        { "triangleOwner", "outside its collider" },
        { "material", "triangle 0 material" },
        { "nan", "vertex 0 is not finite" },
        { "inverted", "collider 0 bounds are inverted" },
        { "kind", "collider 0 kind 8 unknown" },
        { "reservedFlag", "collider 0 sets reserved flag bits" },
        { "stringIndex", "collider 1 actor string index" },
        { "cylinderActor", "cylinder 0 has no actor" },
        { "truncated", "truncated" },
        { "trailing", "trailing bytes" },
        { "missing", "not the 1.0 set in order" },
        { "badUtf8", "STRS string 0 is not UTF-8" },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void CorruptFilesAreRejected(string corruption, string reason)
    {
        byte[] data = Corrupt(TestBoxBytes(), corruption);
        var e = Assert.Throws<SvMapFormatException>(() => CollisionReader.Read(data));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.Equal("sim/collision.bin", e.File);
    }

    [Fact]
    public void LenientModeSkipsWriterRulesButKeepsRangeChecks()
    {
        CollisionData noZones = CollisionReader.Read(Corrupt(TestBoxBytes(), "missing"), strict: false);
        Assert.Empty(noZones.Zones);
        Assert.Equal(3, CollisionReader.Read(Corrupt(TestBoxBytes(), "trailing"), strict: false).Colliders.Length);
        Assert.Equal(3, CollisionReader.Read(Corrupt(TestBoxBytes(), "padding"), strict: false).Colliders.Length);
        Assert.Throws<SvMapFormatException>(() => CollisionReader.Read(Corrupt(TestBoxBytes(), "duplicate"), strict: false));
        Assert.Throws<SvMapFormatException>(() => CollisionReader.Read(Corrupt(TestBoxBytes(), "vertexIndex"), strict: false));
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private readonly record struct Chunk(string FourCC, int Header, int Payload, int Size, int Count);

    private static List<Chunk> Chunks(byte[] data)
    {
        var chunks = new List<Chunk>();
        int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        int pos = 16;
        for (int i = 0; i < n; i++)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 8));
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 12));
            chunks.Add(new Chunk(Encoding.ASCII.GetString(data, pos, 4), pos, pos + 16, size, count));
            pos = (pos + 16 + size + 15) & ~15;
        }

        return chunks;
    }

    private static Chunk Find(byte[] data, string fourcc) => Chunks(data).Single(c => c.FourCC == fourcc);

    private static byte[] InsertChunk(byte[] data, string fourcc, uint version, uint count, byte[] payload)
    {
        Chunk after = Find(data, "VERT");
        int at = (after.Payload + after.Size + 15) & ~15;
        int padded = (payload.Length + 15) & ~15;
        var chunk = new byte[16 + padded];
        Encoding.ASCII.GetBytes(fourcc, chunk.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), count);
        payload.CopyTo(chunk, 16);
        byte[] result = [.. data.AsSpan(0, at), .. chunk, .. data.AsSpan(at)];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(8)) + 1);
        return result;
    }

    private static byte[] Corrupt(byte[] data, string corruption)
    {
        Span<byte> d = data;
        switch (corruption)
        {
            case "magic":
                d[0] = (byte)'X';
                return data;
            case "major":
                BinaryPrimitives.WriteUInt16LittleEndian(d[4..], 2);
                return data;
            case "flags":
                BinaryPrimitives.WriteUInt32LittleEndian(d[12..], 1);
                return data;
            case "chunkVersion":
                BinaryPrimitives.WriteUInt32LittleEndian(d[(Find(data, "VERT").Header + 4)..], 2);
                return data;
            case "duplicate":
            {
                Chunk zone = Find(data, "ZONE");
                byte[] copy = data.AsSpan(zone.Header, data.Length - zone.Header).ToArray();
                byte[] result = [.. data, .. copy];
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 11);
                return result;
            }

            case "size":
                BinaryPrimitives.WriteUInt32LittleEndian(d[(Find(data, "VERT").Header + 12)..], (uint)(Find(data, "VERT").Count + 1));
                return data;
            case "padding":
            {
                Chunk strs = Find(data, "STRS");
                int end = strs.Payload + strs.Size;
                Assert.True(end % 16 != 0, "STRS needs padding for this case");
                d[end] = 1;
                return data;
            }

            case "vertexIndex":
                BinaryPrimitives.WriteUInt32LittleEndian(d[Find(data, "TRIS").Payload..], 100_000);
                return data;
            case "triangleOwner":
                BinaryPrimitives.WriteUInt16LittleEndian(d[(Find(data, "TRIS").Payload + 12)..], 1);
                return data;
            case "material":
                BinaryPrimitives.WriteUInt16LittleEndian(d[(Find(data, "TRIS").Payload + 14)..], 99);
                return data;
            case "nan":
                BinaryPrimitives.WriteSingleLittleEndian(d[Find(data, "VERT").Payload..], float.NaN);
                return data;
            case "inverted":
                BinaryPrimitives.WriteSingleLittleEndian(d[(Find(data, "COLL").Payload + 96)..], 1e9f);
                return data;
            case "kind":
                d[Find(data, "COLL").Payload] = 8;
                return data;
            case "reservedFlag":
                BinaryPrimitives.WriteUInt16LittleEndian(d[(Find(data, "COLL").Payload + 2)..], 0x100);
                return data;
            case "stringIndex":
                BinaryPrimitives.WriteUInt32LittleEndian(d[(Find(data, "COLL").Payload + 128 + 4)..], 500);
                return data;
            case "cylinderActor":
                BinaryPrimitives.WriteUInt32LittleEndian(d[Find(data, "CYLS").Payload..], CollisionData.None);
                return data;
            case "truncated":
                return data.AsSpan(0, Find(data, "ZONE").Payload + 16).ToArray();
            case "trailing":
                return [.. data, .. new byte[16]];
            case "missing":
            {
                Chunk zone = Find(data, "ZONE");
                byte[] result = data.AsSpan(0, zone.Header).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 9);
                return result;
            }

            case "badUtf8":
            {
                Chunk strs = Find(data, "STRS");
                d[strs.Payload + (4 * strs.Count)] = 0xC3;
                d[strs.Payload + (4 * strs.Count) + 1] = 0x28;
                return data;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
        }
    }
}
