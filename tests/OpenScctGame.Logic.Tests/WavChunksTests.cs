using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// The empty sampler chunk of some original waves (WavChunks): dropped before Godot loads the file, which would
/// otherwise read past the end and loop the one-shot.
/// </summary>
public sealed class WavChunksTests
{
    private static byte[] Wave(bool sampler, uint loops)
    {
        using var m = new MemoryStream();
        void Chunk(string id, byte[] body)
        {
            m.Write(System.Text.Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
            m.Write(size);
            m.Write(body);
        }

        m.Write("RIFF"u8);
        m.Write(new byte[4]);
        m.Write("WAVE"u8);
        var fmt = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), 22050);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(8), 44100);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), 16);
        Chunk("fmt ", fmt);
        Chunk("data", new byte[200]);
        if (sampler)
        {
            var smpl = new byte[36 + (loops * 24)];
            BinaryPrimitives.WriteUInt32LittleEndian(smpl.AsSpan(28), loops);
            Chunk("smpl", smpl);
        }

        byte[] bytes = m.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    [Fact]
    public void EmptySamplerChunkIsDropped()
    {
        byte[] withEmpty = Wave(sampler: true, loops: 0);
        byte[] cleaned = WavChunks.WithoutEmptySampler(withEmpty);
        Assert.Equal(Wave(sampler: false, loops: 0), cleaned);
        Assert.Equal((uint)(cleaned.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(cleaned.AsSpan(4)));
    }

    [Fact]
    public void SamplerWithLoopsAndPlainWavesAreKept()
    {
        byte[] looped = Wave(sampler: true, loops: 1);
        Assert.Same(looped, WavChunks.WithoutEmptySampler(looped));
        byte[] plain = Wave(sampler: false, loops: 0);
        Assert.Same(plain, WavChunks.WithoutEmptySampler(plain));
    }

    /// <summary>The converted library: the waves with an empty sampler chunk are the four the play logs warned about.</summary>
    [Fact]
    public void ConvertedWavesWithAnEmptySampler()
    {
        string root = Path.Combine(RepoRoot(), ".content", "shared", "audio", "wav");
        Assert.SkipWhen(!Directory.Exists(root), "no converted audio in .content/shared/audio");
        string[] hits = Directory.EnumerateFiles(root, "*.wav", SearchOption.AllDirectories)
            .Where(f => WavChunks.HasEmptySampler(File.ReadAllBytes(f)))
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Contains("Jingl_AttacB.wav", hits);
        Assert.Contains("Jingl_DefB.wav", hits);
        Assert.Contains("krau_blup1.wav", hits);
        foreach (string f in Directory.EnumerateFiles(root, "*.wav", SearchOption.AllDirectories))
        {
            Assert.False(WavChunks.HasEmptySampler(WavChunks.WithoutEmptySampler(File.ReadAllBytes(f))), f);
        }
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? Directory.GetCurrentDirectory();
    }
}
