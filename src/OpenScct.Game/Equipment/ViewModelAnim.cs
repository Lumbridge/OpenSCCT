using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The animation channels of a weapon's view model (and of the spy's pistol): which clip each channel plays, its
/// blend alpha, and when a one-shot ends. The weapon advances them in its tick and gets AnimEnd(channel) when a
/// one-shot reaches its end, which is what drives the weapon states (reload, shot, draw, holster, electric).
/// </summary>
public sealed class ViewModelAnim
{
    /// <summary>Channels 0 to 7 (the rifle uses 0 to 6).</summary>
    public const int ChannelCount = 8;

    private readonly Name[] _sequence = new Name[ChannelCount];
    private readonly float[] _remaining = new float[ChannelCount];
    private readonly bool[] _loop = new bool[ChannelCount];
    private readonly bool[] _playing = new bool[ChannelCount];
    private readonly bool[] _backward = new bool[ChannelCount];
    private readonly float[] _alpha = new float[ChannelCount];
    private readonly float[] _rate = new float[ChannelCount];

    /// <summary>The clip a channel plays or last played (a finished one-shot keeps its name, as in the engine).</summary>
    public Name Sequence(int Channel) => _sequence[Channel];

    /// <summary>True while a channel plays a clip.</summary>
    public bool IsAnimating(int Channel) => _playing[Channel];

    /// <summary>True when the channel plays its clip backward.</summary>
    public bool IsBackward(int Channel) => _backward[Channel];

    /// <summary>The channel's blend alpha (AnimBlendParams).</summary>
    public float Alpha(int Channel) => _alpha[Channel];

    /// <summary>The play rate of the channel's loop (LoopAnim's Rate: 1 plays the clip at its own frame rate).</summary>
    public float Rate(int Channel) => _rate[Channel];

    /// <summary>Seconds before the channel's one-shot ends.</summary>
    public float Remaining(int Channel) => _remaining[Channel];

    /// <summary>Sets a channel's blend alpha.</summary>
    public void SetAlpha(int Channel, float Alpha) => _alpha[Channel] = Alpha;

    /// <summary>PlayAnim: a one-shot of <paramref name="Clip"/> at <paramref name="Rate"/>.</summary>
    public void Play(int Channel, Name Sequence, ClipData Clip, float Rate = 1f, bool Backward = false)
    {
        _sequence[Channel] = Sequence;
        _rate[Channel] = Rate;
        _remaining[Channel] = Clip.Seconds(Rate);
        _loop[Channel] = false;
        _playing[Channel] = true;
        _backward[Channel] = Backward;
    }

    /// <summary>
    /// LoopAnim: the clip loops at <paramref name="Rate"/> until another one replaces it. Looping the clip already playing
    /// only changes its rate (the loop keeps its phase), as the engine's PlayAnim does for the same sequence.
    /// </summary>
    public void Loop(int Channel, Name Sequence, float Rate = 1f)
    {
        _rate[Channel] = Rate;
        _sequence[Channel] = Sequence;
        _remaining[Channel] = 0f;
        _loop[Channel] = true;
        _playing[Channel] = true;
        _backward[Channel] = false;
    }

    /// <summary>
    /// SetAnimFrame in frames: the channel's one-shot jumps to <paramref name="Frame"/> of <paramref name="Clip"/> (the
    /// shotgun's reload skips the shells it does not need). A loop or a stopped channel is left alone.
    /// </summary>
    public void SetFrame(int Channel, float Frame, ClipData Clip)
    {
        if (!_playing[Channel] || _loop[Channel] || Clip.Frames <= 1 || _rate[Channel] <= 0f)
        {
            return;
        }

        float left = (Clip.Frames - 1 - Frame) / (Clip.Fps * _rate[Channel]);
        _remaining[Channel] = left > 0f ? left : 0f;
    }

    /// <summary>Seconds a channel's one-shot of <paramref name="Clip"/> has played (0 for a loop or a stopped channel).</summary>
    public float Elapsed(int Channel, ClipData Clip) =>
        _playing[Channel] && !_loop[Channel] ? Clip.Seconds(_rate[Channel]) - _remaining[Channel] : 0f;

    /// <summary>Stops a channel (no AnimEnd).</summary>
    public void Stop(int Channel)
    {
        _playing[Channel] = false;
        _loop[Channel] = false;
        _remaining[Channel] = 0f;
    }

    /// <summary>
    /// Advances every channel by <paramref name="DeltaTime"/>; each one-shot that ends stops and calls
    /// <paramref name="AnimEnd"/> with its channel (in channel order).
    /// </summary>
    public void Advance(float DeltaTime, Actor Owner, Action<Actor, int> AnimEnd)
    {
        for (int c = 0; c < ChannelCount; c++)
        {
            if (!_playing[c] || _loop[c])
            {
                continue;
            }

            _remaining[c] -= DeltaTime;
            if (_remaining[c] <= 0f)
            {
                _playing[c] = false;
                _remaining[c] = 0f;
                AnimEnd(Owner, c);
            }
        }
    }
}

