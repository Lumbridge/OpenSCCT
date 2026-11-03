using System;
using System.IO;
using OpenScct.Core;

namespace OpenScct.Net;

/// <summary>A rendered player view. It is replay presentation data and never drives authoritative gameplay.</summary>
public readonly record struct DemoViewFrame(uint Tick, int PlayerId, int PawnId, FVector Location,
    FRotator Rotation, float Fov, FRotator Aim, float Breath, float Accuracy, bool SpyFirstPerson,
    bool FirstPersonBody = true, byte ViewSource = 0)
{
    public const int Bytes = 61;

    public bool IsValid => Finite(Location.X) && Finite(Location.Y) && Finite(Location.Z) &&
        float.IsFinite(Fov) && Fov is > 1f and < 180f && Finite(Breath) && Finite(Accuracy) && ViewSource < 16;

    private static bool Finite(float value) => float.IsFinite(value) && MathF.Abs(value) <= 10_000_000f;

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Tick); writer.Write(PlayerId); writer.Write(PawnId);
        writer.Write(Location.X); writer.Write(Location.Y); writer.Write(Location.Z);
        writer.Write(Rotation.Pitch); writer.Write(Rotation.Yaw); writer.Write(Rotation.Roll);
        writer.Write(Fov);
        writer.Write(Aim.Pitch); writer.Write(Aim.Yaw); writer.Write(Aim.Roll);
        writer.Write(Breath); writer.Write(Accuracy);
        writer.Write((byte)((SpyFirstPerson ? 1 : 0) | (FirstPersonBody ? 2 : 0) | (ViewSource << 2)));
    }

    internal static DemoViewFrame Read(BinaryReader reader)
    {
        var frame = new DemoViewFrame(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(),
            new FVector(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            new FRotator(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()), reader.ReadSingle(),
            new FRotator(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()), reader.ReadSingle(), reader.ReadSingle(), false);
        byte flags = reader.ReadByte();
        return frame with { SpyFirstPerson = (flags & 1) != 0, FirstPersonBody = (flags & 2) != 0, ViewSource = (byte)(flags >> 2) };
    }

    /// <summary>Small control-message payload; identity and recording tick are replaced by the server.</summary>
    public string Encode()
    {
        using var buffer = new MemoryStream(Bytes);
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true)) Write(writer);
        return Convert.ToBase64String(buffer.GetBuffer(), 0, Bytes);
    }

    public static bool TryDecode(string text, out DemoViewFrame frame)
    {
        frame = default;
        if (text.Length != 84) return false;
        Span<byte> data = stackalloc byte[Bytes];
        if (!Convert.TryFromBase64String(text, data, out int count) || count != Bytes) return false;
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        frame = Read(reader);
        return frame.IsValid;
    }

    internal static DemoViewFrame Blend(DemoViewFrame a, DemoViewFrame b, uint tick)
    {
        float alpha = (float)(tick - a.Tick) / (b.Tick - a.Tick);
        return a with
        {
            Tick = tick, Location = a.Location + (b.Location - a.Location) * alpha,
            Rotation = new FRotator(Angle(a.Rotation.Pitch, b.Rotation.Pitch, alpha),
                Angle(a.Rotation.Yaw, b.Rotation.Yaw, alpha), Angle(a.Rotation.Roll, b.Rotation.Roll, alpha)),
            Fov = a.Fov + (b.Fov - a.Fov) * alpha,
            Aim = new FRotator(Angle(a.Aim.Pitch, b.Aim.Pitch, alpha), Angle(a.Aim.Yaw, b.Aim.Yaw, alpha), Angle(a.Aim.Roll, b.Aim.Roll, alpha)),
            Breath = a.Breath + (b.Breath - a.Breath) * alpha, Accuracy = a.Accuracy + (b.Accuracy - a.Accuracy) * alpha,
        };
    }

    private static int Angle(int a, int b, float alpha) => a + (int)(unchecked((short)(b - a)) * alpha);
}
