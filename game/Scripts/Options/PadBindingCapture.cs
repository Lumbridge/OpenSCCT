using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Options;

/// <summary>
/// Captures one of the existing controller gestures. The opener must be released before capture starts;
/// committing on release leaves time to recognize a combination or B's existing long press.
/// </summary>
public sealed class PadBindingCapture
{
    private readonly HashSet<string> _previous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _seconds = new(StringComparer.Ordinal);
    private string? _candidate;
    private bool _complete;

    public PadBindingCapture(int device) => Device = device;

    public int Device { get; }
    public bool Armed { get; private set; }
    public string? Error { get; private set; }
    public string? Preview => _candidate;

    /// <summary>
    /// Returns the captured gesture once, when one of its controls is released. Only physical button and trigger
    /// names are considered; stick motion cannot become a binding. Unsupported combinations wait for release.
    /// </summary>
    public string? Update(IReadOnlySet<string> down, double delta)
    {
        ArgumentNullException.ThrowIfNull(down);
        if (_complete) return null;
        var physical = new HashSet<string>(down.Where(control => PadSettings.PhysicalControls.Contains(control)), StringComparer.Ordinal);
        if (!Armed)
        {
            if (physical.Count == 0)
            {
                Armed = true;
                _previous.Clear();
                _seconds.Clear();
                _candidate = null;
            }
            return null;
        }

        double elapsed = double.IsFinite(delta) && delta > 0 ? delta : 0;
        foreach (string control in _previous)
            _seconds[control] = _seconds.GetValueOrDefault(control) + elapsed;

        // A chord is already identified before either constituent releases. Do not turn a released chord into
        // the remaining modifier's standalone assignment, or a long B press into its tap assignment.
        if (_candidate is { } candidate && GestureControls(candidate).Any(control => !physical.Contains(control)))
        {
            if (candidate == "B" && _seconds.GetValueOrDefault("B") >= PadBindingState.HoldSeconds)
                candidate = "B:Hold";
            _candidate = candidate;
            _complete = true;
            Armed = false;
            return candidate;
        }

        foreach (string control in physical)
            if (!_previous.Contains(control)) _seconds[control] = 0;

        if (physical.Count == 1)
        {
            string single = physical.Single();
            _candidate = single == "B" && _seconds.GetValueOrDefault("B") >= PadBindingState.HoldSeconds ? "B:Hold" : single;
            Error = null;
        }
        else if (physical.Count > 1)
        {
            string? chord = physical.Count == 2
                ? PadSettings.Controls.FirstOrDefault(control => control.Contains('+') && physical.SetEquals(GestureControls(control)))
                : null;
            if (chord is null)
            {
                Error = "Unsupported combination. Release the controls, then use a single button, Hold B, or a supported LS/LB combination.";
                _candidate = null;
                Armed = false;
            }
            else
            {
                _candidate = chord;
                Error = null;
            }
        }

        _previous.Clear();
        _previous.UnionWith(physical);
        return null;
    }

    private static IEnumerable<string> GestureControls(string gesture) => gesture.EndsWith(":Hold", StringComparison.Ordinal)
        ? new[] { gesture[..^5] } : gesture.Split('+');
}
