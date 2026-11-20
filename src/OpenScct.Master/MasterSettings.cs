using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenScct.Master;

/// <summary>
/// The settings the admin portal can change while the master runs (message of the day, latest and minimum
/// version): stored in the account database, they override the config file's values. <see cref="Apply"/> writes the
/// stored values (or the config's, for settings not stored) into the live options.
/// </summary>
internal static class MasterSettings
{
    private sealed record ConfigValues(string? Motd, string? LatestVersion, string? MinVersion);

    private static readonly ConditionalWeakTable<MasterOptions, ConfigValues> s_config = new();

    internal static bool ValidVersion(string value)
    {
        string[] parts = value.Split('.');
        return parts.Length is >= 1 and <= 3 && parts.All(part =>
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    public static void Apply(AccountStore store, MasterOptions options)
    {
        ConfigValues config = s_config.GetValue(options, o => new ConfigValues(o.Motd, o.LatestVersion, o.MinVersion));
        Dictionary<string, string> stored = store.Settings();
        options.Motd = stored.TryGetValue("motd", out string? motd) ? motd : config.Motd;
        options.LatestVersion = stored.TryGetValue("latestVersion", out string? latest) ? latest : config.LatestVersion;
        options.MinVersion = stored.TryGetValue("minVersion", out string? min) ? min : config.MinVersion;
    }
}
