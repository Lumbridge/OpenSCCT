using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The receiving side of the combat and equipment calls (Combat/ICombatNet.cs): the network layer hands each arrived
// CombatCall to the receiving machine's copy of the sending or owning controller. Owner-to-server calls that run on
// another actor are accepted only for actors this player owns (as the original's connection ownership test).
public partial class SPlayerController
{
    /// <summary>True when <paramref name="A"/> belongs to this player: itself, its pawn, or an actor owned by them.</summary>
    public bool OwnsActor(Actor? A)
    {
        for (Actor? a = A; a is not null; a = a.Owner)
        {
            if (ReferenceEquals(a, this) || ReferenceEquals(a, Pawn) || (a is Pawn p && ReferenceEquals(p.Controller, this)))
            {
                return true;
            }
        }

        return A is not null && A.Instigator is { } inst && ReferenceEquals(inst.Controller, this);
    }

    /// <summary>
    /// Runs an arrived combat call. <paramref name="FromOwner"/> is true for an owner-to-server call (this is the
    /// server's copy of the sender) and false for a server-to-owner call (this is the owner's copy).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Replicated function dispatch of the combat port (the netcode spec owns the transport)")]
    public void ReceiveCombatCall(in CombatCall Call, bool FromOwner)
    {
        if (FromOwner)
        {
            ReceiveFromOwner(Call);
        }
        else
        {
            ReceiveFromServer(Call);
        }
    }

