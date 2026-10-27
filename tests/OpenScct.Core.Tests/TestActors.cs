using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Core.Tests;

/// <summary>Shared log so tests can check the order of events across actors.</summary>
public sealed class EventLog
{
    public readonly List<string> Entries = new();

    public void Add(string entry) => Entries.Add(entry);

    public override string ToString() => string.Join(", ", Entries);
}

/// <summary>Plain actor that records its events.</summary>
public class LoggingActor : Actor
{
    public EventLog? Log;
    public string Label = "";
    public int TickCalls;
    public int TimerCalls;
    public int TriggerCalls;
    public int UnTriggerCalls;
    public Actor? LastTriggerOther;
    public Pawn? LastTriggerInstigator;

    /// <summary>Optional work done inside Tick (spawning, destroying...).</summary>
    public System.Action<LoggingActor>? OnTick;

    private void Write(string what) => Log?.Add(Label.Length > 0 ? Label + "." + what : what);

    protected internal override void PreBeginPlay() => Write("PreBeginPlay");

    protected internal override void BeginPlay() => Write("BeginPlay");

    protected internal override void PostBeginPlay() => Write("PostBeginPlay");

    protected internal override void SetInitialState()
    {
        Write("SetInitialState");
        base.SetInitialState();
    }

    protected internal override void PostNetBeginPlay() => Write("PostNetBeginPlay");

    protected internal override void Tick(float DeltaTime)
    {
        TickCalls++;
        Write("Tick");
        OnTick?.Invoke(this);
    }

    protected internal override void Timer()
    {
        TimerCalls++;
        Write("Timer");
    }

    protected internal override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        TriggerCalls++;
        LastTriggerOther = Other;
        LastTriggerInstigator = EventInstigator;
        Write("Trigger");
    }

    protected internal override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        UnTriggerCalls++;
        Write("UnTrigger");
    }

    protected internal override void Destroyed() => Write("Destroyed");

    protected internal override void BaseChange() => Write("BaseChange");

    protected internal override void Touch(Actor Other) => Write("Touch");

    protected internal override void UnTouch(Actor Other) => Write("UnTouch");

    protected internal override void GainedChild(Actor Other) => Write("GainedChild");

    protected internal override void LostChild(Actor Other) => Write("LostChild");
}

public sealed class StaticActor : LoggingActor
{
    public StaticActor()
    {
        bStatic = true;
    }
}

public sealed class NoDeleteActor : LoggingActor
{
    public NoDeleteActor()
    {
        bNoDelete = true;
    }
}

public sealed class TestPawn : Pawn
{
}

public sealed class TestController : Controller
{
}

/// <summary>
/// The worked example of a state with latent code. UnrealScript equivalent:
/// <code>
/// auto state Idle
/// {
///     event BeginState() { Log("Idle.BeginState"); }
///     event EndState()   { Log("Idle.EndState"); }
/// Begin:
///     Counter++;
///     Sleep(0.5);
///     Counter += 10;
///     Goto('Begin');
/// }
///
/// state Busy extends Idle      // no code of its own: GotoState('Busy') runs Idle's Begin code
/// {
///     ignores Bump;
///     event Touch(Actor Other) { Log("Busy.Touch"); }
///     event BeginState() { Log("Busy.BeginState"); }
/// }
///
/// state Waiting
/// {
/// Begin:
///     WaitForLanding();
///     Log("Landed");
/// Loop:
///     Sleep(0.1);
///     LoopCount++;
///     if (LoopCount &lt; 3) Goto('Loop');
///     GotoState('Idle');
/// }
/// </code>
/// </summary>
public class SleeperActor : Actor
{
    public readonly List<string> Log = new();
    public int Counter;
    public int LoopCount;
    public int GlobalTouches;
    public int GlobalBumps;

    /// <summary>When set, EndState of Idle jumps to this state (to test preemption).</summary>
    public Name RedirectOnIdleEnd;

    public static new readonly StateTable ClassStates = new(Actor.ClassStates, Idle.Instance, Busy.Instance, Waiting.Instance);

    public override StateTable GetStateTable() => ClassStates;

    protected internal override void Touch(Actor Other) => GlobalTouches++;

    protected internal override void Bump(Actor Other) => GlobalBumps++;

    public class Idle : ActorState
    {
        public static readonly Idle Instance = new(nameof(SleeperActor), nameof(Idle), null, true, ("Begin", 1));

        protected Idle(string className, string stateName, ActorState? super, bool isAuto,
            params System.ReadOnlySpan<(string Name, int Pc)> labels)
            : base(className, stateName, super, isAuto, labels)
        {
        }

        public override void BeginState(Actor self) => ((SleeperActor)self).Log.Add("Idle.BeginState");

        public override void EndState(Actor self)
        {
            var me = (SleeperActor)self;
            me.Log.Add("Idle.EndState");
            if (!me.RedirectOnIdleEnd.IsNone)
            {
                Name target = me.RedirectOnIdleEnd;
                me.RedirectOnIdleEnd = Name.None;
                me.GotoState(target);
            }
        }

        public override void Code(Actor self, ref LatentFrame frame) => ((SleeperActor)self).Idle_Code(ref frame);
    }

