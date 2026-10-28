using System;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame;

/// <summary>
/// Reloaded's process switches (rl:gfx.system): <c>--priority=</c> and <c>--affinity=</c> on the command line (read by
/// OpenScct.Server.ProcessOptions, which the dedicated server shares), and the Windows power throttling opt-out
/// (<c>throttle_stop</c>), which keeps Windows from running the game on efficiency cores or at low clock speed when its
/// window is not in front.
/// </summary>
public static class ProcessTuning
{
    /// <summary>Applies the priority and affinity asked for; a value that cannot be read is reported and ignored.</summary>
    public static void Apply(string? priority, string? affinity) =>
        OpenScct.Server.ProcessOptions.Apply(priority, affinity, done => GD.Print("SVM_SYSTEM " + done), problem => GD.PushWarning(problem));


    /// <summary>
    /// Windows 10 and later: SetProcessInformation(ProcessPowerThrottling) with execution speed throttling controlled
    /// and off. Nothing elsewhere.
    /// </summary>
    public static void StopPowerThrottling()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        var state = new PowerThrottlingState { Version = PowerThrottlingCurrentVersion, ControlMask = PowerThrottlingExecutionSpeed, StateMask = 0 };
        if (!SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>()))
        {
            GD.PushWarning($"Could not turn power throttling off (error {Marshal.GetLastPInvokeError()}).");
        }
        else
        {
            GD.Print("SVM_SYSTEM power throttling off");
        }
    }

    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
