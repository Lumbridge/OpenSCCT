using System;
using System.Buffers.Binary;

namespace OpenScctGame.Audio;

/// <summary>
/// RIFF WAVE chunk fixes before a file is handed to Godot's loader. Four of the original's waves (objective jingles
/// and a Krauser ambience) end with a sampler chunk ("smpl") that lists no loop. Godot's loader reads one loop entry
/// past such a chunk (the end of the file, "Reading less data than requested") and turns the jingle into a forward
/// loop from 0 to 0. The original plays them once: the empty sampler chunk is dropped and the RIFF size fixed.
/// </summary>
public static class WavChunks
{
    /// <summary>
    /// <paramref name="file"/> without its sampler chunks that hold no loop (the same array when it has none).
    /// </summary>
    public static byte[] WithoutEmptySampler(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 12 || !Is(file, 0, "RIFF") || !Is(file, 8, "WAVE"))
        {
            return file;
        }

        int at = 12;
        while (at + 8 <= file.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            long next = at + 8L + size + (size & 1);
            if (Is(file, at, "smpl") && size >= 36 && at + 8 + 36 <= file.Length &&
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 8 + 28)) == 0)
            {
                int end = (int)Math.Min(next, file.Length);
                var cut = new byte[file.Length - (end - at)];
                file.AsSpan(0, at).CopyTo(cut);
                file.AsSpan(end).CopyTo(cut.AsSpan(at));
                BinaryPrimitives.WriteUInt32LittleEndian(cut.AsSpan(4), (uint)(cut.Length - 8));
                return WithoutEmptySampler(cut);
            }

            if (next > file.Length)
            {
                break;
            }

            at = (int)next;
        }

        return file;
    }

    /// <summary>True when the file has a sampler chunk that lists no loop.</summary>
    public static bool HasEmptySampler(byte[] file) => !ReferenceEquals(WithoutEmptySampler(file), file);

    private static bool Is(byte[] b, int at, string id) =>
        at + 4 <= b.Length && b[at] == id[0] && b[at + 1] == id[1] && b[at + 2] == id[2] && b[at + 3] == id[3];
}
