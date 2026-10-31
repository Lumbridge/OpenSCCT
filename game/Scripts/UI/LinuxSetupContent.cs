using System.IO;

namespace OpenScctGame.UI;

/// <summary>Prerequisites for the Linux install screen, independent of the Godot UI.</summary>
public static class LinuxSetupContent
{
    public static bool SharedReady(string? shared) => shared is not null &&
        File.Exists(Path.Combine(shared, "classes.json")) &&
        Directory.Exists(Path.Combine(shared, "ui")) &&
        Directory.Exists(Path.Combine(shared, "chars")) &&
        Directory.Exists(Path.Combine(shared, "audio"));

    public static string? OverrideProblem(string? root) =>
        root is not null && !SharedReady(Path.Combine(root, "shared"))
            ? "The selected content folder is incomplete: " + root + "\n" +
              "Choose a complete converted content folder, or restart without --svm-content and SVM_CONTENT " +
              "to download the standard packs. Downloads cannot repair the selected folder."
            : null;
}
