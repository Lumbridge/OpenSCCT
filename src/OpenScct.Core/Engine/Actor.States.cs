using System;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// State machine and latent state code (UE2 FStateFrame, UObject::GotoState, GotoLabel, ProcessState).
public abstract partial class Actor
{
    /// <summary>Most latent code segments run per tick before a runaway loop is assumed (UE2 runaway check).</summary>
    public const int MaxLatentStepsPerTick = 100000;

    /// <summary>Sleeps longer than this many ticks never wake (UE2 floats stop counting down long before).</summary>
    public const long MaxSleepTicks = 1_000_000;

    protected static readonly Name NAME_Auto = new("Auto");
    protected static readonly Name NAME_Begin = new("Begin");

    private ActorState _state = ActorState.None;
    private ActorState? _codeState;
    private LatentFrame _frame;
    private bool _stateChanged;
    private bool _inEndState;

    /// <summary>The state this class and its parents declare (override in classes that declare states).</summary>
    public static readonly StateTable ClassStates = StateTable.Empty;

    /// <summary>Returns the state table of the actor's class. Classes with states return their ClassStates.</summary>
    public virtual StateTable GetStateTable() => ClassStates;

    /// <summary>Current state node; <see cref="ActorState.None"/> when in no state.</summary>
    public ActorState StateNode => _state;

    /// <summary>Serializable state machine data.</summary>
    public ref readonly LatentFrame StateFrame => ref _frame;

    /// <summary>Number of times latent code hit the runaway limit (diagnostics).</summary>
    public int RunawayLatentLoops { get; private set; }

    /// <summary>Current state name, or the class name when in no state (as UE2 GetStateName does).</summary>
    public Name GetStateName() => ReferenceEquals(_state, ActorState.None) ? ClassName : _state.Name;

    /// <summary>True when the current state is <paramref name="StateName"/> or extends it.</summary>
    public bool IsInState(Name StateName) => !ReferenceEquals(_state, ActorState.None) && _state.IsOrExtends(StateName);

    /// <summary>
    /// UnrealScript GotoState(NewState, Label). Changing state sends EndState to the old state and BeginState to the
    /// new one, then jumps to <paramref name="Label"/> (Begin when None). Going to the current state only jumps to
    /// the label. An unknown state leaves the actor in no state.
    /// </summary>
    [UcNative("UObject::execGotoState")]
    public virtual EGotoState GotoState(Name NewState = default, Name Label = default)
    {
        EGotoState result = EGotoState.GOTOSTATE_Success;
        if (NewState != GetStateName())
        {
            result = GotoStateNode(NewState);
        }

        if (result == EGotoState.GOTOSTATE_Success)
        {
            GotoLabel(Label.IsNone ? NAME_Begin : Label);
        }

        return result;
    }

    [UcNative("UObject::GotoState")]
    private EGotoState GotoStateNode(Name newState)
    {
        _frame.Wait = LatentWait.None;
        _frame.UntilTick = 0;
        _frame.Channel = 0;
        StateTable table = GetStateTable();
        ActorState? node = newState == NAME_Auto ? table.AutoState : table.Find(newState);
        if (node is null)
        {
            newState = Name.None;
        }
        else if (newState == NAME_Auto)
        {
            newState = node.Name;
        }

        Name oldName = ReferenceEquals(_state, ActorState.None) ? Name.None : _state.Name;

        if (!oldName.IsNone && newState != oldName && !_inEndState)
        {
            _stateChanged = false;
            _inEndState = true;
            try
            {
                eventEndState();
            }
            finally
            {
                _inEndState = false;
            }

            if (_stateChanged)
            {
                return EGotoState.GOTOSTATE_Preempted;
            }
        }

        _state = node ?? ActorState.None;
        _frame.StateId = _state.Id;
        _frame.Pc = LatentFrame.NoCode;
        _frame.LabelId = 0;
        _codeState = null;

        if (!newState.IsNone && newState != oldName)
        {
            _stateChanged = false;
            eventBeginState();
            if (_stateChanged)
            {
                return EGotoState.GOTOSTATE_Preempted;
            }
        }

        if (!newState.IsNone)
        {
            _stateChanged = true;
            return EGotoState.GOTOSTATE_Success;
        }

        return EGotoState.GOTOSTATE_NotFound;
    }

    /// <summary>
    /// Jumps the latent code to <paramref name="Label"/>, searching the current state and then its super states
    /// (UnrealScript Goto in state code). Returns false, and stops the code, when no state has the label.
    /// </summary>
    [UcNative("UObject::GotoLabel")]
    public bool GotoLabel(Name Label)
    {
        _frame.Wait = LatentWait.None;
        _frame.UntilTick = 0;
        if (!Label.IsNone)
        {
            for (ActorState? s = _state; s is not null; s = s.Super)
            {
                int index = s.FindLabel(Label);
                if (index >= 0)
                {
                    StateLabel label = s.Labels[index];
                    _codeState = s;
                    _frame.LabelId = label.Id;
                    _frame.Pc = label.Pc;
                    return true;
                }
            }
        }

        _codeState = null;
        _frame.LabelId = 0;
        _frame.Pc = LatentFrame.NoCode;
        return false;
    }

    internal void StopLatentCodeForDestroy()
    {
        _frame.Pc = LatentFrame.NoCode;
        _frame.Wait = LatentWait.None;
        _codeState = null;
    }

    /// <summary>Stops the latent code (UnrealScript stop).</summary>
    protected void StopLatentCode()
    {
        _frame.Pc = LatentFrame.NoCode;
        _frame.Wait = LatentWait.None;
    }

