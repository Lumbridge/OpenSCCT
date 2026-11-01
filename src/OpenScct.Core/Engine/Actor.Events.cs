using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// UE2 events. Each event has two parts:
//  - a protected virtual method with the UnrealScript name: the class-level (global) version. Subclasses override
//    it; UnrealScript Super.X() is base.X() and Global.X() is X().
//  - a public eventX dispatcher (the name UE2 natives use), which goes through the current state first. Natives and
//    other actors must call eventX so state overrides and ignores apply.
public abstract partial class Actor
{
    // ---- class-level (global) versions ---------------------------------------------------------------------

    protected internal virtual void BeginState()
    {
    }

    protected internal virtual void EndState()
    {
    }

    protected internal virtual void PreBeginPlay()
    {
    }

    protected internal virtual void BeginPlay()
    {
    }

    protected internal virtual void PostBeginPlay()
    {
    }

    /// <summary>Enters InitialState, or the auto state when InitialState is None (Actor.SetInitialState).</summary>
    [Uc("Actor.SetInitialState")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor.SetInitialState; to be checked against the decompiled Engine.u")]
    protected internal virtual void SetInitialState()
    {
        bScriptInitialized = true;
        if (!InitialState.IsNone)
        {
            GotoState(InitialState);
        }
        else
        {
            GotoState(NAME_Auto);
        }
    }

    protected internal virtual void PostNetBeginPlay()
    {
    }

    protected internal virtual void Tick(float DeltaTime)
    {
    }

    protected internal virtual void Timer()
    {
    }

    protected internal virtual void Touch(Actor Other)
    {
    }

    protected internal virtual void UnTouch(Actor Other)
    {
    }

    protected internal virtual void Bump(Actor Other)
    {
    }

    protected internal virtual void HitWall(FVector HitNormal, Actor? Wall)
    {
    }

    protected internal virtual void Landed(FVector HitNormal)
    {
    }

    protected internal virtual void Falling()
    {
    }

    protected internal virtual void Trigger(Actor? Other, Pawn? EventInstigator)
    {
    }

    protected internal virtual void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
    }

    protected internal virtual void Destroyed()
    {
    }

    protected internal virtual void BaseChange()
    {
    }

    protected internal virtual void Attach(Actor Other)
    {
    }

    protected internal virtual void Detach(Actor Other)
    {
    }

    protected internal virtual void GainedChild(Actor Other)
    {
    }

    protected internal virtual void LostChild(Actor Other)
    {
    }

    protected internal virtual void AnimEnd(int Channel)
    {
    }

    // ---- dispatchers -----------------------------------------------------------------------------------------

    public void eventBeginState() => _state.BeginState(this);

    public void eventEndState() => _state.EndState(this);

    public void eventPreBeginPlay() => _state.PreBeginPlay(this);

    public void eventBeginPlay() => _state.BeginPlay(this);

    public void eventPostBeginPlay() => _state.PostBeginPlay(this);

    public void eventSetInitialState() => _state.SetInitialState(this);

    public void eventPostNetBeginPlay() => _state.PostNetBeginPlay(this);

    public void eventTick(float DeltaTime) => _state.Tick(this, DeltaTime);

    public void eventTimer() => _state.Timer(this);

    // Pawn/controller traversal remains on input time. Devices touched or triggered by that movement
    // schedule work on world time (for example Aquarius's four-second door-release relay).
    private OpenScct.Core.Level.ReplayClock DeviceEventTime() =>
        this is Pawn or Controller ? default : WorldEventTime();

    /// <summary>Run a world-owned effect without inheriting an enclosing movement command's older clock.</summary>
    protected OpenScct.Core.Level.ReplayClock WorldEventTime() =>
        XLevel is null ? default : XLevel.WorldEventTime();

    public void eventTouch(Actor Other)
    {
        using var clock = DeviceEventTime();
        _state.Touch(this, Other);
    }

    public void eventUnTouch(Actor Other)
    {
        using var clock = DeviceEventTime();
        _state.UnTouch(this, Other);
    }

    public void eventBump(Actor Other)
    {
        using var clock = DeviceEventTime();
        _state.Bump(this, Other);
    }

    public void eventHitWall(FVector HitNormal, Actor? Wall) => _state.HitWall(this, HitNormal, Wall);

    public void eventLanded(FVector HitNormal) => _state.Landed(this, HitNormal);

    public void eventFalling() => _state.Falling(this);

    public void eventTrigger(Actor? Other, Pawn? EventInstigator)
    {
        using var clock = DeviceEventTime();
        _state.Trigger(this, Other, EventInstigator);
    }

    public void eventUnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        using var clock = DeviceEventTime();
        _state.UnTrigger(this, Other, EventInstigator);
    }

    public void eventDestroyed() => _state.Destroyed(this);

    public void eventBaseChange() => _state.BaseChange(this);

    public void eventAttach(Actor Other)
    {
        using var clock = DeviceEventTime();
        _state.Attach(this, Other);
    }

    public void eventDetach(Actor Other)
    {
        using var clock = DeviceEventTime();
        _state.Detach(this, Other);
    }

    public void eventGainedChild(Actor Other) => _state.GainedChild(this, Other);

    public void eventLostChild(Actor Other) => _state.LostChild(this, Other);

    public void eventAnimEnd(int Channel) => _state.AnimEnd(this, Channel);
}
