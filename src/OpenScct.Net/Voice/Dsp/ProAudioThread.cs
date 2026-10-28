using System;
using System.Runtime.InteropServices;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// Registers the calling thread with the Windows multimedia scheduler (MMCSS) as a "Pro Audio" task
/// (FEATURES rl:audio.thread), so the voice thread keeps its timing while the game loads the machine. Nothing happens
/// on other systems or when avrt.dll is missing; disposing the result ends the registration.
/// </summary>
[PortOf("rl:audio.thread", Spec = "spec/reloaded/FEATURES.md#rlaudiothread---audio-thread-priority")]
[Provenance(ProvenanceKind.Decoded, "Reloaded.Core string \"Pro Audio\" next to its imports (FEATURES rl:audio.thread evidence)")]
public static class ProAudioThread
{
    public const string TaskName = "Pro Audio";

    /// <summary>Registers this thread; the result undoes it. <see cref="Registered"/> says whether it took.</summary>
    public static Registration Enter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Registration(IntPtr.Zero);
        }

        try
        {
            uint index = 0;
            return new Registration(AvSetMmThreadCharacteristicsW(TaskName, ref index));
        }
        catch (DllNotFoundException)
        {
            return new Registration(IntPtr.Zero);
        }
        catch (EntryPointNotFoundException)
        {
            return new Registration(IntPtr.Zero);
        }
    }

    /// <summary>An MMCSS registration (or none).</summary>
    public sealed class Registration : IDisposable
    {
        private IntPtr _handle;

        internal Registration(IntPtr handle) => _handle = handle;

        public bool Registered => _handle != IntPtr.Zero;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                _ = AvRevertMmThreadCharacteristics(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
