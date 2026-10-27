using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Mover state infrastructure: the state table, the shared state base with the Mover functions a state may
// override, the probe mask (Enable / Disable) and the constants.
public partial class Mover
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates,
        BumpButton.Instance, BumpOpenTimed.Instance, CloseTimedMover.Instance, OpenTimedMover.Instance,
        StandOpenTimed.Instance, TriggerCloseOnceOnly.Instance, TriggerCloseTimed.Instance, TriggerControl.Instance,
        TriggerOpenOnceOnly.Instance, TriggerOpenTimed.Instance, TriggerPound.Instance, TriggerToggle.Instance,
        s_ReturnNoEncroach.Instance, s_TemporaryInactive.Instance, s_WaitAndRetry.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name NAME_Open = new("Open");
    private static readonly Name NAME_Close = new("Close");
    private static readonly Name NAME_Gib = new("Gib");
    private static readonly Name NAME_Trigger = new("Trigger");
    private static readonly Name NAME_UnTrigger = new("UnTrigger");
    private static readonly Name NAME_Bump = new("Bump");
    private static readonly Name NAME_Attach = new("Attach");
    private static readonly Name NAME_TriggerOpenOnceOnly = new(nameof(TriggerOpenOnceOnly));
    private static readonly Name NAME_TriggerCloseOnceOnly = new(nameof(TriggerCloseOnceOnly));
    private static readonly Name NAME_s_TemporaryInactive = new(nameof(s_TemporaryInactive));
    private static readonly Name NAME_s_WaitAndRetry = new(nameof(s_WaitAndRetry));
    private static readonly Name NAME_s_ReturnNoEncroach = new(nameof(s_ReturnNoEncroach));

    // ---- probe mask ---------------------------------------------------------------------------------------

    // Bits of the state frame's probe mask (events.md rule 13) for the probe functions Mover states switch.
    private const ulong ProbeTrigger = 1UL << 6;
    private const ulong ProbeUnTrigger = 1UL << 7;
    private const ulong ProbeBump = 1UL << 15;
    private const ulong ProbeAttach = 1UL << 19;

    private ulong _disabledProbes;
    private int _jumps;

    /// <summary>Probe functions switched off with <see cref="Disable"/> in the current state.</summary>
    public ulong DisabledProbes => _disabledProbes;

    /// <summary>Switches a probe function off until the state changes or <see cref="Enable"/> (UnrealScript Disable).</summary>
    [UcNative("UObject::execDisable", Exe = 0x10949b10)]
    [PortOf("nat:Core.Object.Disable", Spec = "spec/devices/events.md#core-state-machine-rules-the-devices-depend-on")]
    [Provenance(ProvenanceKind.Estimated, "UE2 probe mask rule, not yet confirmed in this binary (events.md open question 1)")]
    public void Disable(Name ProbeFunc) => _disabledProbes |= ProbeBit(ProbeFunc);

    /// <summary>Switches a probe function back on (UnrealScript Enable).</summary>
    [UcNative("UObject::execEnable", Exe = 0x10949a40)]
    [PortOf("nat:Core.Object.Enable", Spec = "spec/devices/events.md#core-state-machine-rules-the-devices-depend-on")]
    public void Enable(Name ProbeFunc) => _disabledProbes &= ~ProbeBit(ProbeFunc);

    private bool IsProbeEnabled(ulong bit) => (_disabledProbes & bit) == 0;

    private static ulong ProbeBit(Name probe)
    {
        if (probe == NAME_Trigger)
        {
            return ProbeTrigger;
        }

        if (probe == NAME_UnTrigger)
        {
            return ProbeUnTrigger;
        }

        if (probe == NAME_Bump)
        {
            return ProbeBump;
        }

        return probe == NAME_Attach ? ProbeAttach : 0UL;
    }

    /// <summary>
    /// GotoState with the probe rule: entering a different state recomputes the probe mask (every Disable is undone),
    /// going to the current state keeps it (events.md rules 15 and 16).
    /// </summary>
    public override EGotoState GotoState(Name NewState = default, Name Label = default)
    {
        _jumps++;
        if (NewState != GetStateName())
        {
            _disabledProbes = 0;
        }

        return base.GotoState(NewState, Label);
    }

    // ---- dispatchers for the Mover functions states override ----------------------------------------------

    /// <summary>EncroachingOn through the current state (s_WaitAndRetry and s_ReturnNoEncroach never refuse).</summary>
    public bool eventEncroachingOn(Actor? Other) =>
        StateNode is IEncroachingOn s ? s.EncroachingOn(this, Other) : EncroachingOn(Other);

    public void eventSubTrigger(Actor? Other, Pawn? EventInstigator)
    {
        if (StateNode is MoverState s)
        {
            s.SubTrigger(this, Other, EventInstigator);
        }
        else
        {
            SubTrigger(Other, EventInstigator);
        }
    }

    public void eventSubUnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        if (StateNode is MoverState s)
        {
            s.SubUnTrigger(this, Other, EventInstigator);
        }
        else
        {
            SubUnTrigger(Other, EventInstigator);
        }
    }

    /// <summary>DisableTrigger exists only in states; outside them it does nothing.</summary>
    public void eventDisableTrigger() => (StateNode as MoverState)?.DisableTrigger(this);

    /// <summary>EnableTrigger exists only in states; outside them it does nothing.</summary>
    public void eventEnableTrigger() => (StateNode as MoverState)?.EnableTrigger(this);

    public void eventLock()
    {
        if (StateNode is MoverState s)
        {
            s.Lock(this);
        }
        else
        {
            Lock();
        }
    }

    public void eventUnLock()
    {
        if (StateNode is MoverState s)
        {
            s.UnLock(this);
        }
        else
        {
            UnLock();
        }
    }

    /// <summary>
    /// Base of the Mover states. Trigger, UnTrigger, Bump and Attach first check the probe mask (a Disable'd probe
    /// does nothing), then call the state's version, which by default is the class-level one.
    /// </summary>
    public abstract class MoverState : UcActorState
    {
        protected MoverState(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        public sealed override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var mover = (Mover)self;
            if (mover.IsProbeEnabled(ProbeTrigger))
            {
                Trigger(mover, Other, EventInstigator);
            }
        }

        public sealed override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var mover = (Mover)self;
            if (mover.IsProbeEnabled(ProbeUnTrigger))
            {
                UnTrigger(mover, Other, EventInstigator);
            }
        }

        public sealed override void Bump(Actor self, Actor Other)
        {
            var mover = (Mover)self;
            if (mover.IsProbeEnabled(ProbeBump))
            {
                Bump(mover, Other);
            }
        }

        public sealed override void Attach(Actor self, Actor Other)
        {
            var mover = (Mover)self;
            if (mover.IsProbeEnabled(ProbeAttach))
            {
                Attach(mover, Other);
            }
        }

        public virtual void Trigger(Mover self, Actor? Other, Pawn? EventInstigator) => self.Trigger(Other, EventInstigator);

        public virtual void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator) => self.UnTrigger(Other, EventInstigator);

        public virtual void Bump(Mover self, Actor Other) => self.Bump(Other);

        public virtual void Attach(Mover self, Actor Other) => self.Attach(Other);

        public virtual void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator) => self.SubTrigger(Other, EventInstigator);

        public virtual void SubUnTrigger(Mover self, Actor? Other, Pawn? EventInstigator) => self.SubUnTrigger(Other, EventInstigator);

        public virtual void DisableTrigger(Mover self)
        {
        }

        public virtual void EnableTrigger(Mover self)
        {
        }

        public virtual void Lock(Mover self) => self.Lock();

        public virtual void UnLock(Mover self) => self.UnLock();
    }

    /// <summary>The shared filter of BumpOpenTimed, StandOpenTimed and BumpButton: true when the bump may open the mover.</summary>
    private bool PassesBumpType(Actor Other)
    {
        var p = Other as Pawn;
        if (BumpType != EBumpType.BT_AnyBump && p is null)
        {
            return false;
        }

        if (BumpType == EBumpType.BT_PlayerBump && !IsPlayerPawn(p))
        {
            return false;
        }

        return !(BumpType == EBumpType.BT_PawnBump && Other.Mass < 10f);
    }

    /// <summary>Starts a state code segment: remembers the jump count so a jump made by a called function ends it.</summary>
    private int Segment(ref LatentFrame f, int pc)
    {
        f.Pc = pc;
        return _jumps;
    }

    /// <summary>True when something the segment called moved the state code elsewhere (the segment must stop).</summary>
    private bool Jumped(int mark) => mark != _jumps;
}
