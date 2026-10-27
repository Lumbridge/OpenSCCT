using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The combat and equipment answers to the calls other areas make through their seams: the game flow's inventory,
/// vision, poison and death calls, the devices' gadget lookups and refills, and the movement port's weapon, laser,
/// Use-key and quick-gadget calls. Each decorator handles its calls and passes the rest on.
/// </summary>
public static class CombatEquipment
{
    /// <summary>
    /// Installs the decorators on a level: the game-flow ports and device hooks are wrapped (created when missing),
    /// and the movement layer's hooks too when it is installed.
    /// </summary>
    public static void Install(Level Level)
    {
        Level.SetService<IGameFlowPorts>(new EquipmentGameFlowPorts(Level.GetService<IGameFlowPorts>()));
        Level.SetService<IDeviceHooks>(new EquipmentDeviceHooks(Level.GetService<IDeviceHooks>(), Level));
        if (Level.GetService<MovementService>() is { } movement)
        {
            movement.Hooks = new EquipmentMovementHooks(movement.Hooks);
        }

        PawnGadgets.RegisterNotifies(Level);
    }
}

/// <summary>The game flow's calls into the equipment and combat ports.</summary>
public sealed class EquipmentGameFlowPorts : IGameFlowPorts
{
    public EquipmentGameFlowPorts(IGameFlowPorts? inner = null)
    {
        Inner = inner ?? GameFlowPortsLocal.Instance;
    }

    public IGameFlowPorts Inner { get; }

    /// <summary>SGameInfo.AddDefaultInventory's call: UpdateInventory then AddDefaultGadgets (loadouts.md rule 1).</summary>
    public void AddDefaultInventory(SBasePawn Pawn, EGameMode Mode)
    {
        Loadouts.UpdateInventory(Pawn, Mode);
        Loadouts.AddDefaultGadgets(Pawn, Mode);
        Inner.AddDefaultInventory(Pawn, Mode);
    }

    public void DeleteAllInventory(SBasePawn Pawn)
    {
        PawnInventory.DeleteAllInventory(Pawn);
        Inner.DeleteAllInventory(Pawn);
    }

    /// <summary>SGameInfo.ResetVisionMode: the goggles back to the normal view.</summary>
    public void ResetVisionMode(SPlayerController PC)
    {
        PC.Goggle?.ResetVisionMode();
        Inner.ResetVisionMode(PC);
    }

    public void CountPoisonDrain(SPlayerController PC)
    {
        PC.CountPoisonDrain();
        Inner.CountPoisonDrain(PC);
    }

    /// <summary>The death entry's equipment and combat parts: the gadget unloaded, the shock ended.</summary>
    [PortOf("uc:SBase.SPlayerController:s_Dead.BeginState", Spec = "spec/combat/death-and-respawn.md#rules")]
    public void BeginDeath(SPlayerController PC)
    {
        PC.SBPawn?.SelectedGadget?.Unload();
        PC.EndStickyShockNow();
        Inner.BeginDeath(PC);
    }

    public void EnterStartState(SPlayerController PC, SBasePawn Pawn, bool bSpy) => Inner.EnterStartState(PC, Pawn, bSpy);

    public void EndDeath(SPlayerController PC) => Inner.EndDeath(PC);

    public void SpawnMagmaHud(SPlayerController PC) => Inner.SpawnMagmaHud(PC);

    public void ZoneTipTouch(SZoneTips Tip, Pawn Pawn) => Inner.ZoneTipTouch(Tip, Pawn);

    public void PlayWaiting(Pawn Pawn) => Inner.PlayWaiting(Pawn);

    public void PlayTeleportEffect(Pawn Pawn, bool bOut, bool bSound) => Inner.PlayTeleportEffect(Pawn, bOut, bSound);

    public void NativePlayerInput(SPlayerController PC, float DeltaTime) => Inner.NativePlayerInput(PC, DeltaTime);

    public void UpdateVoiceChatMuting(SGameInfo Game, bool bInit) => Inner.UpdateVoiceChatMuting(Game, bInit);

    public void HearVoiceChatter(SGameInfo Game, PlayerController Listener, PlayerController Speaker, bool Mute) =>
        Inner.HearVoiceChatter(Game, Listener, Speaker, Mute);
}

