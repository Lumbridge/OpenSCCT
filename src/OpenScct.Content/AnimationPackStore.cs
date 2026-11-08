using System;
using System.IO;
using System.IO.Compression;

namespace OpenScct.Content;

public static class AnimationPackStore
{
    public static string Install(string archivePath, string libraryRoot)
    {
        string root = Path.GetFullPath(libraryRoot); Directory.CreateDirectory(root);
        string stage = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath); long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                total += entry.Length;
                if (entry.Length > 256_000_000 || total > 2_000_000_000) throw new InvalidDataException("Animation pack is too large.");
                string path = AnimationBundle.AssetPath(stage, entry.FullName);
                if (File.Exists(path)) throw new InvalidDataException("Duplicate animation pack entry.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using Stream input = entry.Open(); using FileStream output = File.Create(path); input.CopyTo(output);
            }
            AnimationBundle bundle = AnimationBundle.Load(stage) ?? throw new InvalidDataException("The archive is not an animation pack.");
            string digest = SvMapHash.Sha256Hex(File.ReadAllBytes(Path.Combine(stage, AnimationBundle.RelativePath)))[..16];
            string destination = AnimationBundle.AssetPath(root, bundle.Project.Id + "-" + digest);
            if (Directory.Exists(destination)) { _ = AnimationBundle.Load(destination); return destination; }
            Directory.Move(stage, destination); return destination;
        }
        finally
        {
            string full = Path.GetFullPath(stage);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
