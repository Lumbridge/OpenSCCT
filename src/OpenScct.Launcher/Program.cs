using System;
using System.Threading;
using System.Windows.Forms;
using OpenScct.Launcher.Update;

namespace OpenScct.Launcher.App;

internal static class Program
{
    public const string AfterUpdateArg = "--after-update";

    [STAThread]
    private static int Main(string[] args)
    {
        string? exe = Environment.ProcessPath;
        bool afterUpdate = Array.IndexOf(args, AfterUpdateArg) >= 0;
        if (exe is not null)
        {
            // After a self update the old process may still be closing: give it a few seconds.
            new LauncherSwap(exe).CleanupAfterStart(afterUpdate ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\OpenSCCT.Launcher", out bool first);
        if (!first && !afterUpdate)
        {
            return 0;
        }

        if (!first)
        {
            // The previous launcher is exiting after a self update; wait for it to let go.
            try
            {
                single.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // It exited without releasing: ours now.
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
