using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Update;

namespace OpenScct.Launcher.App;

internal static class Program
{
    public const string AfterUpdateArg = "--after-update";

    /// <summary>The launcher's exe now: Environment.ProcessPath keeps the start name after a rename on update.</summary>
    public static string? ExePath { get; private set; } = Environment.ProcessPath;

    [STAThread]
    private static int Main(string[] args)
    {
        string? joinServerId = null;
        string? mapCode = null;
        bool afterUpdate = false;
        bool openUnreal = false;
        foreach (string arg in args)
        {
            bool hasTarget = openUnreal || joinServerId is not null || mapCode is not null;
            if (arg == AfterUpdateArg && !afterUpdate) afterUpdate = true;
            else if (!hasTarget && string.Equals(arg, "openscct://edition/unreal", StringComparison.OrdinalIgnoreCase)) openUnreal = true;
            else if (!hasTarget && WebJoinRequest.TryParse(arg, out string? id)) joinServerId = id;
            else if (!hasTarget && WebMapRequest.TryParse(arg, out string? code)) mapCode = code;
            else
            {
                MessageBox.Show("This OpenSCCT link is invalid. Open the project website and try the link again.", "OpenSCCT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }
        }
        string? exe = Environment.ProcessPath;
        if (exe is not null)
        {
            // After a self update the old process may still be closing: give it a few seconds.
            var swap = new LauncherSwap(exe);
            swap.CleanupAfterStart(afterUpdate ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);

            // An older launcher kept its versioned name when it updated to this one: take the plain name.
            if (afterUpdate)
            {
                ExePath = swap.RenameToPlainName();
            }
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\OpenSCCT.Launcher", out bool first);
        if (!first && !afterUpdate)
        {
            if (openUnreal)
                return WebJoinPipe.SendAsync(WebJoinPipe.EditionName, "Unreal").GetAwaiter().GetResult() ? 0 : 1;
            if (mapCode is not null && !WebJoinPipe.SendAsync(WebJoinPipe.MapsName, mapCode).GetAwaiter().GetResult())
            {
                MessageBox.Show("The running launcher could not open this map. Close it, open the latest launcher, then try the map link again.", "OpenSCCT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            if (joinServerId is not null && !WebJoinPipe.SendAsync(WebJoinPipe.LauncherName, joinServerId).GetAwaiter().GetResult())
            {
                MessageBox.Show("The running launcher could not receive this Join request. Close it, open the latest launcher, then choose Join again.", "OpenSCCT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            return 0;
        }

        if (!first)
        {
            // The previous launcher is exiting after a self update; wait for it to let go.
            try
            {
                if (!single.WaitOne(TimeSpan.FromSeconds(10)))
                {
                    MessageBox.Show("The previous launcher is still closing. Open the launcher again in a moment.", "OpenSCCT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }
            }
            catch (AbandonedMutexException)
            {
                // It exited without releasing: ours now.
            }
        }

        ApplicationConfiguration.Initialize();
        using var form = new MainForm(joinServerId, WebJoinRegistration.Register(ExePath));
        _ = form.Handle;
        if (openUnreal) form.ShowEdition("Unreal");
        if (mapCode is not null) form.Shown += (_, _) => form.BeginInvoke(() => form.OpenMapRequest(mapCode));
        using var lifetime = new CancellationTokenSource();
        Task listener = WebJoinPipe.ListenAsync(form.QueueWebJoin, lifetime.Token);
        Task editions = WebJoinPipe.ListenAsync(form.ShowEdition, lifetime.Token, WebJoinPipe.EditionName);
        Task maps = WebJoinPipe.ListenAsync(form.OpenMapRequest, lifetime.Token, WebJoinPipe.MapsName);
        try { Application.Run(form); }
        finally
        {
            lifetime.Cancel();
            Task.WhenAll(listener, editions, maps).GetAwaiter().GetResult();
            single.ReleaseMutex();
        }
        return 0;
    }
}
