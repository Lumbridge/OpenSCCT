using System;
using System.Globalization;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// Reloaded's frame-rate readout (rl:gfx.fpscounter): the frame times of the last 5 s (at most 2048) give the
/// current rate and the slowest and fastest 1 %, drawn in the top right corner.
/// </summary>
public sealed class HudFps
{
    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 19: 5 s, at most 2048 frames")]
    public const int MaxFrames = 2048;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 19")]
    public const float Window = 5f;

    private readonly float[] _times = new float[MaxFrames];
    private readonly float[] _sorted = new float[MaxFrames];
    private int _start;
    private int _count;
    private float _sum;

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The last readout: frames a second now, of the slowest 1 % and of the fastest 1 %.</summary>
    public (float Now, float Low, float High) Rates { get; private set; }

    public void Add(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        if (_count == MaxFrames)
        {
            _sum -= _times[_start];
            _start = (_start + 1) % MaxFrames;
            _count--;
        }

        _times[(_start + _count) % MaxFrames] = dt;
        _count++;
        _sum += dt;
        while (_count > 1 && _sum - _times[_start] >= Window)
        {
            _sum -= _times[_start];
            _start = (_start + 1) % MaxFrames;
            _count--;
        }

        _last = dt;
    }

    private float _last;

    /// <summary>Works out <see cref="Rates"/> from the frames kept.</summary>
    public void Measure()
    {
        if (_count == 0)
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            _sorted[i] = _times[(_start + i) % MaxFrames];
        }

        Array.Sort(_sorted, 0, _count);
        int onePercent = Math.Max(1, _count / 100);
        float slow = 0f, fast = 0f;
        for (int i = 0; i < onePercent; i++)
        {
            fast += _sorted[i];
            slow += _sorted[_count - 1 - i];
        }

        Rates = (1f / _last, onePercent / slow, onePercent / fast);
    }

    public void Draw(Vector2 screen, bool shown)
    {
        DrawList.Clear();
        if (!shown || _count == 0)
        {
            return;
        }

        Measure();
        float h = Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight);
        (float now, float low, float high) = Rates;
        string text = string.Create(CultureInfo.InvariantCulture, $"{now:0} fps  1% low {low:0}  1% high {high:0}");
        DrawList.Items.Add(HudText.Item("Fps", text, new Rect2(0f, 6f, screen.X - 8f, h), h, MagmaAlign.Right, new Color(1f, 1f, 1f, 0.9f)));
    }
}