    private void ReceiveFromOwner(in CombatCall c)
    {
        Actor? target = c.Target;
        if (target is not null && !OwnsActor(target))
        {
            return;
        }

        switch (c.Function.ToString())
        {
            case "PawnTakeDamage":
                PawnTakeDamage(c.Int1, c.Actor1 as Pawn, c.Vector1, c.Vector2, c.DamageType, c.Int2, c.Int3);
                break;
            case "DamagePawn_C2S":
                DamagePawn_C2S(c.Target as Pawn, c.Int1, c.Actor1 as Pawn, c.Vector1, c.Vector2, c.DamageType, c.Int2, c.Int3);
                break;
            case "ServerSetVelocityAndOrientationForBlast":
                ServerSetVelocityAndOrientationForBlast(c.Vector1);
                break;
            case "ServerSetVelocityForDyingPawn":
                ServerSetVelocityForDyingPawn(c.Vector1);
                break;
            case "AskServerToReanimatePawn":
                AskServerToReanimatePawn(c.Actor1 as SBasePawn);
                break;
            case "ReplicateMate" when target is SMateInteraction mateEntry:
                mateEntry.ReplicateMate(c.Actor1 as SBasePawn);
                break;
            case "AskServerToGrabPawn" when c.Actor1 is SBasePawn grabbed:
                AskServerToGrabPawn(grabbed);
                break;
            case "AskServerToGrabPawnFromLedge" when c.Actor1 is SBasePawn pulled:
                AskServerToGrabPawnFromLedge(pulled);
                break;
            case "AskServerToUnGrabPawn":
                AskServerToUnGrabPawn((GrabReleaseMethode_)c.Int1);
                break;
            case "AskServerToUnGrabPawnAndFreeGuy":
                AskServerToUnGrabPawnAndFreeGuy((GrabReleaseMethode_)c.Int1);
                break;
            case "AskServerToElbowAttack" when c.Actor1 is SBasePawn elbowed:
                AskServerToElbowAttack(elbowed, c.Int1, c.Actor2 as Pawn, c.Vector1, c.Vector2, c.DamageType);
                break;
            case "AskServerToTerminatePawn" when c.Actor1 is SBasePawn finished:
                AskServerToTerminatePawn(finished);
                break;
            case "TellServerForDropAttack" when c.Actor1 is SBasePawn dropped:
                TellServerForDropAttack(dropped);
                break;
            case "TellServerToFallAttack" when c.Actor1 is SBasePawn fallenOn:
                TellServerToFallAttack(fallenOn);
                break;
            case "GrabSendServerCounterValue" when target is SBasePawn pawn:
                pawn.GrabSendServerCounterValue(c.Int1, (PawnType)c.Int2);
                break;
            case "SetEMFToServer" when target is SBasePawn pawn:
                PawnVision.SetEMFToServer(pawn);
                break;
            case "UnSetEMFToServer" when target is SBasePawn pawn:
                PawnVision.UnSetEMFToServer(pawn);
                break;
            case "ServerSound" when target is SWeapon weapon:
                if (c.Bool1)
                    (weapon.Instigator ?? (Actor)weapon).PlayOwnedSound(new AssetRef(c.Name1.ToString()),
                        (ESoundSlot)c.Int1, null, false, c.Int2 == 0 ? (float?)null : c.Int2);
                else
                    weapon.ServerSound(new AssetRef(c.Name1.ToString()), (ESoundSlot)c.Int1, c.Int2);
                break;
            case "SelectCarriedWeapon" when ReferenceEquals(target, Pawn):
                ReceiveWeaponSelection(c.Int1, c.Int2);
                break;
            case "ReloadCarriedPrimary" when target is SWeaponDefense primary:
                primary.RequestCarriedReload();
                break;
            case "ReloadSidearm" when target is MercSidearm sidearm:
                sidearm.Reload();
                break;
            case "ServerSetAccurateRotation" when target is SWeaponDefense rifle:
                rifle.ServerSetAccurateRotation(c.Rotation.Yaw, c.Rotation.Pitch, c.Rotation.Roll);
                break;
            case "ServerFireWeapon" when target is SWeapon weapon:
                // The shot's number and whether its impacts were predicted (spec/net/lag-compensation.md).
                if (weapon.Instigator is SBasePawn { SidearmWeapon: not null } &&
                    (c.Int2 <= 0 || c.Int2 != weapon.ShotCounter + 1)) break;
                weapon.BeginClientShot(c.Int2, c.Bool1);
                weapon.ServerFireWeapon(c.Int1);
                weapon.EndClientShot();
                break;
            case "ServerSetROF" when target is SWeaponDefense rifle:
                rifle.ServerSetROF((SRateOfFireMode)c.Int1);
                break;
            case "ServerSwitchLaser" when target is SWeaponAttaque pistol:
                pistol.ServerSwitchLaser(c.Bool1);
                break;
            case "ServerFireWeaponWithLocation" when target is SWeaponAttaque pistol:
                pistol.BeginClientShot(c.Int1, c.Bool1);
                pistol.ServerFireWeaponWithLocation(c.Vector1, c.Rotation.Yaw, c.Rotation.Pitch, c.Rotation.Roll);
                pistol.EndClientShot();
                break;
            case "TorcheON" when target is SGadTorche torch:
                torch.TorcheON();
                break;
            case "TorcheOFF" when target is SGadTorche torch:
                torch.TorcheOFF();
                break;
            case "Malfunction" when target is SGadTorche torch:
                torch.Malfunction(c.Float1);
                break;
            case "TorcheON" when target is SGadTorcheLaser laser:
                laser.TorcheON();
                break;
            case "TorcheOFF" when target is SGadTorcheLaser laser:
                laser.TorcheOFF();
                break;
            case "ThrowSpyBullet" when target is SGadTorcheLaser laser:
                laser.ThrowSpyBullet(c.Actor1 as SBasePawn);
                break;
            case "FullReload" when target is SGadget gadget:
                gadget.FullReload();
                break;
            case "ServerDecCharge" when target is SGadget gadget:
                gadget.ServerDecCharge(SvmMath.Max(c.Int1, 1));
                break;
            case "ServerAddCharge" when target is SGadget gadget:
                gadget.ServerAddCharge(c.Int1);
                break;
            case "ActivateBombTarget" when target is SGadBomb bomb:
                bomb.ActivateBombTarget();
                break;
            case "ThrowGren" when target is SGadGrenLauncher launcher:
                launcher.ServerThrowGren(c.Int1, c.Vector1, c.Rotation, c.Bool1);
                break;
            case "UseBulletSet" when target is SGadTazerLauncher taser:
                taser.UseBulletSet();
                break;
            case "ServerSpawnInventory" when target is SBasePawn pawn:
                Loadouts.ServerSpawnInventory(pawn, new ClassRef(c.Name1.ToString()));
                break;
            case "ServerSpawnWeapon" when target is SBasePawn pawn:
                Loadouts.ServerSpawnWeapon(pawn, new ClassRef(c.Name1.ToString()));
                break;
            case "ServerChangeGear":
                ServerChangeGear();
                break;
            case "UpdateMinimapParams":
                // PlayerReplicationInfo.UpdateMinimapParams from the strategic map page (spec/hud/strategic-map.md).
                PlayerReplicationInfo?.UpdateMinimapParams();
                break;
            case "Throw" when target is SGamePlayObject thrown:
                // SGamePlayObject.Throw (reliable owner to server): the hand item released by ThrowObject.
                thrown.eventThrow(this, c.Vector1);
                break;
            case "ReplicateSpecialsChatOnServer":
                ReplicateSpecialsChatOnServer(c.Int1 != 0 ? (byte)1 : (byte)0);
                break;
            case "ServerSetVoiceChatTarget":
                ServerSetVoiceChatTarget(c.Actor1 as Pawn);
                break;
            case "ServerClearVoiceChatTarget":
                ServerClearVoiceChatTarget();
                break;
            case "ServerSetVoiceComWhenGrabbing":
                ServerSetVoiceComWhenGrabbing(c.Bool1);
                break;
            default:
                GadgetNetReceive.FromOwner(this, c);
                break;
        }
    }

