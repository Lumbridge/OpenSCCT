using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace OpenScct.Launcher.App;

internal static class WebJoinRegistration
{
    public static string? Register(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !Path.IsPathFullyQualified(exePath)) return "Open the installed launcher to enable website Join links.";
        try
        {
            using RegistryKey protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\openscct", writable: true);
            protocol.SetValue(string.Empty, "URL:OpenSCCT", RegistryValueKind.String);
            protocol.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
            using RegistryKey command = protocol.CreateSubKey(@"shell\open\command", writable: true);
            // Windows expands the one URI placeholder. Both executable and placeholder are
            // quoted; Program accepts only the strict join URI, never arbitrary game arguments.
            command.SetValue(string.Empty, "\"" + exePath + "\" \"%1\"", RegistryValueKind.String);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return "Website Join links could not be enabled for this Windows account. You can still join from Find a Session in the game.";
        }
    }
}
