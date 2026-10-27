using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// The master's own build, apart from the game's version: MasterVersion in OpenScct.Master.csproj, the build number
/// (the commit's UTC timestamp, Directory.Build.targets) and the short commit, all stamped into svm-master.dll. The
/// status answer carries them (masterVersion, masterBuild, masterCommit) and the game shows them under its version.
/// </summary>
public static class MasterBuildInfo
{
    /// <summary>The master's version ("1.0.0").</summary>
    public static string Version { get; }

    /// <summary>The build number, yyyyMMdd.HHmm; "0" without git.</summary>
    public static string BuildNumber { get; }

    /// <summary>The short commit hash, or empty.</summary>
    public static string Commit { get; }

    /// <summary>"1.0.0 build 20261027.1200 (abc1234)", for the log.</summary>
    public static string Text => Commit.Length > 0 ? $"{Version} build {BuildNumber} ({Commit})" : $"{Version} build {BuildNumber}";

    static MasterBuildInfo()
    {
        System.Reflection.Assembly assembly = typeof(MasterBuildInfo).Assembly;
        (_, BuildNumber, Commit) = BuildInfo.Stamp(assembly);
        Version = BuildInfo.Metadata(assembly, "MasterVersion") ?? "0.0.0";
    }
}
