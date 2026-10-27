using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController permission predicates: other systems ask the current state what the player may do. Every
// global version returns false; the walking, ledge and traversal states override them. Written from
// spec/movement/walking.md (permission predicates).
public partial class SPlayerController
{
    public interface ICanInteract
    {
        bool CanInteract(SPlayerController self);
    }

    public interface ICanIUseBinocular
    {
        bool CanIUseBinocular(SPlayerController self);
    }

    public interface ICanUncrouchAndUndraw
    {
        bool CanUncrouchAndUndraw(SPlayerController self);
    }

    public interface ICanSwitchGoggleManually
    {
        bool CanSwitchGoggleManually(SPlayerController self);
    }

    public interface ICanBeDropAttacked
    {
        bool CanBeDropAttacked(SPlayerController self);
    }

    public interface ICanBeGrabbed
    {
        bool CanBeGrabbed(SPlayerController self);
    }

    public interface ICanBeElbowShocked
    {
        bool CanBeElbowShocked(SPlayerController self);
    }

    public interface ICanElbowShock
    {
        bool CanElbowShock(SPlayerController self);
    }

    public interface ICanUseCam
    {
        bool CanUseCam(SPlayerController self);
    }

    public interface ICanActivateLaser
    {
        bool CanActivateLaser(SPlayerController self);
    }

    public interface ICanShortLadderHelp
    {
        bool CanShortLadderHelp(SPlayerController self);
    }

    public interface ICanHoistClimbHelp
    {
        bool CanHoistClimbHelp(SPlayerController self);
    }

    public interface ICanBeBlasted
    {
        bool CanBeBlasted(SPlayerController self, FVector Momentum);
    }

    public bool eventCanInteract() => StateNode is ICanInteract s ? s.CanInteract(this) : CanInteract();

    public bool eventCanIUseBinocular() => StateNode is ICanIUseBinocular s ? s.CanIUseBinocular(this) : CanIUseBinocular();

    public bool eventCanUncrouchAndUndraw() =>
        StateNode is ICanUncrouchAndUndraw s ? s.CanUncrouchAndUndraw(this) : CanUncrouchAndUndraw();

    public bool eventCanSwitchGoggleManually() =>
        StateNode is ICanSwitchGoggleManually s ? s.CanSwitchGoggleManually(this) : CanSwitchGoggleManually();

    public bool eventCanBeDropAttacked() => StateNode is ICanBeDropAttacked s ? s.CanBeDropAttacked(this) : CanBeDropAttacked();

    public bool eventCanBeGrabbed() => StateNode is ICanBeGrabbed s ? s.CanBeGrabbed(this) : CanBeGrabbed();

    public bool eventCanBeElbowShocked() => StateNode is ICanBeElbowShocked s ? s.CanBeElbowShocked(this) : CanBeElbowShocked();

    public bool eventCanElbowShock() => StateNode is ICanElbowShock s ? s.CanElbowShock(this) : CanElbowShock();

    public bool eventCanUseCam() => StateNode is ICanUseCam s ? s.CanUseCam(this) : CanUseCam();

    public bool eventCanActivateLaser() => StateNode is ICanActivateLaser s ? s.CanActivateLaser(this) : CanActivateLaser();

    public bool eventCanShortLadderHelp() => StateNode is ICanShortLadderHelp s ? s.CanShortLadderHelp(this) : CanShortLadderHelp();

    public bool eventCanHoistClimbHelp() => StateNode is ICanHoistClimbHelp s ? s.CanHoistClimbHelp(this) : CanHoistClimbHelp();

    public bool eventCanBeBlasted(FVector Momentum) =>
        StateNode is ICanBeBlasted s ? s.CanBeBlasted(this, Momentum) : CanBeBlasted(Momentum);

    [Uc("SBase.SPlayerController.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanInteract() => false;

    [Uc("SBase.SPlayerController.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanIUseBinocular", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanIUseBinocular() => false;

    [Uc("SBase.SPlayerController.CanUncrouchAndUndraw", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanUncrouchAndUndraw", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanUncrouchAndUndraw() => false;

    [Uc("SBase.SPlayerController.CanSwitchGoggleManually", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanSwitchGoggleManually", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanSwitchGoggleManually() => false;

    [Uc("SBase.SPlayerController.CanBeDropAttacked", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanBeDropAttacked", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanBeDropAttacked() => false;

    [Uc("SBase.SPlayerController.CanBeGrabbed", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanBeGrabbed", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanBeGrabbed() => false;

    [Uc("SBase.SPlayerController.CanBeElbowShocked", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanBeElbowShocked", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanBeElbowShocked() => false;

    [Uc("SBase.SPlayerController.CanElbowShock", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanElbowShock", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanElbowShock() => false;

    [Uc("SBase.SPlayerController.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanUseCam", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanUseCam() => false;

    [Uc("SBase.SPlayerController.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanActivateLaser", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanActivateLaser() => false;

    [Uc("SBase.SPlayerController.CanShortLadderHelp", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanShortLadderHelp", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanShortLadderHelp() => false;

    [Uc("SBase.SPlayerController.CanHoistClimbHelp", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanHoistClimbHelp", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanHoistClimbHelp() => false;

    [Uc("SBase.SPlayerController.CanBeBlasted", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanBeBlasted", Spec = "spec/movement/walking.md#permission-predicates")]
    public virtual bool CanBeBlasted(FVector Momentum) => false;
}