/// <summary>The clip names and their decoded frame data.</summary>
[Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx clip frames and rates (merc-rifle.md, spy-shocker.md)")]
public static class ViewModelClips
{
    public static readonly Name FirstShooStFd2 = new("FirstShooStFd2");
    public static readonly Name FirstReloStFd2 = new("FirstReloStFd2");
    public static readonly Name FirstReloStFd1 = new("FirstReloStFd1");
    public static readonly Name FirstDrawStEd2 = new("FirstDrawStEd2");
    public static readonly Name FirstGrbdStBg2 = new("FirstGrbdStBg2");
    public static readonly Name FirstElecStFd2 = new("FirstElecStFd2");
    public static readonly Name FirstSnipStBg2 = new("FirstSnipStBg2");
    public static readonly Name FirstWalkStFd2 = new("FirstWalkStFd2");
    public static readonly Name FirstWaitStFd2 = new("FirstWaitStFd2");
    public static readonly Name FirstJumpStFd2 = new("FirstJumpStFd2");
    public static readonly Name FirstFallStFd2 = new("FirstFallStFd2");
    public static readonly Name FirstLandStFd2 = new("FirstLandStFd2");
    public static readonly Name FirstLandStHi2 = new("FirstLandStHi2");
    public static readonly Name FirstStrkStFd2 = new("FirstStrkStFd2");
    public static readonly Name FirstReanStFd2 = new("FirstReanStFd2");
    public static readonly Name DrawP190 = new("DrawP190");
    public static readonly Name SnipeP190 = new("SnipeP190");
    public static readonly Name WaitP190 = new("WaitP190");
    public static readonly Name FirstButtStFd2 = new("FirstButtStFd2");
    public static readonly Name FirstMineStBg2 = new("FirstMineStBg2");
    public static readonly Name FirstMineStNt2 = new("FirstMineStNt2");
    public static readonly Name FirstMineStEd2 = new("FirstMineStEd2");
    public static readonly Name FirstAtakChEd2 = new("FirstAtakChEd2");
    public static readonly Name FirstAtakChit2 = new("FirstAtakChit2");
    public static readonly Name FirstAtakCrFd2 = new("FirstAtakCrFd2");
    public static readonly Name FirstShooStFd2U = new("FirstShooStFd2U");
    public static readonly Name FirstShooStFd2Ub = new("FirstShooStFd2Ub");
    public static readonly Name FirstReloStFd2U = new("FirstReloStFd2U");
    public static readonly Name FirstShooStFd2S = new("FirstShooStFd2S");
    public static readonly Name FirstReloStFd2S = new("FirstReloStFd2S");

    /// <summary>The clip data of a sequence (an unknown clip ends at once).</summary>
    public static ClipData DataOf(Name Sequence)
    {
        if (Sequence == FirstShooStFd2)
        {
            return K.FirstShooStFd2;
        }

        if (Sequence == FirstReloStFd2)
        {
            return K.FirstReloStFd2;
        }

        if (Sequence == FirstReloStFd1)
        {
            return K.FirstReloStFd1;
        }

        if (Sequence == FirstDrawStEd2)
        {
            return K.FirstDrawStEd2;
        }

        if (Sequence == FirstLandStFd2)
        {
            return K.FirstLandStFd2;
        }

        if (Sequence == FirstLandStHi2)
        {
            return K.FirstLandStHi2;
        }

        if (Sequence == FirstGrbdStBg2)
        {
            return K.FirstGrbdStBg2;
        }

        if (Sequence == FirstElecStFd2)
        {
            return K.FirstElecStFd2;
        }

        if (Sequence == FirstSnipStBg2)
        {
            return K.FirstSnipStBg2;
        }

        if (Sequence == DrawP190)
        {
            return K.DrawP190;
        }

        if (Sequence == SnipeP190)
        {
            return K.SnipeP190;
        }

        if (Sequence == WaitP190)
        {
            return K.WaitP190;
        }

        if (Sequence == FirstButtStFd2)
        {
            return K.FirstButtStFd2;
        }

        if (Sequence == FirstMineStBg2)
        {
            return K.FirstMineStBg2;
        }

        if (Sequence == FirstMineStNt2)
        {
            return K.FirstMineStNt2;
        }

        if (Sequence == FirstMineStEd2)
        {
            return K.FirstMineStEd2;
        }

        if (Sequence == FirstAtakChEd2)
        {
            return K.FirstAtakChEd2;
        }

        if (Sequence == FirstAtakChit2)
        {
            return K.FirstAtakChit2;
        }

        if (Sequence == FirstAtakCrFd2)
        {
            return K.FirstAtakCrFd2;
        }

        if (Sequence == FirstShooStFd2U)
        {
            return K.FirstShooStFd2U;
        }

        if (Sequence == FirstShooStFd2Ub)
        {
            return K.FirstShooStFd2Ub;
        }

        if (Sequence == FirstReloStFd2U)
        {
            return K.FirstReloStFd2U;
        }

        if (Sequence == FirstShooStFd2S)
        {
            return K.FirstShooStFd2S;
        }

        if (Sequence == FirstReloStFd2S)
        {
            return K.FirstReloStFd2S;
        }

        return new ClipData(1, 15f);
    }
}
