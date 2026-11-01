using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The receiving side of the gadget calls (<see cref="GadgetNet"/>): the combat receivers hand over the calls they do
/// not know. Each returns true when it ran the call. Owner-to-server calls on another actor were already checked for
/// ownership by the combat receiver.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Replicated function dispatch of the gadget port (the netcode spec owns the transport)")]
public static class GadgetNetReceive
{
    /// <summary>An owner-to-server call; <paramref name="PC"/> is the server's copy of the sender.</summary>
    public static bool FromOwner(SPlayerController PC, in CombatCall c)
    {
        Actor? target = c.Target;
        switch (c.Function.ToString())
        {
            case "SpawnGaz" when target is SStickyCam cam:
                cam.SpawnGaz();
                return true;
            case "AskServerGiveObjectToPawn" when target is SGadget gad:
                PC.AskServerGiveObjectToPawn(gad, c.Actor1 as SBasePawn, c.Int1);
                return true;
            case "ServerStickyCamGas":
                PC.ServerStickyCamGas(c.Int1, c.Int2);
                return true;
            case "ServerSpawnStickyCam":
                PC.ServerSpawnStickyCam(target as SGadStickyCam, new ClassRef(c.Name1.ToString()), c.Vector1, c.Rotation, c.Int1, c.Actor1);
                return true;
            case "ServerUpdateOrientableRotation":
                PC.ServerUpdateOrientableRotation(c.Actor1 as SOrientableObject, c.Int1, c.Int2);
                return true;
            case "UseCamNetwork":
                PC.UseCamNetwork(c.Int1, c.Bool1);
                return true;
            case "CamNetworkSetFovOnServer":
                PC.CamNetworkSetFovOnServer(c.Actor1 as SStickyCam, c.Float1);
                return true;
            case "CamFire":
                PC.CamFire(c.Float1, c.Actor1 as SCamNetwork);
                return true;
            case "CamNetworkSwitchVisionModeOnServer" when target is SBasePawn pawn:
                PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, c.Actor1 as SCamNetwork, c.Int1);
                return true;
            case "SetGasMaskUseOnServer" when target is SBasePawn pawn:
                PawnGadgets.SetGasMaskUseOnServer(pawn, c.Bool1);
                return true;
            case "SetDesiredGlassValue" when target is SBasePawn pawn:
                PawnVision.SetDesiredGlassValue(pawn, (byte)c.Int1, c.Float1);
                return true;
            case "SetPerturbationGlass" when target is SBasePawn pawn:
                PawnVision.SetPerturbationGlass(pawn, c.Float1, c.Vector1.X);
                return true;
            case "SetAmbientSoundOnServer" when target is SGadGasMask mask && c.Actor1 is SBasePawn wearer:
                mask.SetAmbientSoundOnServer(wearer, (byte)c.Int1);
                return true;
            case "AskServerToRemoveOldestMine" when target is SGadMultiMinesLauncher launcher:
                launcher.AskServerToRemoveOldestMine(c.Actor1, (byte)c.Int1, c.Bool1);
                return true;
            case "SpawnMine" when target is SGadMultiMinesLauncher launcher && c.Actor1 is Pawn placer:
                launcher.SpawnMine(new ClassRef(c.Name1.ToString()), placer, c.Vector1, c.Rotation);
                return true;
            case "SpawnNoiseEmitter" when target is SGadNoiseEmitter snare && c.Actor1 is Pawn shooter &&
                ReferenceEquals(shooter, snare.OwnerPawn):
                snare.SpawnNoiseEmitter(new ClassRef(c.Name1.ToString()), shooter, c.Vector1, c.Rotation, c.Actor2);
                return true;
            case "ThrowBullet" when target is SSpyBulletLauncher launcher:
                launcher.ThrowBullet(c.Vector1, c.Vector2);
                return true;
        }

        return false;
    }

    /// <summary>A server-to-owner call; <paramref name="PC"/> is the owner's copy of the controller.</summary>
    public static bool FromServer(SPlayerController PC, in CombatCall c)
    {
        Actor? target = c.Target;
        switch (c.Function.ToString())
        {
            case "TellClientToDecCharge" when target is SGadStickyCam gad:
                gad.TellClientToDecCharge();
                return true;
            case "LocalSystemMessage" when target is SGadNoiseEmitter snare:
                snare.LocalSystemMessage();
                return true;
            case "MyLocalMessage" when target is SSpyBulletLauncher launcher:
                launcher.MyLocalMessage();
                return true;
            case "TellClientToTestAndSetEMFVisible" when target is SBasePawn pawn:
                pawn.TellClientToTestAndSetEMFVisible(c.Bool1);
                return true;
            case "CleanStickyCamOnClient":
                PC.CleanStickyCamOnClient();
                return true;
            case "LoseStickyCam":
                PC.LoseStickyCam((byte)c.Int1);
                return true;
            case "AddImpact":
                ImpactManager.AddImpactHere(PC.XLevel, c.Name1, c.Vector1, c.Rotation, c.Actor1, c.Bool1);
                return true;
            case "ReloadStickyCam":
                PC.ReloadStickyCam((byte)c.Int1);
                return true;
            case "SetHudPerturbation":
                PC.SetHudPerturbation(c.Float1);
                return true;
            case "AddGPOOnHud":
                PC.AddGPOOnHud(c.Vector1);
                return true;
            case "RemoveGPOFromHud":
                PC.RemoveGPOFromHud(c.Vector1);
                return true;
            case "ClientChaffed":
                PC.eventClientChaffed();
                return true;
            case "ReloadMine":
                PC.ReloadMine(c.Actor1);
                return true;
            case "TellClientToAddGadgetCharge":
                PC.TellClientToAddGadgetCharge(c.Int1, c.Int2);
                return true;
            case "CamNetworkSwitchVisionModeOnAllClients" when target is SBasePawn pawn:
                PawnGadgets.CamNetworkSwitchVisionModeOnAllClients(pawn, c.Actor1 as SCamNetwork, c.Int1);
                return true;
            case "SetDesiredGlassValueOnClients" when target is SBasePawn pawn:
                PawnVision.ClientDesiredGlassValue(pawn, (byte)c.Int1, c.Float1);
                return true;
            case "SetPerturbationGlassOnClients" when target is SBasePawn pawn:
                PawnVision.ClientPerturbationGlass(pawn, c.Float1, c.Vector1.X);
                return true;
            case "UpdateRemoveMineUsabilityOnClient" when target is SGadMultiMinesLauncher launcher:
                launcher.UpdateRemoveMineUsabilityOnClient(c.Bool1);
                return true;
        }

        return false;
    }
}
