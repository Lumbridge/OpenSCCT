namespace OpenScct.Net.Discovery;

/// <summary>
/// The build a server or client runs, as the browser and the master show it. Joining is decided by the protocol and
/// content hashes, not by this text; the master compares it with its latest and minimum versions for update notices.
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// The release version ("0.5.0", "0.6.0-beta.1"): VersionPrefix in Directory.Build.props, or the release's
    /// -p:Version, as the build stamped it into this assembly.
    /// </summary>
    public static string Version { get; }

    /// <summary>The build number: the commit's UTC timestamp, yyyyMMdd.HHmm (Directory.Build.targets); "0" without git.</summary>
    public static string BuildNumber { get; }

    /// <summary>The short commit hash the build was made from, or empty.</summary>
    public static string Commit { get; }

    /// <summary>"OpenSCCT 0.5.0  build 20261026.1412 (d3cd88b)", as the menus show it.</summary>
    public static string Text =>
        Commit.Length > 0 ? $"OpenSCCT {Version}  build {BuildNumber} ({Commit})" : $"OpenSCCT {Version}  build {BuildNumber}";

    static BuildInfo()
    {
        (Version, BuildNumber, Commit) = Stamp(typeof(BuildInfo).Assembly);
    }

    /// <summary>
    /// What a build stamped into <paramref name="assembly"/>: the informational version split into the version and the
    /// short commit hash, and the AssemblyMetadata("BuildNumber") of Directory.Build.targets ("0" when missing).
    /// </summary>
    public static (string Version, string BuildNumber, string Commit) Stamp(System.Reflection.Assembly assembly)
    {
        System.ArgumentNullException.ThrowIfNull(assembly);
        string informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion ?? "0.0.0";
        int plus = informational.IndexOf('+', System.StringComparison.Ordinal);
        string version = plus >= 0 ? informational[..plus] : informational;
        string commit = plus >= 0 ? informational[(plus + 1)..] : string.Empty;
        string build = Metadata(assembly, "BuildNumber") ?? "0";
        return (version, build, commit.Length > 7 ? commit[..7] : commit);
    }

    /// <summary>The value of the assembly's AssemblyMetadata(<paramref name="key"/>), or null when it has none.</summary>
    public static string? Metadata(System.Reflection.Assembly assembly, string key)
    {
        System.ArgumentNullException.ThrowIfNull(assembly);
        foreach (System.Reflection.AssemblyMetadataAttribute meta in
                 System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(assembly))
        {
            if (meta.Key == key && !string.IsNullOrEmpty(meta.Value))
            {
                return meta.Value;
            }
        }

        return null;
    }

    /// <summary>Default UDP game port (no clash with the original game's 8888 or the Reloaded Editor test slots).</summary>
    public const int DefaultGamePort = 28888;

    /// <summary>Fixed LAN discovery port, so a host on another game port still answers LAN searches.</summary>
    public const int DefaultLanPort = 28889;

    /// <summary>Default port of the master's NAT punch introducer.</summary>
    public const int DefaultIntroducerPort = 28880;

    /// <summary>Default port of the master's relay.</summary>
    public const int DefaultRelayPort = 28881;

    /// <summary>Default port of the master's HTTP list API (behind a TLS proxy on a public host).</summary>
    public const int DefaultMasterHttpPort = 28800;

    /// <summary>
    /// Compares two versions "a.b.c" numerically (missing parts are 0, text after a dash is ignored); negative when
    /// <paramref name="a"/> is older.
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        int[] x = Parts(a);
        int[] y = Parts(b);
        for (int i = 0; i < 3; i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }

        return 0;
    }

    private static int[] Parts(string? version)
    {
        var parts = new int[3];
        if (string.IsNullOrEmpty(version))
        {
            return parts;
        }

        string core = version.Split('-', '+')[0];
        string[] items = core.Split('.');
        for (int i = 0; i < 3 && i < items.Length; i++)
        {
            _ = int.TryParse(items[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parts[i]);
        }

        return parts;
    }
}