/// <summary>
/// The devices' calls into the equipment port (gadget lookup and refills), the HUD (markers, map hints) and the game
/// rules (team messages); each is then passed on.
/// </summary>
public sealed class EquipmentDeviceHooks : IDeviceHooks
{
    private readonly Level? _level;

    public EquipmentDeviceHooks(IDeviceHooks? inner = null, Level? level = null)
    {
        Inner = inner;
        _level = level;
    }

    public IDeviceHooks? Inner { get; }

    public Actor? FindGadgetType(SBasePawn Pawn, ClassRef GadgetClass) => PawnInventory.FindGadgetType(Pawn, GadgetClass);

    /// <summary>The ammo crate's refill: the weapon's full reload or the gadget's charges.</summary>
    public void FullReload(Actor Item)
    {
        switch (Item)
        {
            case SWeapon weapon:
                weapon.eventFullReload();
                break;
            case SGadget gadget:
                gadget.FullReload();
                break;
        }
    }

    /// <summary>SPlayerController.AddGPOOnHud (the HUD's marker list, sent to a remote player's machine).</summary>
    public void AddGPOOnHud(SPlayerController Controller, FVector Location)
    {
        Controller.AddGPOOnHud(Location);
        Inner?.AddGPOOnHud(Controller, Location);
    }

    /// <summary>SPlayerController.RemoveGPOFromHud.</summary>
    public void RemoveGPOFromHud(SPlayerController Controller, FVector Location)
    {
        Controller.RemoveGPOFromHud(Location);
        Inner?.RemoveGPOFromHud(Controller, Location);
    }

    /// <summary>SHud(Controller.myHUD).AddTutoMapMessage (the zone tips' map hint).</summary>
    public void AddTutoMapMessage(SPlayerController Controller, string Message)
    {
        (Controller.myHUD as SHud)?.AddTutoMapMessage(Message);
        Inner?.AddTutoMapMessage(Controller, Message);
    }

    /// <summary>
    /// SGameInfo(Level.Game).BroadcastTeamLocalizedMessage(Team, MessageClass, Switch, None, None, OptionalObject):
    /// on the server only (a client has no game info), each player of the side gets it as ReceiveLocalizedMessage.
    /// </summary>
    public void BroadcastTeamLocalizedMessage(int Team, ClassRef MessageClass, int Switch, Actor? OptionalObject)
    {
        (_level?.Info.Game as SGameInfo)?.BroadcastTeamLocalizedMessage((byte)Team, MessageClass, Switch, null, null, OptionalObject);
        Inner?.BroadcastTeamLocalizedMessage(Team, MessageClass, Switch, OptionalObject);
    }

    public USPlayerProfile? GetPlayerProfile() => Inner?.GetPlayerProfile();
}

/// <summary>The movement port's calls into the weapons, gadgets and melee; the camera and footprints are passed on.</summary>
public sealed class EquipmentMovementHooks : IMovementHooks
{
    public EquipmentMovementHooks(IMovementHooks? inner = null)
    {
        Inner = inner ?? MovementHooksLocal.Instance;
    }

    public IMovementHooks Inner { get; }

    /// <summary>The merc rifle's movement clips (merc-rifle.md rule 24).</summary>
    public void PlayWeaponClip(SPlayerController PC, MovementWeaponClip Clip)
    {
        if (PC.SBPawn?.MyWeapon is not SWeaponDefense rifle)
        {
            Inner.PlayWeaponClip(PC, Clip);
            return;
        }

        switch (Clip)
        {
            case MovementWeaponClip.Jump:
                rifle.AnimateJump();
                break;
            case MovementWeaponClip.Fall:
                rifle.AnimateFall();
                break;
            case MovementWeaponClip.Land:
                rifle.AnimateLand();
                break;
            case MovementWeaponClip.LandHigh:
                rifle.AnimateLandHigh();
                break;
        }
    }

    public void SetWeaponLoaded(SBasePawn Pawn, int Num) => PawnVision.SetWeaponLoaded(Pawn, Num);

