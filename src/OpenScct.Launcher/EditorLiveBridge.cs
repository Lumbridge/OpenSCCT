using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenScct.Launcher.App;

internal static partial class EditorLiveBridge
{
    // The live MCP bridge forwards requests to the editor process and never opens the content packs itself.
    internal static bool IsLiveMcpProcess(int pid)
    {
        try
        {
            using var process = new ManagementObject($"Win32_Process.Handle='{pid}'");
            return process["CommandLine"] is string commandLine && LiveMcpCommand().IsMatch(commandLine);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            // If the command line is unavailable, continue to treat the CLI as using shared content.
            return false;
        }
    }

    [GeneratedRegex("""^(?:"[^"]+"|[^ \t]+)[ \t]+mcp[ \t]+--live(?:[ \t]+--pid[ \t]+[0-9]+)?[ \t]*$""")]
    private static partial Regex LiveMcpCommand();
}
