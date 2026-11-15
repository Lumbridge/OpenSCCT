using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;

namespace OpenScct.Game.SBase;

public partial class SComputerObjectiveTrigger
{
    // Source COM_ONL dispatcher 0x14051C..0x140641. These materials belong to
    // the original animated terminal's slots 2/3, not the PC screen's slot 1.
    public AssetRef Ps2SkinActive;
    public AssetRef Ps2SkinInactive;
    public AssetRef Ps2SkinHacked;
    public AssetRef Ps2SkinKeyboard;
    public AssetRef Ps2SkinKeyboardHacked;
    // The retained PC animation package contains the same source sequence
    // order, frame counts, rates and notify layout (COM 0x37C022..0x37C13B).
    public Name Ps2HackAnimation = new("nd133_hack_ps2");
    public Name Ps2HackedAnimation = new("nd133_keyboard_in_ps2");
    public Name Ps2AlarmAnimation = new("nd133_alarm_ps2");
    public Name Ps2CtfAnimation = new("nd133_ctf_seq_ps2");

    public byte Ps2VisualState { get; private set; } = 254;
    public Name Ps2AnimationSequence { get; private set; }
    public float Ps2AnimationRate { get; private set; }
    public bool Ps2AnimationBackward { get; private set; }
    public bool Ps2AnimationContinue { get; private set; }
    public int Ps2AnimationSerial { get; private set; }
    /// <summary>Normalized frame at the latest command, including a late joiner's initial snapshot.</summary>
    public float Ps2AnimationStartFrame { get; private set; }
    public float Ps2AnimationFrame => _ps2Frame;

    // Runtime protocol additions, independent of the recovered common class
    // pack. SkinState alone cannot represent pause, reversal or replay of the
    // same state. CommandSerial is last in the wire layout so NativeTick sees
    // a complete snapshot after a received bunch.
    public byte Ps2NetVisualState = 254;
    public Name Ps2NetSequence;
    public float Ps2NetRate;
    public float Ps2NetFrame;
    public float Ps2NetInitialFrame;
    public bool Ps2NetBackward;
    public bool Ps2NetContinue;
    public bool Ps2NetProxy;
    public int Ps2NetAnimationSerial;
    public int Ps2NetCommandSerial;
    private int _ps2ReceivedCommandSerial;
    private int _ps2InitialCaptureTime;
    private bool _ps2Proxy;
    private int _ps2FinishedAnimationSerial = -1;
    private float _ps2Frame;
    private int _ps2RestoreStage;
    private float _ps2RestoreRemaining;
    private int Ps2TotalCaptureTime => DominationCaptureTime + ((Level.Game as SGameInfo)?.MatchInfo?.Handicap ?? 0);
    private bool Ps2Neutralization => (Level.Game as SGameInfo)?.MatchInfo?.GameMode == EGameMode.GM_Neutralization;

    public void Ps2UseAsProxy()
    {
        if (!bPS2Objective) return;
        _ps2Proxy = true;
        bHidden = false;
        SetCollision(true, true, true);
        Ps2CommitVisualCommand();
    }

    /// <summary>Visual animation completion. Old/repeated renderer callbacks cannot change current materials.</summary>
    public void Ps2AnimationFinished(int serial)
    {
        if (!bPS2Objective || serial != Ps2AnimationSerial || serial == _ps2FinishedAnimationSerial) return;
        _ps2FinishedAnimationSerial = serial;
        AnimEnd(0);
    }

    /// <summary>The original nine-way animation/material event, used by linked vials and bomb spots.</summary>
    public void Ps2SetVisualState(byte state, float rate)
    {
        if (!bPS2Objective || state > 8) return;
        // Keep the original byte for tools and the original native layout;
        // the runtime snapshot also carries same-state and pause commands.
        if (Role == ENetRole.ROLE_Authority) SkinState = state;
        LocalSkinState = state;
        Ps2VisualState = state;
        if (state == 5) _ps2Proxy = true;
        bHidden = false;
        if (state is 0 or 2 or 5 or 8) Ps2SetSkin(2, Ps2SkinActive);
        if (state is 3 or 4 or 6 or 7) Ps2SetSkin(2, Ps2SkinInactive);
        if (state is 7 or 8) { Ps2CommitVisualCommand(); return; }
        Name nextSequence = state switch
        {
            0 or 2 => Ps2HackAnimation,
            1 => Ps2HackedAnimation,
            3 or 4 => Ps2AlarmAnimation,
            _ => Ps2CtfAnimation,
        };
        Ps2AnimationRate = state is 3 or 4 ? 1f : rate;
        Ps2AnimationBackward = state is 2 or 4 or 6;
        Ps2AnimationContinue = state is 0 or 2 or 5 or 6;
        if (!Ps2AnimationContinue || nextSequence != Ps2AnimationSequence) _ps2Frame = Ps2AnimationBackward ? 1f : 0f;
        Ps2AnimationSequence = nextSequence;
        Ps2AnimationSerial++;
        Ps2AnimationStartFrame = _ps2Frame;
        Ps2CommitVisualCommand();
    }

    private void Ps2SetSkin(int slot, AssetRef material)
    {
        // An omitted map binding must preserve the source-authored material.
        if (material.IsNone) return;
        while (Skins.Count <= slot) Skins.Add(AssetRef.None);
        Skins[slot] = material;
    }

