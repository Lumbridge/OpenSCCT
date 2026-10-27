using System;
using System.Threading;
using System.Windows.Forms;
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
        string? exe = Environment.ProcessPath;
        bool afterUpdate = Array.IndexOf(args, AfterUpdateArg) >= 0;
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
