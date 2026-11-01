using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using OpenScctGame.Render.Gltf;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class GlbColourTests
{
    [Theory]
    [InlineData(5121, 3)]
    [InlineData(5121, 4)]
    [InlineData(5123, 3)]
    [InlineData(5123, 4)]
    [InlineData(5126, 3)]
    [InlineData(5126, 4)]
    public void RgbAndRgbaAccessorsRespectComponentFormatStrideAndOffsets(int componentType, int components)
    {
        int size = componentType == 5121 ? 1 : componentType == 5123 ? 2 : 4;
        int stride = (size * components + 7) & ~3;
        byte[] bin = new byte[12 + 2 * stride];
        float[] values = [0, 0.5f, 1, 0.25f, 1, 0, 0.25f, 0.5f];
        for (int i = 0; i < 2; i++)
            for (int c = 0; c < components; c++)
            {
                int at = 12 + i * stride + c * size;
                float value = values[i * 4 + c];
                if (size == 1) bin[at] = (byte)MathF.Round(value * 255);
                else if (size == 2) BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(at), (ushort)MathF.Round(value * 65535));
                else BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(at), value);
            }
        string json = JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            buffers = new[] { new { byteLength = bin.Length } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 8, byteLength = bin.Length - 8, byteStride = stride } },
            accessors = new[] { new { bufferView = 0, byteOffset = 4, componentType, count = 2, type = "VEC" + components, normalized = size != 4 } },
        });
        byte[] utf8 = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) & ~3));
        string path = Path.Combine(Path.GetTempPath(), "svm-colours-" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x46546C67u); writer.Write(2); writer.Write(28 + utf8.Length + bin.Length);
                writer.Write(utf8.Length); writer.Write(0x4E4F534Au); writer.Write(utf8);
                writer.Write(bin.Length); writer.Write(0x004E4942u); writer.Write(bin);
            }
            using GlbDocument doc = GlbDocument.Load(path);
            Assert.Equal(new byte[] { 0, 128, 255, (byte)(components == 3 ? 255 : 64), 255, 0, 64, (byte)(components == 3 ? 255 : 128) }, doc.ReadColours(0));
        }
        finally { File.Delete(path); }
    }
}
