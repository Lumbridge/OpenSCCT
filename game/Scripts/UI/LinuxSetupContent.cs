using System.IO;

namespace OpenScctGame.UI;

/// <summary>Prerequisites for the desktop setup screen, independent of the Godot UI.</summary>
public static class LinuxSetupContent
{
    public static bool SharedReady(string? shared) => shared is not null &&
        File.Exists(Path.Combine(shared, "classes.json")) &&
        Directory.Exists(Path.Combine(shared, "ui")) &&
        Directory.Exists(Path.Combine(shared, "chars")) &&
        Directory.Exists(Path.Combine(shared, "audio"));

    public static string? OverrideProblem(string? root) =>
        root is not null && !SharedReady(Path.Combine(root, "shared"))
            ? "The selected game content folder is incomplete: " + root + "\n" +
              "Rebuild it in the OpenSCCT launcher. For a custom build, set --svm-content or SVM_CONTENT to the completed conversion."
            : null;
}
