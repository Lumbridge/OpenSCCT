using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Godot;

namespace OpenScctGame.Web;

internal static partial class BrowserStorage
{
    // Browser account.cfg contains identity and appearance revision bookkeeping, never a bearer token.
    private static readonly string[] Files = ["settings.json", "settings.cfg", "ui.cfg", "account.cfg", "tutorials.json", "svm_keys_spy.ini", "svm_keys_merc.ini"];
    private static string? s_last;
    internal static string Scope { get; set; } = "guest";

    [JSImport("globalThis.openscctLoadSettings")]
    private static partial string Load(string scope);

    [JSImport("globalThis.openscctSaveSettings")]
    private static partial void Save(string scope, string data);

    internal static void Restore()
    {
        try
        {
            var files = JsonSerializer.Deserialize<Dictionary<string, string>>(Load(Scope));
            foreach (string name in Files)
            {
                string path = ProjectSettings.GlobalizePath("user://" + name);
                if (files is null || !files.TryGetValue(name, out string? data) || data is null || data.Length > 512 * 1024)
                {
                    // A browser can restore its virtual filesystem before us. Do not inherit another account's layout.
                    if (File.Exists(path)) File.Delete(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, data);
            }
        }
        catch (Exception error) when (error is JsonException or JSException or IOException)
        {
            Console.Error.WriteLine("Browser settings could not be restored: " + error.Message);
        }
    }

    internal static void Flush()
    {
        var files = new Dictionary<string, string>();
        foreach (string name in Files)
        {
            string path = ProjectSettings.GlobalizePath("user://" + name);
            if (File.Exists(path)) files[name] = File.ReadAllText(path);
        }
        string data = JsonSerializer.Serialize(files);
        if (data == s_last) return;
        try { Save(Scope, data); s_last = data; }
        catch (JSException error) { Console.Error.WriteLine("Browser settings could not be saved: " + error.Message); }
    }

    internal static void DiscardBindings()
    {
        foreach (string name in new[] { "svm_keys_spy.ini", "svm_keys_merc.ini" })
        {
            string path = ProjectSettings.GlobalizePath("user://" + name);
            if (File.Exists(path)) File.Delete(path);
        }
        Flush();
    }
}
