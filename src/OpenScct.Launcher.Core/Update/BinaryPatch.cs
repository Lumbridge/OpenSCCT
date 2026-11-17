using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace OpenScct.Launcher.Update;

/// <summary>
/// Applies an OSCTDIF1 binary patch, made by OpenSCCT-Unreal's Tools/ReleaseDelta (the format's reference; this is a
/// port of its <c>Delta.Apply</c>). The format is bsdiff's: a list of controls (add, copy, seek), the bytes to add to
/// the old file's bytes, and the bytes that are new, in three zlib streams. Everything is little-endian.
/// <code>
///   "OSCTDIF1"            8 bytes
///   old size, new size    int64 each
///   old SHA-256           32 bytes
///   new SHA-256           32 bytes
///   control, diff, extra  int64 each: the length of each zlib stream
///   the three zlib streams, in that order
/// </code>
/// A control is three int64: add (bytes read from the diff stream, each added to the old file's byte at the old
/// position, both positions then advance), copy (bytes read from the extra stream, the new position advances) and
/// seek (moves the old position, may be negative). Old bytes outside the old file count as zero. The controls run
/// until the new file is complete.
/// </summary>
public static class BinaryPatch
{
    public const int HeaderSize = 8 + 8 + 8 + 32 + 32 + 8 + 8 + 8;

    private static ReadOnlySpan<byte> Magic => "OSCTDIF1"u8;

    /// <summary>What a patch's header says.</summary>
    public sealed record Header(long OldSize, long NewSize, string OldSha256, string NewSha256, long ControlLength, long DiffLength, long ExtraLength);

    /// <summary>Reads and checks a patch's header. Throws <see cref="InvalidDataException"/> when the file is not a whole OSCTDIF1 patch.</summary>
    public static Header ReadHeader(string patchPath)
    {
        using FileStream patch = File.OpenRead(patchPath);
        return ReadHeader(patch);
    }

    private static Header ReadHeader(FileStream patch)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        if (patch.Length < HeaderSize)
        {
            throw new InvalidDataException("not an OSCTDIF1 patch (too short)");
        }

        patch.ReadExactly(header);
        if (!header[..8].SequenceEqual(Magic))
        {
            throw new InvalidDataException("not an OSCTDIF1 patch");
        }

        var h = new Header(
            BitConverter.ToInt64(header[8..]),
            BitConverter.ToInt64(header[16..]),
            Convert.ToHexStringLower(header[24..56]),
            Convert.ToHexStringLower(header[56..88]),
            BitConverter.ToInt64(header[88..]),
            BitConverter.ToInt64(header[96..]),
            BitConverter.ToInt64(header[104..]));
        if (h.OldSize < 0 || h.NewSize < 0 || h.ControlLength < 0 || h.DiffLength < 0 || h.ExtraLength < 0
            || h.ControlLength > patch.Length || h.DiffLength > patch.Length || h.ExtraLength > patch.Length
            || HeaderSize + h.ControlLength + h.DiffLength + h.ExtraLength != patch.Length)
        {
            throw new InvalidDataException("the patch is damaged");
        }

        return h;
    }

    /// <summary>
    /// Writes the file <paramref name="patchPath"/> makes from <paramref name="oldPath"/> to <paramref name="newPath"/>.
    /// Checks the old file's size and SHA-256 first and the new file's at the end; on any failure throws
    /// <see cref="InvalidDataException"/> (or an <see cref="IOException"/>) and leaves no new or partial file. Every
    /// handle is closed before the finished file is moved into place, so <paramref name="newPath"/> may be the old file.
    /// </summary>
    public static void Apply(string oldPath, string patchPath, string newPath, CancellationToken cancel = default)
    {
        string temporary = newPath + ".partial";
        try
        {
            ApplyTo(oldPath, patchPath, temporary, cancel);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        File.Move(temporary, newPath, overwrite: true);
    }

    private static void ApplyTo(string oldPath, string patchPath, string output, CancellationToken cancel)
    {
        Header h;
        using (FileStream patch = File.OpenRead(patchPath))
        {
            h = ReadHeader(patch);
        }

        using FileStream old = new(oldPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        if (old.Length != h.OldSize)
        {
            throw new InvalidDataException("the old file is not the one the patch was made from (size)");
        }

        string oldHash = Convert.ToHexStringLower(SHA256.HashData(old));
        if (!string.Equals(oldHash, h.OldSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("the old file is not the one the patch was made from (SHA-256)");
        }

        cancel.ThrowIfCancellationRequested();
        using var controlZ = new ZLibStream(Slice(patchPath, HeaderSize, h.ControlLength), CompressionMode.Decompress);
        using var diffZ = new ZLibStream(Slice(patchPath, HeaderSize + h.ControlLength, h.DiffLength), CompressionMode.Decompress);
        using var extraZ = new ZLibStream(Slice(patchPath, HeaderSize + h.ControlLength + h.DiffLength, h.ExtraLength), CompressionMode.Decompress);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string madeHash;
        using (FileStream made = new(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            byte[] buffer = new byte[1 << 20];
            byte[] oldBuffer = new byte[1 << 20];
            Span<byte> triple = stackalloc byte[24];
            long newPos = 0, oldPos = 0;
            while (newPos < h.NewSize)
            {
                cancel.ThrowIfCancellationRequested();
                controlZ.ReadExactly(triple);
                long add = BitConverter.ToInt64(triple);
                long copy = BitConverter.ToInt64(triple[8..]);
                long seek = BitConverter.ToInt64(triple[16..]);
                if (add < 0 || copy < 0 || add > h.NewSize - newPos || copy > h.NewSize - newPos - add
                    || seek < -(h.OldSize + h.NewSize) || seek > h.OldSize + h.NewSize)
                {
                    throw new InvalidDataException("the patch is damaged (control out of range)");
                }

                for (long done = 0; done < add;)
                {
                    int n = (int)Math.Min(buffer.Length, add - done);
                    diffZ.ReadExactly(buffer, 0, n);
                    // Old bytes outside the old file count as zero (bsdiff's rule).
                    Array.Clear(oldBuffer, 0, n);
                    long from = oldPos + done;
                    long start = Math.Max(from, 0), end = Math.Min(from + n, h.OldSize);
                    if (start < end)
                    {
                        old.Position = start;
                        old.ReadExactly(oldBuffer, (int)(start - from), (int)(end - start));
                    }

                    for (int i = 0; i < n; i++)
                    {
                        buffer[i] += oldBuffer[i];
                    }

                    made.Write(buffer, 0, n);
                    sha.AppendData(buffer, 0, n);
                    done += n;
                }

                newPos += add;
                oldPos += add;

                for (long done = 0; done < copy;)
                {
                    int n = (int)Math.Min(buffer.Length, copy - done);
                    extraZ.ReadExactly(buffer, 0, n);
                    made.Write(buffer, 0, n);
                    sha.AppendData(buffer, 0, n);
                    done += n;
                }

                newPos += copy;
                oldPos += seek;
            }

            madeHash = Convert.ToHexStringLower(sha.GetHashAndReset());
        }

        if (!string.Equals(madeHash, h.NewSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("the patched file does not have the expected SHA-256");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind in the staging folder, which is discarded anyway.
        }
    }

    private static Stream Slice(string path, long offset, long length)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        stream.Position = offset;
        return new BoundedStream(stream, length);
    }

    /// <summary>Reads at most <c>length</c> bytes of the inner stream, then reports its end.</summary>
    private sealed class BoundedStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            int n = inner.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
            _remaining -= n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
