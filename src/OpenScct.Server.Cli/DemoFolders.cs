using System;
using System.IO;
using OpenScct.Net;

namespace OpenScct.Server.Cli;

/// <summary>Housekeeping of a server's demo folder.</summary>
internal static class DemoFolders
{
    /// <summary>Deletes demo files older than <paramref name="keep"/> (by last write time).</summary>
    public static void Prune(string folder, TimeSpan keep, Action<string> log)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - keep;
        int removed = 0;
        foreach (string file in Directory.EnumerateFiles(folder, "*" + DemoFile.Extension))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (IOException e)
            {
                log($"demo {file} not removed: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                log($"demo {file} not removed: {e.Message}");
            }
        }

        if (removed > 0)
        {
            log($"removed {removed} demo(s) older than {keep.TotalDays:0} days from {folder}");
        }
    }
}
