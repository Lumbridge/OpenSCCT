using System;
using System.IO;

namespace OpenScct.Launcher.Install;

/// <summary>
/// Deletes a folder the launcher owns. Refuses anything outside the given root, and never walks into a junction or
/// symbolic link: the link itself is removed, its target is left alone. User data (Godot's user:// folder under
/// %APPDATA%) is never under the root, and a link to it inside a version folder would not be followed.
/// </summary>
public static class SafeDelete
{
    public static void Tree(string path, string mustBeUnder)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mustBeUnder);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mustBeUnder)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Length <= root.Length)
        {
            throw new InvalidOperationException($"Refusing to delete '{full}': it is not inside '{root}'.");
        }

        var info = new DirectoryInfo(full);
        if (!info.Exists)
        {
            return;
        }

        DeleteTree(info);
    }

    private static void DeleteTree(DirectoryInfo dir)
    {
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // Removes the link only.
            dir.Delete(recursive: false);
            return;
        }

        foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child)
            {
                DeleteTree(child);
            }
            else
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }

                entry.Delete();
            }
        }

        dir.Delete(recursive: false);
    }
}