    private void ReceiveFromServer(in CombatCall c)
    {
        switch (c.Function.ToString())
        {
            case "CarriedWeaponSelected" when SBPawn is { } selectedPawn:
                if (selectedPawn.ActiveWeaponSlot != c.Int1) SidearmEquipment.Select(selectedPawn, c.Int1, true);
                if (!c.Bool1) selectedPawn.WeaponSwitchUntil = selectedPawn.Level.TimeSeconds + c.Float1;
                break;
            case "WeaponAmmoState" when c.Target is SWeaponDefense checkedWeapon:
                if (c.Int3 == checkedWeapon.ShotCounter) { checkedWeapon.ClipAmmo = c.Int1; checkedWeapon.Ammo = c.Int2; }
                break;

            case "ClientDamage":
                ClientDamage(c.Vector1, c.DamageType, c.Vector2, c.Actor1 as Pawn);
                break;
            case "PlayAnimDmg":
                PlayAnimDmg(c.Int1, c.Vector1);
                break;
            case "PlayAnimDeath":
                PlayAnimDeath(c.Vector1, c.Int1);
                break;
            case "TellClientServerWaitForFatalBlast":
                TellClientServerWaitForFatalBlast();
                break;
            case "ClientStopBinoculars":
                ClientStopBinoculars();
                break;
            case "ClientStickyShock":
                ClientStickyShock(c.Int1, c.Bool1);
                break;
            case "Client_Jammed":
                Client_Jammed(c.Actor1 as Pawn);
                break;
            case "GoInSmoke":
                eventGoInSmoke(c.Float1, c.Vector1.X, c.Vector1.Y, c.Vector1.Z);
                break;
            case "GrabServerReleaseRequest":
                GrabServerReleaseRequest((GrabReleaseMethode_)c.Int1);
                break;
            case "GrabAborted":
                GrabAborted();
                break;
            case "PCChangeClientStateForMercGrabbedFromLedge" when c.Actor1 is SBasePawn ledgeSpy:
                PCChangeClientStateForMercGrabbedFromLedge(c.Vector2.X, c.Vector2.Y, ledgeSpy, c.Vector1, c.Int1);
                break;
            case "TellClientToStickyBlast":
                TellClientToStickyBlast(c.Actor1);
                break;
            case "ForceRotateTo":
                ForceRotateTo(c.Rotation, c.Int1, c.Int2);
                break;
            case "Client_Vibration_Charge_Obstacle":
                Client_Vibration_Charge_Obstacle();
                break;
            case "ServerSendAnEvent":
                ServerSendAnEvent((SentEventType_)c.Int1);
                break;
            case "MirrorStateForRepos":
                MirrorStateForRepos(c.Name1, c.Vector1, c.Int1);
                break;
            case "UpdateInventory" when c.Target is SBasePawn pawn && ReferenceEquals(pawn, Pawn):
                Loadouts.UpdateInventory(pawn, (EGameMode)c.Int1);
                break;
            case "ClientGearChangeResult":
                XLevel?.GetService<IGearChangeFeedback>()?.GearChangeAnswered((GearChangeResult)c.Int1, c.Float1);
                break;
            default:
                GadgetNetReceive.FromServer(this, c);
                break;
        }
    }
}
