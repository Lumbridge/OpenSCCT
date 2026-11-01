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
    /// The authority's native damage tick: each period, touching pawns take damage through their controller,
    /// which owns player health, state reactions and kill credit. Other matching actors use TakeDamage.
    /// </summary>
    [UcNative("ASDamageVolume::Tick")]
    [PortOf("eng:ASDamageVolume::Tick", Spec = "spec/native/natives2-damage.md#6-sdamagevolume-tick-native-replaces-the-actor-tick")]
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 6: authority, strict period, sensitivity and controller damage; no parent tick")]
    [Provenance(ProvenanceKind.Estimated, "Uses the deterministic world clock in place of the original appSeconds clock")]
    protected override void NativeTick(float DeltaSeconds)
    {
        float now = XLevel.WorldTimeSeconds;
        if (bDeleteMe || Role != ENetRole.ROLE_Authority || !bActive || now - LastDamageTime <= Period)
        {
            return;
        }

        if (LastDamageTime != 0f)
        {
            LastTouched = null;
        }

        LastDamageTime = now;
        Actor[] touching = Touching.ToArray();
        foreach (Actor victim in touching)
        {
            if (victim.bDeleteMe || (!DamagedClass.IsNone && !ClassIsChildOf(victim.GetUcClass(), DamagedClass)))
            {
                continue;
            }

            FVector momentum = victim.Location - Location;
            if (victim is Pawn pawn)
            {
                if (pawn.Controller is SPlayerController controller && controller.bIsDamageVolumeSensitive())
                {
                    controller.PawnTakeDamage(HitPoints, null, victim.Location, momentum, DamageType);
                }
            }
            else
            {
                victim.eventTakeDamage(HitPoints, null, victim.Location, momentum, DamageType);
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
