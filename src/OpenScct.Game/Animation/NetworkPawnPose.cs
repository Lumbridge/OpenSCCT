using System;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Animation;

/// <summary>Presentation supplied by the network, without running proxy gameplay or animation notifies.</summary>
public interface INetworkPawnPose
{
    bool Tick(SBasePawn pawn, float deltaTime);
}

public interface INetworkPawnCommands
{
    bool DrivesPawn(SBasePawn pawn);
    bool RepositionsHeldPawnOnWorldClock(SBasePawn pawn) => false;
    void BeforeOwnerAnimation(SBasePawn pawn) { }
}

/// <summary>Native actions authored externally on an owned pawn, such as the victim half of a grab.</summary>
public interface INetworkPawnActions
{
    SBasePawn? InputOwner { get; set; }
    void Authored(SBasePawn pawn, int action);
}

/// <summary>Receives completed authored channel changes, including several changes within one world tick.</summary>
public interface IPawnAnimationTransitions
{
    void Authored(SBasePawn pawn, int channel, in NetworkAnimChannel state);
}

/// <summary>A channel's authored state. Clip data stays in the shared content, never on the wire.</summary>
public struct NetworkAnimChannel
{
    public Name Sequence, StartBone, QueuedSequence;
    public int Serial;
    public float Frame, Rate, Alpha, TweenRate, TweenWeight, BlendIn, BlendOut, BlendTarget, BlendTimer;
    public float QueuedRate, QueuedTween;
    public bool Loop, Backward, Mirror, GlobalPose, TimedBlend, QueuedLoop, QueuedBackward, NotifyIgnore;

    public static NetworkAnimChannel Capture(PawnAnimChannel c) => new()
    {
        Sequence = c.Sequence, StartBone = c.StartBone, Serial = c.Serial,
        Frame = c.Frame, Rate = c.Rate, Alpha = c.BlendAlpha, TweenRate = c.TweenRate, TweenWeight = c.TweenWeight,
        BlendIn = c.BlendInTime, BlendOut = c.BlendOutTime, BlendTarget = c.BlendTarget, BlendTimer = c.BlendTimer,
        Loop = c.bLoop, Backward = c.bBackward, Mirror = c.bMirror, GlobalPose = c.bGlobalPose, TimedBlend = c.bTimedBlend,
        QueuedSequence = c.QueuedClip?.Name ?? default, QueuedRate = c.QueuedRate, QueuedTween = c.QueuedTween,
        QueuedLoop = c.QueuedLoop, QueuedBackward = c.QueuedBackward,
        NotifyIgnore = c.bNotifyIgnore,
    };

    public readonly bool SamePlay(in NetworkAnimChannel other) =>
        Sequence == other.Sequence && Serial == other.Serial && StartBone == other.StartBone &&
        Loop == other.Loop && Backward == other.Backward && Mirror == other.Mirror && GlobalPose == other.GlobalPose && NotifyIgnore == other.NotifyIgnore &&
        TweenRate == other.TweenRate && BlendIn == other.BlendIn && BlendOut == other.BlendOut &&
        TimedBlend == other.TimedBlend && BlendTarget == other.BlendTarget &&
        QueuedSequence == other.QueuedSequence && QueuedRate == other.QueuedRate && QueuedTween == other.QueuedTween &&
        QueuedLoop == other.QueuedLoop && QueuedBackward == other.QueuedBackward;

    public readonly void Apply(SBasePawn pawn, int channel, bool presentationOnly = true)
    {
        PawnClip? clip = Sequence.IsNone ? null : PawnActions.Clip(pawn, Sequence);
        PawnAnimChannel c = pawn.PawnAnim[channel];
        if (clip is null) c.Clear();
        c.Clip = clip;
        c.Sequence = Sequence;
        c.Frames = clip?.Frames ?? 0;
        c.AnimLast = clip is { Frames: > 1 } ? 1f - 1f / clip.Frames : 0f;
        c.BaseRate = clip is null ? 0f : clip.Fps / clip.Frames;
        c.Serial = Serial;
        c.Frame = Frame; c.Rate = Rate; c.OldAnimRate = Rate; c.BlendAlpha = Alpha;
        c.TweenRate = TweenRate; c.TweenWeight = TweenWeight;
        c.StartBone = StartBone; c.BlendInTime = BlendIn; c.BlendOutTime = BlendOut;
        c.BlendTarget = BlendTarget; c.BlendTimer = BlendTimer; c.bTimedBlend = TimedBlend;
        c.bLoop = Loop; c.bBackward = Backward; c.bMirror = Mirror; c.bGlobalPose = GlobalPose;
        c.bNotifyIgnore = presentationOnly || NotifyIgnore; c.bAnimNotify = !presentationOnly && clip?.Notifies.Count > 0;
        c.TweenCacheFrame = Frame; c.TweenCacheSequence = Sequence;
        c.bQueued = !QueuedSequence.IsNone;
        c.QueuedClip = QueuedSequence.IsNone ? null : PawnActions.Clip(pawn, QueuedSequence);
        c.QueuedRate = QueuedRate; c.QueuedTween = QueuedTween; c.QueuedLoop = QueuedLoop; c.QueuedBackward = QueuedBackward;
    }

    public static NetworkAnimChannel Interpolate(in NetworkAnimChannel a, in NetworkAnimChannel b, float alpha)
    {
        if (!a.SamePlay(b)) return alpha >= 1f ? b : a;
        NetworkAnimChannel result = a;
        float end = b.Frame;
        if (a.Loop && a.Frame >= 0f && end >= 0f && end < a.Frame - 0.5f) end += 1f;
        result.Frame = a.Frame + (end - a.Frame) * alpha;
        if (result.Frame >= 1f && a.Loop) result.Frame -= 1f;
        result.Alpha += (b.Alpha - a.Alpha) * alpha;
        result.Rate += (b.Rate - a.Rate) * alpha;
        result.TweenWeight += (b.TweenWeight - a.TweenWeight) * alpha;
        return result;
    }

    public readonly NetworkAnimChannel Extrapolate(float seconds, float speed, float lastFrame)
    {
        NetworkAnimChannel result = this;
        if (result.Frame < 0f)
        {
            float left = TweenRate > 0f ? -result.Frame / TweenRate : seconds;
            float tween = Math.Min(seconds, left);
            result.Frame += tween * TweenRate;
            seconds -= tween;
        }

        if (seconds > 0f && result.Frame >= 0f)
        {
            float rate = Rate < 0f ? Math.Max(0.3f, -Rate * speed) : Rate;
            result.Frame += seconds * rate;
            if (Loop) result.Frame -= (int)result.Frame;
            else result.Frame = Math.Min(result.Frame, lastFrame);
        }

        if (TimedBlend && BlendTimer > 0f)
            result.Alpha += (BlendTarget - Alpha) * Math.Min(1f, seconds / BlendTimer);
        return result;
    }
}