    protected override void AnimEnd(int channel)
    {
        if (!bPS2Objective) { base.AnimEnd(channel); return; }
        if (_ps2RestoreStage != 0) return;
        if (_ps2Proxy) return; // The source proxy state suppresses AnimEnd.
        Ps2SetSkin(2, bActive ? Ps2SkinActive : Objective is { ObjectiveTaken: false } ? Ps2SkinInactive : Ps2SkinHacked);
        Ps2SetSkin(3, bActive || Objective is { ObjectiveTaken: false } ? Ps2SkinKeyboard : Ps2SkinKeyboardHacked);
    }

    private void Ps2Triggering(bool status)
    {
        base.Triggering(status);
        DeltaCaptureTime = 0f;
        if (status)
        {
            int total = Ps2Neutralization ? Ps2TotalCaptureTime : CaptureTime;
            Ps2SetVisualState(0, total > 0 ? 10f / total : 0f);
            ObjectiveMessages.Broadcast(this, K.MsgNeutralizationInProgress, this);
        }
        else
        {
            ControllerWhoTakesIt = null;
            if (bActive)
            {
                if (Ps2Neutralization) Ps2StopAnimation();
                else Ps2SetVisualState(2, 4f);
            }
        }
        bTriggering = status;
    }

    private void Ps2ReceiveVisualState()
    {
        if (Role == ENetRole.ROLE_Authority || Ps2NetCommandSerial == 0 || Ps2NetCommandSerial == _ps2ReceivedCommandSerial) return;
        bool initial = _ps2ReceivedCommandSerial == 0;
        _ps2ReceivedCommandSerial = Ps2NetCommandSerial;
        LocalSkinState = Ps2VisualState = Ps2NetVisualState;
        if (Ps2VisualState is 0 or 2 or 5 or 8) Ps2SetSkin(2, Ps2SkinActive);
        if (Ps2VisualState is 3 or 4 or 6 or 7) Ps2SetSkin(2, Ps2SkinInactive);
        _ps2Proxy = Ps2NetProxy;
        Ps2AnimationSequence = Ps2NetSequence;
        Ps2AnimationRate = Ps2NetRate;
        Ps2AnimationBackward = Ps2NetBackward;
        Ps2AnimationContinue = Ps2NetContinue;
        _ps2Frame = Ps2AnimationStartFrame = Math.Clamp(initial ? Ps2NetInitialFrame : Ps2NetFrame, 0f, 1f);
        Ps2AnimationSerial = Ps2NetAnimationSerial;
        bHidden = false;
    }

    private void Ps2CommitVisualCommand()
    {
        if (Role != ENetRole.ROLE_Authority) return;
        Ps2NetVisualState = Ps2VisualState;
        Ps2NetSequence = Ps2AnimationSequence;
        Ps2NetRate = Ps2AnimationRate;
        Ps2NetFrame = Ps2NetInitialFrame = _ps2Frame;
        Ps2NetBackward = Ps2AnimationBackward;
        Ps2NetContinue = Ps2AnimationContinue;
        Ps2NetProxy = _ps2Proxy;
        Ps2NetAnimationSerial = Ps2AnimationSerial;
        Ps2NetCommandSerial++;
    }

    private void Ps2StopAnimation()
    {
        Ps2AnimationRate = 0f;
        Ps2AnimationContinue = true;
        Ps2AnimationSerial++;
        Ps2AnimationStartFrame = _ps2Frame;
        Ps2CommitVisualCommand();
    }

    private void Ps2LockConsole()
    {
        if (Ps2Neutralization)
        {
            if (bTriggering) Triggering(false);
            bLocked = true;
            Ps2SetVisualState(2, 4f);
            _ps2RestoreStage = 3;
            _ps2RestoreRemaining = _ps2Frame * 2.5f;
        }
        else
        {
            Ps2SetVisualState(3, 1f);
            GotoState(K.s_TemporaryInactive);
        }
    }

    private void Ps2BeginUnlock()
    {
        Ps2SetVisualState(4, 1f);
        _ps2RestoreStage = 1;
        _ps2RestoreRemaining = 2.5f;
    }

    private void Ps2TickAnimation(float dt)
    {
        float seconds = Ps2AnimationSequence == Ps2AlarmAnimation ? 2.5f :
            Ps2AnimationSequence == Ps2HackedAnimation ? 25f / 30f : 10f;
        if (Ps2AnimationRate > 0f)
        {
            _ps2Frame = Math.Clamp(_ps2Frame + (Ps2AnimationBackward ? -1f : 1f) * dt * Ps2AnimationRate / seconds, 0f, 1f);
            if (_ps2Frame == (Ps2AnimationBackward ? 0f : 1f)) Ps2AnimationFinished(Ps2AnimationSerial);
        }
        if (Role == ENetRole.ROLE_Authority) Ps2NetInitialFrame = _ps2Frame;
        if (Role != ENetRole.ROLE_Authority || _ps2RestoreStage == 0) return;
        _ps2RestoreRemaining -= dt;
        if (_ps2RestoreRemaining > .0001f) return;
        switch (_ps2RestoreStage)
        {
            case 1:
                Ps2SetVisualState(0, 4f);
                _ps2RestoreStage = 2;
                _ps2RestoreRemaining = Ps2TotalCaptureTime > 0 ?
                    MathF.Max(0f, 2.5f * (Ps2TotalCaptureTime - CaptureTime) / Ps2TotalCaptureTime) : 0f;
                break;
            case 2:
                Ps2StopAnimation();
                _ps2RestoreStage = 0;
                bActive = true;
                bLocked = false;
                GotoState(K.s_Initial);
                break;
            case 3:
                _ps2RestoreStage = 0;
                Ps2SetVisualState(3, 1f);
                GotoState(K.s_TemporaryInactive);
                break;
        }
    }
}