    /// <summary>The spy's laser request with the draw (spy-shocker.md rule 2).</summary>
    public void SwitchLaser(SPlayerController PC, bool On)
    {
        if (PC.SBPawn?.MyWeapon is SWeaponAttaque pistol)
        {
            pistol.ServerSwitchLaser(On);
            return;
        }

        Inner.SwitchLaser(PC, On);
    }

    /// <summary>The spy's Use searches in order: grab, revive, elbow (melee rule 2).</summary>
    public bool UseSearch(SPlayerController PC, int Step, bool bCantRollCuzColl) => Step switch
    {
        0 => PC.AskToGrabPawn(),
        1 => PC.FindPawnToReanimate(),
        2 => PC.FindPawnToElbowShotAndGoForIt(bCantRollCuzColl),
        _ => Inner.UseSearch(PC, Step, bCantRollCuzColl),
    };

    /// <summary>SPlayerController.DiscardPickup: Fire, Use and the binocular shot drop the hand item first.</summary>
    public void DiscardPickup(SPlayerController PC)
    {
        PC.DiscardPickup();
        Inner.DiscardPickup(PC);
    }

    /// <summary>The death animation of a fall (combat spec).</summary>
    public void PlayAnimDeath(SPlayerController PC) => PC.PlayAnimDeath(FVector.Zero, PC.Pawn?.Health ?? 0);

    public void FootPrint(SBasePawn Pawn, bool bTrueIfLeftFoot) => Inner.FootPrint(Pawn, bTrueIfLeftFoot);

    /// <summary>
    /// The holstered end of the quick launch sequence (grenade-throw.md rule 6): with the query pending it is cleared,
    /// the launch date taken (the 2 s cooldown starts here) and the quick throw clip played. The clip's script notify
    /// NotifyQuickLaunch at 0.4 s then throws (SBasePawn.NotifyQuickLaunch, registered with the gadget notifies). An
    /// animation set without the converted clip (the built-in table) cannot fire it, so the notify runs at once there.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/grenade-throw.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PlayerTick quick launch branch: bQuickGadgetQuery cleared, fDateLaunchedLastQuickGadget = now, ChangeEnumAction(SA_QUICK_THROW); the throw is ThroStAlMix's AnimNotify_Script NotifyQuickLaunch at 0.4 (converted SPerso.PRO)")]
    public void LaunchQuickGadget(SPlayerController PC)
    {
        if (!PC.bQuickGadgetQuery)
        {
            return;
        }

        PC.bQuickGadgetQuery = false;
        PC.fDateLaunchedLastQuickGadget = PC.Level.TimeSeconds;
        if (PC.SBPawn is not { } pawn)
        {
            return;
        }

        // Forced, as in the original: a pawn still in SA_QUICK_THROW from the last throw replays the clip.
        pawn.ChangeEnumAction(SBasePawn.SA_QUICK_THROW, true);

        if (pawn.bQuickGadgetHasBegun && !QuickThrowNotifyPending(pawn))
        {
            pawn.NotifyQuickLaunch();
        }
    }

    /// <summary>True when the palm channel plays the quick throw clip with its launch notify still ahead.</summary>
    private static bool QuickThrowNotifyPending(SBasePawn pawn)
    {
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.PALMCHANNEL];
        if (c.Sequence != SBasePawn.REF_ThroStAlMix || !c.bAnimNotify || c.Clip is not { } clip)
        {
            return false;
        }

        foreach (PawnClipNotify n in clip.Notifies)
        {
            if (n.Notify is UAnimNotify_Script script && script.NotifyName == SBasePawn.NAME_NotifyQuickLaunch &&
                n.Time > c.Frame)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The drop on a head (drop-attack.md rule 3).</summary>
    public void PerformDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => PC.performDropAttack(PawnToAttack);

    /// <summary>The zip line drop request (drop-attack.md rule 5).</summary>
    public void TellServerForDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => PC.TellServerForDropAttack(PawnToAttack);

    public void SetCameraMode(SPlayerController PC, ECamMode Mode) => Inner.SetCameraMode(PC, Mode);

    public void CameraSmooth(SPlayerController PC, float Smooth) => Inner.CameraSmooth(PC, Smooth);

    public void AttachCameraToBone(SPlayerController PC, bool YesNo, Name BoneName, FVector Offset) =>
        Inner.AttachCameraToBone(PC, YesNo, BoneName, Offset);
}
