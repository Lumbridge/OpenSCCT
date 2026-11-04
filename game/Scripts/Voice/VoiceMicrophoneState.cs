using System;

namespace OpenScctGame.Voice;

/// <summary>Tracks capture preference changes without reopening a failed device on every frame.</summary>
public sealed class VoiceMicrophoneState
{
    public const double RetrySeconds = 5;
    private string? _device;
    private bool _enabled;
    private bool _ready;
    private double _retry;

    public void Update(bool enabled, string? device, double delta, Func<string, bool> start, Action stop)
    {
        device ??= string.Empty;
        if (!enabled)
        {
            if (_enabled) stop();
            Reset();
            return;
        }
        bool changed = !_enabled || !string.Equals(device, _device, StringComparison.Ordinal);
        _enabled = true;
        _device = device;
        _retry -= Math.Max(0, delta);
        if (changed || (!_ready && _retry <= 0))
        {
            _ready = start(device);
            _retry = RetrySeconds;
        }
    }

    public void Reset()
    {
        _enabled = false;
        _device = null;
        _ready = false;
        _retry = 0;
    }
}
