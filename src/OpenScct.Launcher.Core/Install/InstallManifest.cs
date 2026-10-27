using System;
using System.IO;
using System.Text.Json;

namespace OpenScct.Launcher.Install;

/// <summary>
/// The current pointer: which version folder Play starts and which one is kept for rollback. Switching versions is
/// one atomic write of this file, so a crash leaves either the old or the new version current, never a mix.
/// A manifest is used instead of a junction: it switches in one step and nothing can delete through it.
/// </summary>
public sealed class InstallManifest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string? Current { get; set; }

    public string? Previous { get; set; }

    /// <summary>Installed and verified, waiting to become current once no game is running from the current folder.</summary>
    public string? Pending { get; set; }

    public DateTimeOffset? SwitchedAt { get; set; }

    public static InstallManifest Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path), Json) ?? new InstallManifest();
            }
        }
        catch (JsonException)
        {
            // A damaged manifest is treated as no install; the version folders are still there to pick from.
        }

        return new InstallManifest();
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}