    private void Idle_Code(ref LatentFrame f)
    {
        switch (f.Pc)
        {
            case 1: // Begin:
                Counter++;
                f.Pc = 2;
                Sleep(0.5f);
                return;
            case 2:
                Counter += 10;
                GotoLabel(NAME_Begin);
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }

    public class Busy : Idle
    {
        public static new readonly Busy Instance = new(nameof(SleeperActor), nameof(Busy), Idle.Instance, false);

        protected Busy(string className, string stateName, ActorState? super, bool isAuto,
            params System.ReadOnlySpan<(string Name, int Pc)> labels)
            : base(className, stateName, super, isAuto, labels)
        {
        }

        public override void BeginState(Actor self) => ((SleeperActor)self).Log.Add("Busy.BeginState");

        public override void Touch(Actor self, Actor Other) => ((SleeperActor)self).Log.Add("Busy.Touch");

        // ignores Bump;
        public override void Bump(Actor self, Actor Other)
        {
        }
    }

    public class Waiting : ActorState
    {
        public static readonly Waiting Instance = new(nameof(SleeperActor), nameof(Waiting), null, false, ("Begin", 1), ("Loop", 3));

        protected Waiting(string className, string stateName, ActorState? super, bool isAuto,
            params System.ReadOnlySpan<(string Name, int Pc)> labels)
            : base(className, stateName, super, isAuto, labels)
        {
        }

        public override void Code(Actor self, ref LatentFrame frame) => ((SleeperActor)self).Waiting_Code(ref frame);
    }

    private static readonly Name NAME_Loop = new("Loop");
    private static readonly Name NAME_Idle = new("Idle");

    private void Waiting_Code(ref LatentFrame f)
    {
        switch (f.Pc)
        {
            case 1: // Begin:
                f.Pc = 2;
                WaitForLanding();
                return;
            case 2:
                Log.Add("Landed");
                f.Pc = 3;
                return;
            case 3: // Loop:
                f.Pc = 4;
                Sleep(0.1f);
                return;
            case 4:
                LoopCount++;
                if (LoopCount < 3)
                {
                    GotoLabel(NAME_Loop);
                    return;
                }

                GotoState(NAME_Idle);
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }

    /// <summary>Exposes the protected latent helpers to tests.</summary>
    public void SleepFromTest(float seconds) => Sleep(seconds);
}

/// <summary>A subclass redefining an inherited state (UnrealScript: state Idle { event Timer() ... } in a child class).</summary>
public class SubSleeper : SleeperActor
{
    public int StateTimers;

    public static new readonly StateTable ClassStates = new(SleeperActor.ClassStates, Idle.Instance);

    public override StateTable GetStateTable() => ClassStates;

    public new class Idle : SleeperActor.Idle
    {
        public static new readonly Idle Instance = new(nameof(SubSleeper), nameof(Idle), SleeperActor.Idle.Instance, true);

        protected Idle(string className, string stateName, ActorState? super, bool isAuto,
            params System.ReadOnlySpan<(string Name, int Pc)> labels)
            : base(className, stateName, super, isAuto, labels)
        {
        }

        public override void Timer(Actor self) => ((SubSleeper)self).StateTimers++;
    }
}

/// <summary>Actor whose latent code never yields (to test the runaway guard).</summary>
public sealed class RunawayActor : Actor
{
    public int Steps;

    public static new readonly StateTable ClassStates = new(Actor.ClassStates, Spin.Instance);

    public override StateTable GetStateTable() => ClassStates;

    public sealed class Spin : ActorState
    {
        public static readonly Spin Instance = new();

        private Spin()
            : base(nameof(RunawayActor), nameof(Spin), null, true, ("Begin", 1))
        {
        }

        public override void Code(Actor self, ref LatentFrame frame)
        {
            ((RunawayActor)self).Steps++;
            frame.Pc = 1; // Goto('Begin') with no latent call: loops forever.
        }
    }
}

/// <summary>
/// Function declared by an Actor subclass and overridden in states: the nested interface pattern.
/// UnrealScript: function int Ping(int Value) { return Value; } state Alert { function int Ping(int Value) { return Value * 10; } }
/// </summary>
public class GuardActor : Actor
{
    public interface IPing
    {
        int Ping(GuardActor self, int Value);
    }

    public static new readonly StateTable ClassStates = new(Actor.ClassStates, Alert.Instance, Calm.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Global (class-level) version.</summary>
    protected virtual int Ping(int Value) => Value;

    /// <summary>Dispatcher: state version first, then the global one.</summary>
    public int eventPing(int Value) => StateNode is IPing s ? s.Ping(this, Value) : Ping(Value);

    public class Alert : ActorState, IPing
    {
        public static readonly Alert Instance = new(nameof(GuardActor), nameof(Alert), null, false);

        protected Alert(string className, string stateName, ActorState? super, bool isAuto,
            params System.ReadOnlySpan<(string Name, int Pc)> labels)
            : base(className, stateName, super, isAuto, labels)
        {
        }

        public virtual int Ping(GuardActor self, int Value) => Value * 10;
    }

    /// <summary>A state that does not override Ping, so the global version runs.</summary>
    public sealed class Calm : ActorState
    {
        public static readonly Calm Instance = new();

        private Calm()
            : base(nameof(GuardActor), nameof(Calm))
        {
        }
    }
}

public sealed class EliteGuard : GuardActor
{
    public static new readonly StateTable ClassStates = new(GuardActor.ClassStates, Alert.Instance);

    public override StateTable GetStateTable() => ClassStates;

    protected override int Ping(int Value) => Value + 1;

    public new sealed class Alert : GuardActor.Alert
    {
        public static new readonly Alert Instance = new();

        private Alert()
            : base(nameof(EliteGuard), nameof(Alert), GuardActor.Alert.Instance, false)
        {
        }

        // Super.Ping(Value) + 5: the parent class's state version.
        public override int Ping(GuardActor self, int Value) => base.Ping(self, Value) + 5;
    }
}
