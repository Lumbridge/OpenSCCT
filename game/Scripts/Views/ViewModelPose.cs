using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>
/// The channels of a weapon's view model (<see cref="ViewModelAnim"/>: the merc's first-person arms and rifle, the
/// spy's pistol) as <see cref="ChannelPose"/>s: a one-shot runs from its start to its last frame over the time the
/// simulation gives it (so its play rate is kept), backward plays sample from the end, loops run at the clip's rate times
/// their LoopAnim rate.
/// A one-shot's time left is sampled after every client tick (<see cref="OnTick"/>) and drawn between the last two
/// samples by the frame's interpolation alpha (<see cref="OneShotClock"/>), as the camera is, so the arms move every
/// frame instead of holding each pose for a 60 Hz tick.
/// It reads the simulation and never writes it.
/// </summary>
public sealed class ViewModelPose
{
    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx: the view model clips run at 15 fps (Equipment K clip data)")]
    public const float DefaultFps = 15f;

    private readonly ChannelPose[] _channels = new ChannelPose[ViewModelAnim.ChannelCount];
    private readonly Name[] _sequence = new Name[ViewModelAnim.ChannelCount];
    private readonly float[] _remaining = new float[ViewModelAnim.ChannelCount];
    private readonly float[] _duration = new float[ViewModelAnim.ChannelCount];
    private readonly float[] _loopTime = new float[ViewModelAnim.ChannelCount];
    private readonly Name[] _tickSequence = new Name[ViewModelAnim.ChannelCount];
    private readonly float[] _tickRemaining = new float[ViewModelAnim.ChannelCount];
    private readonly Name[] _prevSequence = new Name[ViewModelAnim.ChannelCount];
    private readonly float[] _prevRemaining = new float[ViewModelAnim.ChannelCount];
    private ViewModelAnim? _tickAnim;
    private int _samples;

    /// <summary>After each client tick: the channels' sequences and time left, for the drawing between ticks.</summary>
    public void OnTick(ViewModelAnim? anim)
    {
        if (!ReferenceEquals(anim, _tickAnim))
        {
            _tickAnim = anim;
            _samples = 0;
        }

        if (anim is null)
        {
            return;
        }

        for (int c = 0; c < ViewModelAnim.ChannelCount; c++)
        {
            _prevSequence[c] = _tickSequence[c];
            _prevRemaining[c] = _tickRemaining[c];
            _tickSequence[c] = anim.Sequence(c);
            _tickRemaining[c] = anim.Remaining(c);
        }

        _samples++;
    }

    /// <summary>The time left of channel <paramref name="c"/> at the drawn moment, <paramref name="alpha"/> of the way into the tick.</summary>
    private float DrawnRemaining(ViewModelAnim anim, int c, float alpha)
    {
        Name sequence = anim.Sequence(c);
        float remaining = anim.Remaining(c);
        if (!ReferenceEquals(anim, _tickAnim) || _samples == 0 || _tickSequence[c] != sequence || _tickRemaining[c] != remaining)
        {
            // No sample of this state yet (the view model was just shown, or the simulation moved on without a sample).
            return remaining;
        }

        float full = ViewModelClips.DataOf(sequence).Seconds(anim.Rate(c));
        return OneShotClock.Remaining(_samples > 1, _prevSequence[c], _prevRemaining[c], sequence, remaining, full, alpha);
    }

    /// <summary>The sampled position (0 to 1) of channel <paramref name="channel"/> in the last draw, or -1 when it drew nothing.</summary>
    public float ClipFrameOf(int channel) => _channels[channel].Clip is null ? -1f : _channels[channel].ClipFrame;

    /// <summary>
    /// The channels for this draw, from <paramref name="anim"/>, sampled from <paramref name="sampler"/>'s clips;
    /// <paramref name="tickAlpha"/> is how far the clock is into the next tick (1 draws the last tick's state).
    /// </summary>
    public ReadOnlySpan<ChannelPose> Update(ViewModelAnim anim, AnimSampler sampler, float dt, float tickAlpha = 1f)
    {
        ArgumentNullException.ThrowIfNull(anim);
        ArgumentNullException.ThrowIfNull(sampler);
        Array.Clear(_channels);
        for (int c = 0; c < ViewModelAnim.ChannelCount; c++)
        {
            Name sequence = anim.Sequence(c);
            float remaining = anim.Remaining(c);
            if (sequence != _sequence[c] || remaining > _remaining[c] + 1e-4f)
            {
                // A new clip, or the same one-shot played again: its length at its play rate (what remains now when
                // the clip's data is not known).
                float full = ViewModelClips.DataOf(sequence).Seconds(anim.Rate(c));
                _duration[c] = full > remaining ? full : remaining;
                _loopTime[c] = 0f;
            }

            _sequence[c] = sequence;
            _remaining[c] = remaining;
            if (sequence.IsNone || sampler.Find(sequence.ToString()) is not { } clip)
            {
                continue;
            }

            float alpha = c == 0 ? 1f : anim.Alpha(c);
            if (alpha <= 0f)
            {
                continue;
            }

            int frames = Math.Max(1, (int)MathF.Round(clip.Length * DefaultFps) + 1);
            float last = (frames - 1f) / frames;
            bool loop = anim.IsAnimating(c) && remaining <= 0f;
            float frame;
            if (loop)
            {
                // LoopAnim's rate scales the clip's own frame rate (the merc's walk loop runs at 0.006 x his speed).
                _loopTime[c] += dt * anim.Rate(c);
                frame = _loopTime[c] * DefaultFps / frames;
                frame -= MathF.Floor(frame);
            }
            else
            {
                float drawn = DrawnRemaining(anim, c, tickAlpha);
                float progress = _duration[c] > 0f ? 1f - (drawn / _duration[c]) : 1f;
                frame = Math.Clamp(progress, 0f, 1f) * last;
            }

            float clipFrame = anim.IsBackward(c) ? MathF.Max(0f, last - frame) : frame;
            _channels[c] = new ChannelPose
            {
                Clip = clip,
                Sequence = sequence.ToString(),
                Frame = frame,
                ClipFrame = clipFrame,
                Frames = frames,
                Fps = DefaultFps,
                Alpha = alpha,
                StartBone = 0,
                Loop = loop,
            };
        }

        return _channels;
    }
}
