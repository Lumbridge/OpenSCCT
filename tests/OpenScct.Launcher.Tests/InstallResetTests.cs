using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.Install;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class InstallResetTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "Uses Windows executable names and the editor swap lock.", SkipUnless = nameof(OnWindows))]
    public async Task ForceCloseStopsGamesAndManagedEditorToolsButLeavesOtherProcessesAndActiveUpdates()
    {
        using var temp = new TempDir();
        var layout = new LauncherLayout(temp.Path);
        var processes = new List<Process>();
        try
        {
            Process game = Start(temp.Combine("versions", "1.0.0", "OpenSCCT.exe"));
            Process console = Start(temp.Combine("versions", "1.0.0", "OpenSCCT.console.exe"));
            Process editor = Start(Path.Combine(layout.Editor, EditorService.ExeName));
            string cliPath = Path.Combine(layout.Editor, "ose", "ose.exe");
            Process cli = Start(cliPath);
            Process anotherCli = Start(cliPath);
            Process otherCli = Start(temp.Combine("other-install", "ose.exe"));
            Process updateHelper = Start(Path.Combine(layout.Editor, ".update", "helper", "ose.exe"));

            using (FileStream updateLock = File.Open(Path.Combine(layout.Editor, ".update", "swap.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                await InstallReset.ForceCloseProcessesAsync(layout, Find);
                Assert.All(processes, p => Assert.False(p.HasExited));
            }

            await InstallReset.ForceCloseProcessesAsync(layout, Find);
            Assert.All(new[] { game, console, editor, cli, anotherCli }, p => Assert.True(p.HasExited));
            Assert.False(otherCli.HasExited);
            Assert.False(updateHelper.HasExited);
        }
        finally
        {
            foreach (Process process in processes)
            {
                using (process)
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(5));
                }
            }
        }

        Process Start(string exe)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            if (!File.Exists(exe)) File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
            var info = new ProcessStartInfo(exe, "/d /q")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            };
            Process process = Process.Start(info)!;
            processes.Add(process);
            return process;
        }

        Process[] Find(string name) => processes.Where(p => !p.HasExited && p.ProcessName == name)
            .Select(p => Process.GetProcessById(p.Id)).ToArray();
    }
}
