using System;
using System.Diagnostics;
using System.Globalization;

namespace OpenScct.Server;

/// <summary>
/// Reloaded's process switches (rl:gfx.system): <c>--priority=</c> and <c>--affinity=</c>, shared by the game
/// (OpenScctGame.ProcessTuning) and the dedicated server (svm-server).
/// </summary>
public static class ProcessOptions
{
    /// <summary>The names <see cref="PriorityOf"/> reads, for help texts.</summary>
    public const string PriorityNames = "idle, low, below, normal, medium, above, high";

    /// <summary>
    /// Applies the priority and affinity asked for (null: leave alone). Each outcome is reported: <paramref name="done"/>
    /// for a value set, <paramref name="problem"/> for one that cannot be read or set (it is then ignored).
    /// </summary>
    public static void Apply(string? priority, string? affinity, Action<string> done, Action<string> problem)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(problem);
        using Process self = Process.GetCurrentProcess();
        if (priority is not null)
        {
            if (PriorityOf(priority) is { } p)
            {
                try
                {
                    self.PriorityClass = p;
                    done("priority " + priority);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or ArgumentOutOfRangeException)
                {
                    problem($"Could not set priority {priority}: {e.Message}");
                }
            }
            else
            {
                problem($"Unknown --priority '{priority}' ({PriorityNames}).");
            }
        }

        if (affinity is not null && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            if (AffinityOf(affinity, Environment.ProcessorCount) is { } mask)
            {
                try
                {
                    self.ProcessorAffinity = (IntPtr)mask;
                    done("affinity " + affinity);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentOutOfRangeException)
                {
                    problem($"Could not set affinity {affinity}: {e.Message}");
                }
            }
            else
            {
                problem($"Unreadable --affinity '{affinity}' (a mask such as 0xF, or CPUs such as 0,2-3).");
            }
        }
    }

    /// <summary>The priority class of a name (Reloaded's example is "medium", taken as normal).</summary>
    public static ProcessPriorityClass? PriorityOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToLowerInvariant() switch
        {
            "idle" or "low" => ProcessPriorityClass.Idle,
            "below" or "belownormal" or "below_normal" => ProcessPriorityClass.BelowNormal,
            "normal" or "medium" => ProcessPriorityClass.Normal,
            "above" or "abovenormal" or "above_normal" => ProcessPriorityClass.AboveNormal,
            "high" => ProcessPriorityClass.High,
            _ => null,
        };
    }

    /// <summary>
    /// A CPU mask from "0xF" (hexadecimal), "15" (decimal) or a list of CPUs and ranges ("0,2-3"); null when it cannot
    /// be read or names no CPU this machine has.
    /// </summary>
    public static long? AffinityOf(string text, int cpus)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim();
        long all = cpus >= 63 ? long.MaxValue : (1L << Math.Max(1, cpus)) - 1;
        long mask = 0;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
            {
                return null;
            }
        }
        else if (t.Contains(',', StringComparison.Ordinal) || t.Contains('-', StringComparison.Ordinal))
        {
            foreach (string part in t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ends = part.Split('-', StringSplitOptions.TrimEntries);
                if (ends.Length > 2 || !int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out int from))
                {
                    return null;
                }

                int to = from;
                if (ends.Length == 2 && !int.TryParse(ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out to))
                {
                    return null;
                }

                for (int c = from; c <= to && c < 63; c++)
                {
                    mask |= 1L << c;
                }
            }
        }
        else if (!long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out mask))
        {
            return null;
        }

        mask &= all;
        return mask == 0 ? null : mask;
    }
}
