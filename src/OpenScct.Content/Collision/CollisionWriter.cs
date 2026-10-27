using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenScct.Core;

namespace OpenScct.Content.Collision;

/// <summary>
/// Writes <c>sim/collision.bin</c> 1.0: the file header and exactly the ten 1.0 chunks in order, zero padded to
/// 16 bytes, reserved fields 0. Output is byte-identical to the pipeline's writer for the same data. Unknown
/// chunks are not written.
/// </summary>
public static class CollisionWriter
{
    public static byte[] Write(CollisionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Validate(file: null);
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[16];
        CollisionReader.Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], CollisionReader.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], CollisionReader.Minor);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)CollisionReader.ChunkOrder.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0);
        stream.Write(header);

        foreach (string fourcc in CollisionReader.ChunkOrder)
        {
            (int count, byte[] payload) = Payload(data, fourcc);
            Encoding.ASCII.GetBytes(fourcc, header);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], CollisionReader.ChunkVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)count);
            stream.Write(header);
            stream.Write(payload);
            int pad = (int)((16 - (stream.Length % 16)) % 16);
            for (int i = 0; i < pad; i++)
            {
                stream.WriteByte(0);
            }
        }

        return stream.ToArray();
    }

    private static (int Count, byte[] Payload) Payload(CollisionData d, string fourcc)
    {
        switch (fourcc)
        {
            case "STRS":
            {
                var offsets = new byte[4 * d.Strings.Length];
                var blob = new List<byte>();
                for (int i = 0; i < d.Strings.Length; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(offsets.AsSpan(4 * i), (uint)blob.Count);
                    blob.AddRange(Encoding.UTF8.GetBytes(d.Strings[i]));
                    blob.Add(0);
                }

                var payload = new byte[offsets.Length + blob.Count];
                offsets.CopyTo(payload, 0);
                blob.CopyTo(payload, offsets.Length);
                return (d.Strings.Length, payload);
            }

            case "VERT":
                return Rows(d.Vertices.AsSpan(), 12, static (Span<byte> p, in FVector v) => Vec(p, v));
            case "TRIS":
                return Rows(d.Triangles.AsSpan(), 16, static (Span<byte> p, in CollisionTriangle t) =>
                {
                    U32(p, 0, t.I0);
                    U32(p, 4, t.I1);
                    U32(p, 8, t.I2);
                    U16(p, 12, t.Collider);
                    U16(p, 14, t.Material);
                });
            case "TFLG":
                return Rows(d.Materials.AsSpan(), 8, static (Span<byte> p, in CollisionMaterial m) =>
                {
                    p[0] = m.SurfaceType;
                    U16(p, 2, (ushort)m.Flags);
                    U32(p, 4, m.Name);
                });
            case "PLNS":
                return Rows(d.Planes.AsSpan(), 16, static (Span<byte> p, in CollisionPlane pl) => Plane(p, pl));
            case "HULL":
                return Rows(d.Hulls.AsSpan(), 48, static (Span<byte> p, in CollisionHull h) =>
                {
                    U32(p, 0, h.PlaneStart);
                    U32(p, 4, h.PlaneCount);
                    U32(p, 8, h.VertStart);
                    U32(p, 12, h.VertCount);
                    U16(p, 16, h.Material);
                    Vec(p[24..], h.Min);
                    Vec(p[36..], h.Max);
                });
            case "BSPN":
                return Rows(d.BspNodes.AsSpan(), 48, static (Span<byte> p, in CollisionBspNode n) =>
                {
                    Plane(p, n.Plane);
                    I32(p, 16, n.Front);
                    I32(p, 20, n.Back);
                    I32(p, 24, n.Coplanar);
                    I32(p, 28, n.CollisionBound);
                    U32(p, 32, n.TriStart);
                    U32(p, 36, n.TriCount);
                    U16(p, 40, n.NodeFlags);
                    p[42] = n.ZoneFront;
                    p[43] = n.ZoneBack;
                });
            case "COLL":
                return Rows(d.Colliders.AsSpan(), 128, static (Span<byte> p, in Collider c) =>
                {
                    p[0] = (byte)c.Kind;
                    U16(p, 2, (ushort)c.Flags);
                    U32(p, 4, c.Actor);
                    U32(p, 8, c.Parent);
                    U32(p, 16, c.VertStart);
                    U32(p, 20, c.VertCount);
                    U32(p, 24, c.TriStart);
                    U32(p, 28, c.TriCount);
                    U32(p, 32, c.BspStart);
                    U32(p, 36, c.BspCount);
                    U32(p, 40, c.HullStart);
                    U32(p, 44, c.HullCount);
                    Vec(p[48..], c.Location);
                    I32(p, 60, c.Rotation.Pitch);
                    I32(p, 64, c.Rotation.Yaw);
                    I32(p, 68, c.Rotation.Roll);
                    Vec(p[72..], c.Scale);
                    Vec(p[84..], c.PrePivot);
                    Vec(p[96..], c.AabbMin);
                    Vec(p[108..], c.AabbMax);
                });
            case "CYLS":
                return Rows(d.Cylinders.AsSpan(), 32, static (Span<byte> p, in CollisionCylinder c) =>
                {
                    U32(p, 0, c.Actor);
                    U16(p, 4, (ushort)c.Flags);
                    Vec(p[8..], c.Location);
                    F32(p, 20, c.Radius);
                    F32(p, 24, c.Height);
                });
            default:
                return Rows(d.Zones.AsSpan(), 32, static (Span<byte> p, in CollisionZone z) =>
                {
                    U32(p, 0, z.Actor);
                    U32(p, 4, (uint)z.Flags);
                    Vec(p[8..], z.AabbMin);
                    Vec(p[20..], z.AabbMax);
                });
        }
    }

    private delegate void RowWriter<T>(Span<byte> record, in T value);

    private static (int Count, byte[] Payload) Rows<T>(ReadOnlySpan<T> rows, int stride, RowWriter<T> write)
    {
        var payload = new byte[rows.Length * stride];
        for (int i = 0; i < rows.Length; i++)
        {
            write(payload.AsSpan(i * stride, stride), rows[i]);
        }

        return (rows.Length, payload);
    }

    private static void U32(Span<byte> p, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(p[at..], v);

    private static void I32(Span<byte> p, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(p[at..], v);

    private static void U16(Span<byte> p, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(p[at..], v);

    private static void F32(Span<byte> p, int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(p[at..], v);

    private static void Vec(Span<byte> p, FVector v)
    {
        F32(p, 0, v.X);
        F32(p, 4, v.Y);
        F32(p, 8, v.Z);
    }

    private static void Plane(Span<byte> p, CollisionPlane plane)
    {
        Vec(p, plane.Normal);
        F32(p, 12, plane.W);
    }
}
