using System;
using System.IO;

namespace OpenScct.Launcher.Update;

/// <summary>The free disk space check every install makes before it writes a build.</summary>
internal static class DiskSpace
{
    /// <summary>
    /// Throws <see cref="UpdateRejectedException"/> when the drive of <paramref name="path"/> has less than
    /// <paramref name="needed"/> bytes free. Skipped when the free space cannot be read.
    /// </summary>
    /// <param name="probe">Test hook: the free bytes of a path's drive, or null when unknown; null uses the drive's.</param>
    public static void Ensure(string path, long needed, Func<string, long?>? probe = null)
    {
        long? free = (probe ?? Free)(path);
        if (free is long bytes && bytes < needed)
        {
            throw new UpdateRejectedException(
                $"Not enough free disk space for this update: it needs {needed / (1024 * 1024)} MB and {bytes / (1024 * 1024)} MB is free.");
        }
    }

    private static long? Free(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
