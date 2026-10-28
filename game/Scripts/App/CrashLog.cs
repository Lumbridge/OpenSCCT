using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenScct.Net.Discovery;

namespace OpenScctGame;

/// <summary>
/// Crash reports (rl:ac.crash): an exception nothing caught writes <c>crash-&lt;utc&gt;.log</c> next to the settings
/// (the user data folder) with the managed stack, the build, and the map and mode being played. Exceptions inside
/// Godot's callbacks are caught and printed by Godot itself; native crashes keep Godot's own handler.
/// </summary>
public static class CrashLog
{
    private static string? s_folder;

    /// <summary>The map and mode being played ("SteeE Story"), or "" outside a round; written into the report.</summary>
    public static string Context { get; set; } = "";

    /// <summary>Starts listening (once): unhandled exceptions of any thread and faulted tasks nobody awaited.</summary>
    public static void Install(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (s_folder is not null)
        {
            s_folder = folder;
            return;
        }

        s_folder = folder;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, e.IsTerminating ? "unhandled (terminating)" : "unhandled");
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "unobserved task");
    }

    /// <summary>Writes a report for <paramref name="error"/>; returns its path, or null when it could not be written.</summary>
    public static string? Write(Exception? error, string kind)
    {
        if (s_folder is not { } folder)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"OpenSCCT crash report ({kind})");
        text.AppendLine(CultureInfo.InvariantCulture, $"UTC: {now:yyyy-MM-dd HH:mm:ss.fff}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Build: {BuildInfo.Text}");
        text.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "64" : "32")}-bit)");
        text.AppendLine(CultureInfo.InvariantCulture, $"Playing: {(Context.Length == 0 ? "(menus)" : Context)}");
        text.AppendLine();
        text.AppendLine(error?.ToString() ?? "(no exception object)");
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"crash-{now:yyyyMMdd-HHmmss-fff}.log"));
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
