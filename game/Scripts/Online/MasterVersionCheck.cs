using System;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;

namespace OpenScctGame.Online;

/// <summary>
/// The master's own version, asked once per run from its status answer (<see cref="MasterStatus.MasterVersion"/>)
/// and shown under the game's version (<see cref="UI.BuildLabel"/>). Asked again when the master address changes, and
/// a minute after a failed answer. Smoke runs never ask. Read on the main thread only.
/// </summary>
public static class MasterVersionCheck
{
    private const ulong RetryMs = 60_000;

    private static Task<MasterStatus>? _status;
    private static string _url = string.Empty;
    private static ulong _failedAt;

    /// <summary>
    /// The line to show: "Master 1.0.0  build 20261027.1200 (abc1234)", or what stands in for it ("Master: checking...",
    /// "Master: not reached", "Master: none set"). Starts the check when needed.
    /// </summary>
    public static string Line()
    {
        if (AccountSettings.Isolated)
        {
            return "Master: not checked";
        }

        string url = OnlineSettings.Current.MasterUrl;
        if (url.Length == 0)
        {
            return "Master: none set";
        }

        ulong now = Time.GetTicksMsec();
        if (_status is null || !string.Equals(url, _url, StringComparison.Ordinal) ||
            (_status.IsFaulted && _failedAt != 0 && now - _failedAt >= RetryMs))
        {
            _url = url;
            _failedAt = 0;
            _status = AccountService.Call(c => c.StatusAsync(), withToken: false);
        }

        if (!_status.IsCompleted)
        {
            return "Master: checking...";
        }

        if (!_status.IsCompletedSuccessfully)
        {
            if (_failedAt == 0)
            {
                _failedAt = now;
            }

            return "Master: not reached";
        }

        return Text(_status.Result);
    }

    /// <summary>"Master 1.0.0  build 20261027.1200 (abc1234)"; a master older than its own version shows the game version it was built with.</summary>
    public static string Text(MasterStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.MasterVersion is not { Length: > 0 } version)
        {
            return $"Master (game {status.Version})";
        }

        string text = "Master " + version;
        if (status.MasterBuild is { Length: > 0 } build && build != "0")
        {
            text += "  build " + build;
        }

        return status.MasterCommit is { Length: > 0 } commit ? $"{text} ({commit})" : text;
    }
}
