using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Unreal;

/// <summary>
/// The current OpenSCCT Unreal version: its folder, and the OpenSCCT version and commit it was built against (from its
/// version.json; null when the file does not say).
/// </summary>
public sealed record UnrealInstallation(SemVersion Version, string Folder, string? OpenscctVersion, string? OpenscctCommit);

/// <summary>
/// The answer of an update check. <see cref="Latest"/> is null when no compatible release is published (the repository
/// may not exist yet); <see cref="ReleaseFeed.Problem"/> is then null too, unless GitHub could not be asked.
/// </summary>
public sealed record UnrealCheck(ReleaseFeed Feed, VersionedRelease? Latest, UnrealInstallation? Installed, bool UpdateAvailable);

/// <summary>An install step for the player, with byte counts while downloading.</summary>
public sealed record UnrealProgress(string Phase, long Received = 0, long Total = 0, double BytesPerSecond = 0, bool CanCancel = true);

public enum UnrealInstallOutcome
{
    /// <summary>Installed and made current.</summary>
    Installed,

    /// <summary>That version was current already; nothing was downloaded.</summary>
    AlreadyCurrent,

    /// <summary>Installed; it becomes current when the running game closes (<see cref="UnrealService.FinishPendingSwitch"/>).</summary>
    WaitingForGameToClose,
}

/// <summary>
/// What an install did. <see cref="DownloadedBytes"/> is null when nothing was downloaded; <see cref="BuildBytes"/> is the
/// build's full size when it was installed from its file list, and null after a whole-ZIP install.
/// </summary>
public sealed record UnrealInstallResult(UnrealInstallOutcome Outcome, SemVersion Version, long? DownloadedBytes, long? BuildBytes);

/// <summary>The folders an uninstall could not delete completely (files in use or denied); empty when it removed everything.</summary>
public sealed record UnrealUninstallResult(IReadOnlyList<string> Failures);

/// <summary>A running Unreal game process: its exe when readable, and how to end it (a test seam).</summary>
internal sealed record UnrealProcess(int Id, string? ExePath, Func<CancellationToken, Task> KillAsync);
