using System.IO;

namespace OpenScctGame.Options;

/// <summary>
/// Brings the player's files over from the folder the game used before it was renamed OpenSCCT. Godot keeps user data
/// in a folder named after the project (app_userdata/SvmGame before, app_userdata/OpenSCCT now), so on the first run
/// under the new name the settings, key layouts and online settings are copied once. Nothing is copied when the new
/// folder already has settings, and the old folder is left as it was.
/// </summary>
public static class UserDataMigration
{
    public const string OldFolderName = "SvmGame";

    private static readonly string[] Files =
        ["settings.json", "settings.cfg", "online.cfg", "svm_keys_spy.ini", "svm_keys_merc.ini"];

    /// <summary>Copies the old files when this is the first run under the new name. Returns how many were copied.</summary>
    public static int Run(string current)
    {
        string? parent = Path.GetDirectoryName(current.TrimEnd('/', '\\'));
        if (parent is null)
        {
            return 0;
        }

        return CopyOnce(Path.Combine(parent, OldFolderName), current);
    }

    /// <summary>The copy itself, separate from Godot for tests.</summary>
    public static int CopyOnce(string oldFolder, string newFolder)
    {
        if (!Directory.Exists(oldFolder) || File.Exists(Path.Combine(newFolder, "settings.json")))
        {
            return 0;
        }

        Directory.CreateDirectory(newFolder);
        int copied = 0;
        foreach (string name in Files)
        {
            string from = Path.Combine(oldFolder, name);
            string to = Path.Combine(newFolder, name);
            // Before the player saves settings under the new name, files already here are only the defaults a run
            // wrote (key layouts, online settings), so the old ones replace them.
            if (File.Exists(from))
            {
                File.Copy(from, to, overwrite: true);
                copied++;
            }
        }

        return copied;
    }
}
