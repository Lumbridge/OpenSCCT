using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SDamageVolume: a volume that hurts pawns of DamagedClass inside it every Period while active, switched by
/// events and crediting the player who caused it (spec/devices/damage-volume.md).
/// </summary>
public partial class SDamageVolume
{
    /// <summary>A new toucher restarts the damage period, so it is hurt at once.</summary>
    [Uc("SBase.SDamageVolume.Touch", Sha1 = "6eb8ed3eadb86b3c62727b81d0eedcbfe5359443")]
    [PortOf("uc:SBase.SDamageVolume:Touch", Spec = "spec/devices/damage-volume.md#rules")]
    protected override void Touch(Actor Other)
    {
        if (!ReferenceEquals(LastTouched, Other))
        {
            LastDamageTime = 0f;
        }

        LastTouched = Other;
    }

    /// <summary>
    /// Switches the volume on (or toggles it). When on, the credited controller is the instigator's, falling back
    /// to the sender's instigator and then to SGameInfo.GPODamageInstigator; when none is found the previous one
    /// stays. When off, nobody is credited.
    /// </summary>
    [Uc("SBase.SDamageVolume.Trigger", Sha1 = "db5589ef0e1d346d896817c44cde0e3eacd2d644")]
    [PortOf("uc:SBase.SDamageVolume:Trigger", Spec = "spec/devices/damage-volume.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (bToggle)
        {
            bActive = !bActive;
        }
        else if (!bActive)
        {
            bActive = true;
        }

        if (!bActive)
        {
            TriggerController = null;
            return;
        }

        if (EventInstigator is null && Other is not null)
        {
            EventInstigator = Other.Instigator;
        }

        if (EventInstigator is null && Level.Game is SGameInfo game)
        {
            EventInstigator = game.GPODamageInstigator;
        }

        if (EventInstigator is not null)
        {
            TriggerController = EventInstigator.Controller;
        }
    }

    [Uc("SBase.SDamageVolume.UnTrigger", Sha1 = "250b9eb630e2cdf297d0e563cf0ffd4b4b1f6054")]
    [PortOf("uc:SBase.SDamageVolume:UnTrigger", Spec = "spec/devices/damage-volume.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        bActive = false;
        TriggerController = null;
    }

    /// <summary>
    /// The native damage tick: while active, once Period has passed since the last application, every touching
    /// actor of DamagedClass takes HitPoints of DamageType with no instigator (the damage code looks the volume up
    /// for credit).
    /// </summary>
    [UcNative("ASDamageVolume::Tick")]
    [PortOf("def:SBase.SDamageVolume", Spec = "spec/devices/damage-volume.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Period test and every-toucher rule are damage-volume.md open question 1")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || !bActive || Touching.Count == 0 || Level.TimeSeconds - LastDamageTime < Period)
        {
            return;
        }

        LastDamageTime = Level.TimeSeconds;
        Actor[] touching = Touching.ToArray();
        foreach (Actor victim in touching)
        {
            if (!victim.bDeleteMe && ClassIsChildOf(victim.GetUcClass(), DamagedClass))
            {
                victim.eventTakeDamage(HitPoints, null, victim.Location, FVector.Zero, DamageType);
            }
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        LastTouched = Live(LastTouched);
        TriggerController = Live(TriggerController);
    }
}