    /// <summary>
    /// Restores a saved frame. The current state and code owner are found again by id, so the frame can come from
    /// another process.
    /// </summary>
    public void RestoreStateFrame(in LatentFrame frame)
    {
        if (frame.StateId == 0 && frame.Pc == LatentFrame.NoCode)
        {
            _state = ActorState.None; _codeState = null; _frame = frame;
            return;
        }
        ActorState state = GetStateTable().FindById(frame.StateId)
            ?? throw new InvalidOperationException($"{ClassName} has no state with id {frame.StateId}.");
        ActorState? codeState = null;
        if (frame.Pc != LatentFrame.NoCode)
        {
            for (ActorState? s = state; s is not null && codeState is null; s = s.Super)
            {
                if (s.FindLabelById(frame.LabelId) >= 0)
                {
                    codeState = s;
                }
            }

            if (codeState is null)
            {
                throw new InvalidOperationException($"{ClassName} state {state.Name} has no label with id {frame.LabelId}.");
            }
        }

        _state = state;
        _codeState = codeState;
        _frame = frame;
    }

    // ---- latent functions ----------------------------------------------------------------------------------

    /// <summary>
    /// Latent Sleep. UE2 subtracts DeltaSeconds from the remaining time each tick and wakes once it is below half a
    /// tick; on a fixed tick that is a whole number of ticks, worked out here with the same float steps.
    /// </summary>
    [UcNative("AActor::execSleep")]
    protected void Sleep(float Seconds)
    {
        LatentFloat = Seconds;
        long ticks = SleepTicks(Seconds, XLevel.DeltaTime);
        _frame.Wait = LatentWait.Sleep;
        _frame.UntilTick = ticks == long.MaxValue ? long.MaxValue : XLevel.TickCount + ticks;
    }

    /// <summary>Ticks until a Sleep(<paramref name="seconds"/>) wakes, polling every <paramref name="deltaTime"/>.</summary>
    [UcNative("AActor::execPollSleep")]
    public static long SleepTicks(float seconds, float deltaTime)
    {
        if (float.IsNaN(seconds))
        {
            return long.MaxValue;
        }

        if (!(deltaTime > 0f))
        {
            return 1;
        }

        if (seconds > deltaTime * MaxSleepTicks)
        {
            return long.MaxValue;
        }

        float remaining = seconds;
        float half = 0.5f * deltaTime;
        long n = 0;
        do
        {
            remaining -= deltaTime;
            n++;
        }
        while (!(remaining < half) && n < MaxSleepTicks);

        return n;
    }

    /// <summary>Latent FinishAnim: waits for the animation on <paramref name="Channel"/> to end.</summary>
    protected void FinishAnim(int Channel = 0)
    {
        _frame.Wait = LatentWait.FinishAnim;
        _frame.Channel = Channel;
    }

    /// <summary>Latent FinishInterpolation: waits for bInterpolating to clear.</summary>
    protected void FinishInterpolation()
    {
        _frame.Wait = LatentWait.FinishInterpolation;
    }

    /// <summary>Latent WaitForLanding: waits until the actor is no longer falling.</summary>
    protected void WaitForLanding()
    {
        _frame.Wait = LatentWait.WaitForLanding;
    }

    /// <summary>Starts any latent wait; for latent natives ported in other libraries (MoveTo...).</summary>
    protected void SetLatentWait(LatentWait wait, long untilTick = 0, int channel = 0)
    {
        _frame.Wait = wait;
        _frame.UntilTick = untilTick;
        _frame.Channel = channel;
    }

    /// <summary>Returns true when the current latent wait is over. Override to add waits (MoveTo, real anims).</summary>
    protected virtual bool PollLatentWait(in LatentFrame frame)
    {
        switch (frame.Wait)
        {
            case LatentWait.None:
                return true;
            case LatentWait.Sleep:
                return XLevel.TickCount >= frame.UntilTick;
            case LatentWait.WaitForLanding:
                return Physics != EPhysics.PHYS_Falling;
            case LatentWait.FinishInterpolation:
                return !bInterpolating;
            case LatentWait.FinishAnim:
                return IsAnimFinished(frame.Channel);
            default:
                return true;
        }
    }

    /// <summary>Animation hook for FinishAnim; true until an animation system overrides it.</summary>
    protected virtual bool IsAnimFinished(int Channel) => true;

    /// <summary>
    /// Runs the latent state code for this tick (UE2 UObject::ProcessState): poll the current wait, then run code
    /// segments until something waits, the code stops or the actor is destroyed.
    /// </summary>
    [UcNative("UObject::ProcessState")]
    [Provenance(ProvenanceKind.Estimated, "UE2 ProcessState; a state change made by state code runs the new code in the same tick, to be confirmed")]
    protected internal void ProcessState(float DeltaSeconds)
    {
        if (_frame.Pc == LatentFrame.NoCode || bDeleteMe)
        {
            return;
        }

        if (_frame.Wait != LatentWait.None)
        {
            if (!PollLatentWait(in _frame))
            {
                return;
            }

            _frame.Wait = LatentWait.None;
            _frame.UntilTick = 0;
            _frame.Channel = 0;
        }

        int steps = 0;
        while (_frame.Wait == LatentWait.None && _frame.Pc != LatentFrame.NoCode && !bDeleteMe)
        {
            ActorState? code = _codeState;
            if (code is null)
            {
                _frame.Pc = LatentFrame.NoCode;
                break;
            }

            if (++steps > MaxLatentStepsPerTick)
            {
                RunawayLatentLoops++;
                break;
            }

            code.Code(this, ref _frame);
        }
    }
}
