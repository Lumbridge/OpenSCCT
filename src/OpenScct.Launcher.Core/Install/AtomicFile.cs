using System;
using System.IO;
using System.Text;

namespace OpenScct.Launcher.Install;

/// <summary>Writes a small file so readers see either the old or the new content, never half of it.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content) => WriteAllBytes(path, Encoding.UTF8.GetBytes(content));

    public static void WriteAllBytes(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            // Same-volume rename: NTFS replaces the name in one step.
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
