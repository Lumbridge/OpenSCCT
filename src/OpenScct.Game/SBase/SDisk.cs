using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The Disk Hunt disk: put in play by its mission, picked up and kept by spies, dropped when the carrier dies, sent
// home by mercs or by its timer. Written from spec/objectives/disk-hunt.md.
public partial class SDisk
{
    [Uc("SBase.SDisk.PreBeginPlay", Sha1 = "057abcab77d799acded157e2806b0d9b76a36765")]
    [PortOf("uc:SBase.SDisk:PreBeginPlay", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected override void PreBeginPlay() => SetTimer(0f, false);

    /// <summary>Remembers home, runs the pickable-object setup (the "Pick Up" entry), then turns collision off.</summary>
    [Uc("SBase.SDisk.PostBeginPlay", Sha1 = "2513ed584b4ed84a4b9c698e9e992acba99f4f2f")]
    [PortOf("uc:SBase.SDisk:PostBeginPlay", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected override void PostBeginPlay()
    {
        InitialLocation = Location;
        InitialRotation = Rotation;
        base.PostBeginPlay();
        SetCollision(false, false, false);
    }

    /// <summary>
    /// A spy takes the disk (rule 8): it leaves play and counts as taken by him, which lets the mission test whether
    /// the spies now hold enough disks.
    /// </summary>
    [Uc("SBase.SDisk.NotifyPickup", Sha1 = "95de6328d625414fad471724a77bdc2df5e071e8")]
    [PortOf("uc:SBase.SDisk:NotifyPickup", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected internal override bool NotifyPickup(Controller Instigator)
    {
        base.NotifyPickup(Instigator);
        bActive = false;
        ControllerWhoTakesIt = Instigator as PlayerController;
        eventTakeObjective();
        SetTimer(0f, false);
        return true;
    }

    /// <summary>A merc walking over a dropped disk puts it back home (placement only: no reset, no message).</summary>
    [Uc("SBase.SDisk.Touch", Sha1 = "b0e5e7afbf239b74cdb5d5be07e9f6eec9b2cb7f")]
    [PortOf("uc:SBase.SDisk:Touch", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected override void Touch(Actor Other)
    {
        if (!bActive || Other is not SPAWNDEFENSE)
        {
            return;
        }

        if (Location.X == InitialLocation.X && Location.Y == InitialLocation.Y)
        {
            return;
        }

        SetCollision(false, false, false);
        bCollideWorld = false;
        SetLocation(InitialLocation);
        ObjectiveNatives.SetRotation(this, InitialRotation);
        DesiredRotation = InitialRotation;
        SetCollision(true, false, false);
        bCollideWorld = true;
    }

    /// <summary>The carrier lets the disk go (rule 12): it flies, is back in play and visible, and counts again.</summary>
    [Uc("SBase.SDisk.Throw", Sha1 = "5dac2c08f946d7ea3cdf141e294ade9f1975a847")]
    [PortOf("uc:SBase.SDisk:Throw", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected internal override void Throw(Controller? Thrower, FVector ThrowVelocity)
    {
        base.Throw(Thrower, ThrowVelocity);
        bActive = true;
        bHidden = false;
        if (Objective is SMission mission)
        {
            mission.TotalObjectivesLeft++;
        }
    }

    /// <summary>At rest after a drop: the pickable-object rest, then the timer that sends it home.</summary>
    [Uc("SBase.SDisk.StoppedMoving", Sha1 = "b6c9293c9164cd5625607011ce9b4f61ba1c7511")]
    [PortOf("uc:SBase.SDisk:StoppedMoving", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected internal override void StoppedMoving()
    {
        base.StoppedMoving();
        SetTimer(ResetTime, false);
    }

    [Uc("SBase.SDisk.Timer", Sha1 = "19e33bddd3d57172161c8d5f551a14974c3c5ead")]
    [PortOf("uc:SBase.SDisk:Timer", Spec = "spec/objectives/disk-hunt.md#rules")]
    protected override void Timer() => eventReset();

    /// <summary>
    /// Puts the disk in play at home (rule 5): the trigger reset, the "Pick Up" entry usable again and taken out of
    /// the menus of players standing on it, the disk back home and touchable, the entry re-placed on it.
    /// </summary>
    [Uc("SBase.SDisk.Reset", Sha1 = "f6dbda9094c21eda1e096d9b5d363492859ffc61")]
    [PortOf("uc:SBase.SDisk:Reset", Spec = "spec/objectives/disk-hunt.md#rules")]
    public override void Reset()
    {
        base.Reset();
        if (PickupInteraction is { } pickup)
        {
            bPickable = true;
            pickup.bCanPickup = true;
            var touching = new List<Actor>(pickup.Touching);
            foreach (Actor a in touching)
            {
                if (a is Pawn { Controller: SPlayerController { IManager: { } manager } })
                {
                    manager.RemoveInteractionObj(pickup);
                }
            }
        }

        SetCollision(false, false, false);
        bCollideWorld = false;
        SetLocation(InitialLocation);
        ObjectiveNatives.SetRotation(this, InitialRotation);
        DesiredRotation = InitialRotation;
        bCollideWorld = true;
        bHidden = false;
        SetCollision(true, false, false);
        if (PickupInteraction is { } entry)
        {
            entry.SetBase(null);
            entry.SetLocation(Location);
            ObjectiveNatives.SetRotation(entry, Rotation);
            entry.SetBase(this);
        }
    }

    /// <summary>Out of play (rule 6): hidden, not takeable from the menu, then the objective trigger version.</summary>
    [UcNative("ASDisk::execDesactivate", Exe = 0x10ABFE50, Editor = 0x111CE980)]
    [PortOf("nat:SBase.SDisk.Desactivate", Spec = "spec/objectives/disk-hunt.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Shared thunk 0x10AE9C60, vtable slot 0x138 of ASDisk: bHidden, bPickable and bCanPickup, then ASObjectiveTrigger::Desactivate")]
    public override void Desactivate()
    {
        bHidden = true;
        if (PickupInteraction is { } pickup)
        {
            bPickable = false;
            pickup.bCanPickup = false;
        }

        base.Desactivate();
    }

    /// <summary>
    /// Client visibility (rule 16): a disk nobody carries that is out of play is hidden here, a carried or active
    /// one is shown, whatever the replicated flag says.
    /// </summary>
    [UcNative("ASDisk::Tick", Exe = 0x10ABFDC0, Editor = 0x111CE7F0)]
    [PortOf("eng:ASDisk::Tick", Spec = "spec/objectives/disk-hunt.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASDisk::Tick: Role below authority, no Base and not bActive hides, else shows")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || Role == ENetRole.ROLE_Authority)
        {
            return;
        }

        if (Base is null)
        {
            if (!bHidden && !bActive)
            {
                bHidden = true;
                return;
            }

            if (!bActive)
            {
                return;
            }
        }

        if (bHidden)
        {
            bHidden = false;
        }
    }
}
