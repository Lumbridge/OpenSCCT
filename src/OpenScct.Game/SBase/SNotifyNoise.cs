using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SNotifyNoise: while enabled, every fTimeRateInSec seconds (at least 1) each machine's local player gets a
/// noise of fIntensity at the actor, the merc's sound arrow (spec/devices/notify-noise.md). An event enables it, an
/// untrigger disables it; the state change is broadcast so every machine runs its own timer.
/// </summary>
public partial class SNotifyNoise
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_Wait.Instance, s_MakeNoise.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name NAME_ChangeStateToAll = new("ChangeStateToAll");

    /// <summary>The shortest period between two noises.</summary>
    [Provenance(ProvenanceKind.Decoded, "SNotifyNoise.PostNetBeginPlay: fTimeRateInSec < 1.0 -> 1.0")]
    public const float MinTimeRate = 1f;

    /// <summary>Enters <paramref name="_StateName"/> here; the server's call also goes to every client with a channel.</summary>
    [Uc("SBase.SNotifyNoise.ChangeStateToAll", Sha1 = "555844d7616263be26968ebe99e92e26e9cd9eb1")]
    [PortOf("uc:SBase.SNotifyNoise:ChangeStateToAll", Spec = "spec/devices/notify-noise.md#rules")]
    public void ChangeStateToAll(Name _StateName) => GotoState(_StateName);

    /// <summary>ChangeStateToAll on every machine: sent where its replication says, run here when it says.</summary>
    private void CallChangeStateToAll(Name state)
    {
        IDeviceNet net = XLevel.GetService<IDeviceNet>() ?? DeviceNetLocal.Instance;
        if (Role < ENetRole.ROLE_Authority || net.Call(this, new GameCall(NAME_ChangeStateToAll) { Name1 = state }))
        {
            ChangeStateToAll(state);
        }
    }

    /// <summary>Clamps the period, hides the actor and starts in s_MakeNoise or s_Wait by bEnabled (a late joiner too).</summary>
    [Uc("SBase.SNotifyNoise.PostNetBeginPlay", Sha1 = "c1d3f4e7a5a571f504472240b475e7e9bbf1e08c")]
    [PortOf("uc:SBase.SNotifyNoise:PostNetBeginPlay", Spec = "spec/devices/notify-noise.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (fTimeRateInSec < MinTimeRate)
        {
            fTimeRateInSec = MinTimeRate;
        }

        SetDrawType(EDrawType.DT_None);
        CallChangeStateToAll(bEnabled ? nameof(s_MakeNoise) : nameof(s_Wait));
    }

    /// <summary>The local player's controller hears a noise of fIntensity here.</summary>
    [Uc("SBase.SNotifyNoise.Timer", Sha1 = "df82b8936e3debbd39f459319a678b672f6a1d58")]
    [PortOf("uc:SBase.SNotifyNoise:Timer", Spec = "spec/devices/notify-noise.md#rules")]
    protected override void Timer() => Level.GetLocalPlayerController()?.ClientNotifyNoise(this, fIntensity);

    [Uc("SBase.SNotifyNoise.Trigger", Sha1 = "7e4a799e0362e6fa089c7a27ae7416a41aced696")]
    [PortOf("uc:SBase.SNotifyNoise:Trigger", Spec = "spec/devices/notify-noise.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        bEnabled = true;
        CallChangeStateToAll(nameof(s_MakeNoise));
    }

    [Uc("SBase.SNotifyNoise.UnTrigger", Sha1 = "6efa6225f3a053069c3b3b0eb0a57ab39f7d8f72")]
    [PortOf("uc:SBase.SNotifyNoise:UnTrigger", Spec = "spec/devices/notify-noise.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        bEnabled = false;
        CallChangeStateToAll(nameof(s_Wait));
    }

    /// <summary>Silent: the timer is off.</summary>
    [Uc("SBase.SNotifyNoise.s_Wait")]
    public class s_Wait : UcActorState
    {
        public static readonly s_Wait Instance = new(UcMeta.s_Wait, null);

        protected s_Wait(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SNotifyNoise.s_Wait.BeginState", Sha1 = "057abcab77d799acded157e2806b0d9b76a36765")]
        [PortOf("uc:SBase.SNotifyNoise:s_Wait.BeginState", Spec = "spec/devices/notify-noise.md#rules")]
        public override void BeginState(Actor self) => self.SetTimer(0f, false);
    }

    /// <summary>Noisy: a repeating timer of fTimeRateInSec.</summary>
    [Uc("SBase.SNotifyNoise.s_MakeNoise")]
    public class s_MakeNoise : UcActorState
    {
        public static readonly s_MakeNoise Instance = new(UcMeta.s_MakeNoise, null);

        protected s_MakeNoise(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SNotifyNoise.s_MakeNoise.BeginState", Sha1 = "79590781f9fd772f82946d9e41a15e39584fb1d2")]
        [PortOf("uc:SBase.SNotifyNoise:s_MakeNoise.BeginState", Spec = "spec/devices/notify-noise.md#rules")]
        public override void BeginState(Actor self) => self.SetTimer(((SNotifyNoise)self).fTimeRateInSec, true);
    }
}
