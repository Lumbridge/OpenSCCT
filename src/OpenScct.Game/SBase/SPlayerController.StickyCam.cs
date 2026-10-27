using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController sticky cam functions: entering a cam, the gas, the predicted cam, the previous cam key,
// losing and getting back cams, and the orientable angles. Written from spec/equipment/sticky-cam.md rules 8, 20,
// 24 to 34. The view states are in SPlayerController.s_StickyCam.cs.
public partial class SPlayerController
{
    private static readonly Name CNAME_ServerStickyCamGas = new("ServerStickyCamGas");
    private static readonly Name CNAME_ServerSpawnStickyCam = new("ServerSpawnStickyCam");
    private static readonly Name CNAME_CleanStickyCamOnClient = new("CleanStickyCamOnClient");
    private static readonly Name CNAME_LoseStickyCam = new("LoseStickyCam");
    private static readonly Name CNAME_ReloadStickyCam = new("ReloadStickyCam");
    private static readonly Name CNAME_ServerUpdateOrientableRotation = new("ServerUpdateOrientableRotation");
    private static readonly ClassRef CREF_SGadStickyCam = new("SBase.SGadStickyCam");

    /// <summary>A state's version of StickyCamWasDestroyed.</summary>
    public interface IStickyCamWasDestroyed
    {
        void StickyCamWasDestroyed(SPlayerController self, SPlayerController? Instigator, int NbGasCharge);
    }

    /// <summary>A state's version of the PrevStickyCam key.</summary>
    public interface IPrevStickyCam
    {
        void PrevStickyCam(SPlayerController self);
    }

    /// <summary>A state's version of the clip's interaction notify (AnimNotification_InteractNow).</summary>
    public interface IAnimNotificationInteractNow
    {
        void AnimNotificationInteractNow(SPlayerController self);
    }

    // ---- entering a cam ---------------------------------------------------------------------------------------

    /// <summary>
    /// UseStickyCam (event, rule 8): the gas edges reset, the predicted cam retired, a previous cam destroyed on the
    /// server and forgotten, the new cam used, and the smooth entry (keeping the previous state from the menu).
    /// </summary>
    [Uc("SBase.SPlayerController.UseStickyCam", Sha1 = "32fb9f4dc2268eb1ce112fbdafbed68e65050374")]
    [PortOf("uc:SBase.SPlayerController:UseStickyCam", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
    public void eventUseStickyCam(Actor? ObjToUse)
    {
        bStickyCamGasQueued = false;
        bStickyCamGasHeld = false;
        RetirePredictedStickyCam();
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.UsedObject is { } previous)
        {
            if (Level.NetMode != ENetMode.NM_Client)
            {
                previous.Destroy();
            }

            pawn.UsedObject = null;
        }

        pawn.UsedObject = ObjToUse;
        if (pawn.Health > 0)
        {
            MirrorPredictedState(GadgetStates.s_UseObject_StickyCam_SmoothToPosition, GetStateName() == GadgetStates.s_StickyCam_MenuInteraction);
        }
    }

    /// <summary>
    /// MirrorPredictedState: change state here; a player without a local viewport also gets the state order
    /// (PCChangeClientState) at once.
    /// </summary>
    [Uc("SBase.SPlayerController.MirrorPredictedState", Sha1 = "7d4415a7e8b77c8bb61e2afe2033a18bacae650b")]
    [PortOf("uc:SBase.SPlayerController:MirrorPredictedState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
    public void MirrorPredictedState(Name nomdustate, bool bDontSaveCurrentState = false)
    {
        GotoState(nomdustate, Name.None, bDontSaveCurrentState);
        if (Player is UViewport)
        {
            return;
        }

        GameFlowServices.Host(this).ClientCall(this, new GameCall("PCChangeClientState")
        {
            Float1 = MirrorStateAcknowledgementTime,
            Float2 = Level.TimeSeconds,
            Name1 = nomdustate,
            Bool = bDontSaveCurrentState,
        });
    }

    /// <summary>UsePreviousStickyCam (rule 25): a given cam becomes the used one; alive with a cam, the cam view.</summary>
    [Uc("SBase.SPlayerController.UsePreviousStickyCam", Sha1 = "2b8868ad80cc58fd072ddd746c00810c6336d029")]
    [PortOf("uc:SBase.SPlayerController:UsePreviousStickyCam", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void UsePreviousStickyCam(Actor? ObjToUse)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (ObjToUse is not null)
        {
            pawn.UsedObject = ObjToUse;
        }

        if (pawn.UsedObject is null || pawn.Health <= 0)
        {
            return;
        }

        Name state = GetStateName();
        bool keep = state == GadgetStates.s_StickyCam_MenuInteraction || state == GadgetStates.s_UseObject_StickyCam_SmoothToPosition;
        eventChangeState(GadgetStates.s_UseObject_StickyCam, Name.None, keep);
    }

    /// <summary>GotoStickyInteractionMenu: the sticky cam menu (no 4.0 script calls it).</summary>
    [Uc("SBase.SPlayerController.GotoStickyInteractionMenu", Sha1 = "23a3d85db5353034ca36a61b34dff8ad5ebcaad4")]
    [PortOf("uc:SBase.SPlayerController:GotoStickyInteractionMenu", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void GotoStickyInteractionMenu() => eventChangeState(GadgetStates.s_StickyCam_MenuInteraction);

    /// <summary>The PrevStickyCam key through the current state (its ignores, an empty override), else the class.</summary>
    public void eventPrevStickyCam()
    {
        if (StateNode is IPrevStickyCam s)
        {
            s.PrevStickyCam(this);
        }
        else if (StateRunsGlobal("PrevStickyCam", out _))
        {
            PrevStickyCam();
        }
    }

    /// <summary>
    /// The previous cam key (rule 24, spies): with a sticky cam gadget, no used cam shows the "no cam" message;
    /// otherwise, where the state allows cams and not shocked, back to the used cam.
    /// </summary>
    [Uc("SBase.SPlayerController.PrevStickyCam", Sha1 = "0e5a15afd7237248772e6e669a45c4866822851c")]
    [PortOf("uc:SBase.SPlayerController:PrevStickyCam", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void PrevStickyCam()
    {
        if (SPawnType == PawnType.SPAWNDEFENSE || SBPawn is not { } pawn || PawnInventory.FindGadgetType(pawn, CREF_SGadStickyCam) is null)
        {
            return;
        }

        if (pawn.UsedObject is null)
        {
            GadgetHud.AddMessageToAuxiliaryStack(this, TextNoStickyCamAvailable, K.StickyMessageTime);
        }
        else if (eventCanUseCam() && !pawn.bStickyShocking)
        {
            UsePreviousStickyCam(pawn.UsedObject);
        }
    }

    // ---- gas ---------------------------------------------------------------------------------------------------

    /// <summary>The gas key in the cam view (rule 20): the server releases a dose; a client sends the cam's angles.</summary>
    [Uc("SBase.SPlayerController.UseStickyCamGas", Sha1 = "7d7cc12cebb3c9bf2942b1f3baabfbf43358e031")]
    [PortOf("uc:SBase.SPlayerController:UseStickyCamGas", Spec = "spec/equipment/sticky-cam.md#gas")]
    public void UseStickyCamGas()
    {
        if (SBPawn?.UsedObject is not SStickyCam cam || cam.Smoke_NbDoses <= 0)
        {
            return;
        }

        if (Level.NetMode != ENetMode.NM_Client)
        {
            cam.SpawnGaz();
            return;
        }

        ServerStickyCamGas(cam.iPitch, cam.iYaw);
    }

    /// <summary>The server takes the client's cam angles (and turns the cam), then releases the gas (client to server).</summary>
    [Uc("SBase.SPlayerController.ServerStickyCamGas", Sha1 = "85d87dc6d0cd1b44dfb7a39d68f1e4524e10a648")]
    [PortOf("uc:SBase.SPlayerController:ServerStickyCamGas", Spec = "spec/equipment/sticky-cam.md#gas")]
    public void ServerStickyCamGas(int iPitch, int iYaw)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_ServerStickyCamGas) { Int1 = iPitch, Int2 = iYaw }))
        {
            return;
        }

        if (SBPawn?.UsedObject is not SStickyCam cam)
        {
            return;
        }

        cam.iPitch = iPitch;
        cam.iYaw = iYaw;
        cam.iReplicatedPitch = iPitch;
        cam.iReplicatedYaw = iYaw;
        cam.Rotation = new FRotator(iPitch, iYaw, cam.Rotation.Roll);
        cam.SpawnGaz();
    }

    /// <summary>A client's cam request (rule 6): refused for another pawn's gadget or a dead pawn.</summary>
    [Uc("SBase.SPlayerController.ServerSpawnStickyCam", Sha1 = "86702da6d975ec00cdf8b6ce169c44aff8eb244c")]
    [PortOf("uc:SBase.SPlayerController:ServerSpawnStickyCam", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public void ServerSpawnStickyCam(SGadStickyCam? gad, ClassRef CamClass, FVector Loc, FRotator Rot, int NbGasCharge, Actor? HitActor)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_ServerSpawnStickyCam)
        {
            Target = gad,
            Name1 = new Name(CamClass.Path ?? ""),
            Vector1 = Loc,
            Rotation = Rot,
            Int1 = NbGasCharge,
            Actor1 = HitActor,
        }))
        {
            return;
        }

        if (gad is null || !ReferenceEquals(gad.Owner, SBPawn) || SBPawn is not { Health: > 0 } pawn)
        {
            return;
        }

        gad.SpawnCam(CamClass, pawn, Loc, Rot, NbGasCharge, HitActor);
    }

    // ---- prediction ----------------------------------------------------------------------------------------------

    /// <summary>Retires the predicted cam (rule 32): no longer used, destroyed.</summary>
    [Uc("SBase.SPlayerController.RetirePredictedStickyCam", Sha1 = "61897245c1cf71e1656931e2ccf86e23827d6f82")]
    [PortOf("uc:SBase.SPlayerController:RetirePredictedStickyCam", Spec = "spec/equipment/sticky-cam.md#prediction-reloaded-40-script")]
    public void RetirePredictedStickyCam()
    {
        if (PredictedStickyCam is not { } predicted)
        {
            return;
        }

        if (SBPawn is { } pawn && ReferenceEquals(pawn.UsedObject, predicted))
        {
            pawn.UsedObject = null;
        }

        predicted.Destroy();
        PredictedStickyCam = null;
    }

    /// <summary>
    /// The predicted cam each tick (rule 31): the server's cam takes over its angles and FOV, the view uses it until
    /// then, and it is dropped (leaving the view) after its deadline.
    /// </summary>
    [Uc("SBase.SPlayerController.ReconcilePredictedStickyCam", Sha1 = "48f2803d38c93f592f79ff17eddf84fab53f1a85")]
    [PortOf("uc:SBase.SPlayerController:ReconcilePredictedStickyCam", Spec = "spec/equipment/sticky-cam.md#prediction-reloaded-40-script")]
    public void ReconcilePredictedStickyCam()
    {
        if (PredictedStickyCam is not { } predicted || SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.UsedObject is SStickyCam real && !ReferenceEquals(real, predicted))
        {
            real.iPitch = predicted.iPitch;
            real.iYaw = predicted.iYaw;
            real.fVisionFOV = predicted.fVisionFOV;
            RetirePredictedStickyCam();
            return;
        }

        bool inCamView = GadgetStates.InStickyCamView(this);
        if (pawn.UsedObject is null && inCamView)
        {
            pawn.UsedObject = predicted;
        }

        if (Level.TimeSeconds > PredictedStickyCamDeadline)
        {
            RetirePredictedStickyCam();
            if (inCamView)
            {
                eventChangeState(GadgetStates.s_UseObject_StickyCam_Leave, Name.None, true);
            }
        }
    }

    // ---- losing and getting back cams ---------------------------------------------------------------------------

    /// <summary>StickyCamWasDestroyed through the current state.</summary>
    public void eventStickyCamWasDestroyed(SPlayerController? Instigator, int NbGasCharge)
    {
        if (StateNode is IStickyCamWasDestroyed s)
        {
            s.StickyCamWasDestroyed(this, Instigator, NbGasCharge);
        }
        else
        {
            StickyCamWasDestroyed(Instigator, NbGasCharge);
        }
    }

    /// <summary>A cam is gone (rule 27): the owner loses it; a spy who picked it up gets it back as a charge.</summary>
    [Uc("SBase.SPlayerController.StickyCamWasDestroyed", Sha1 = "de8375afb69a158d7a333a27918e42501aa61b11")]
    [PortOf("uc:SBase.SPlayerController:StickyCamWasDestroyed", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void StickyCamWasDestroyed(SPlayerController? Instigator, int NbGasCharge)
    {
        LoseStickyCam((byte)NbGasCharge);
        Instigator?.ReloadStickyCam((byte)NbGasCharge);
    }

    /// <summary>
    /// The cam view's clean-up on the owner's machine (rule 30, server to owner): the predicted cam retired, the
    /// stance flags, no jump, the HUD back, the view levelled, the FOV reset, the palm clip ended.
    /// </summary>
    [Uc("SBase.SPlayerController.CleanStickyCamOnClient", Sha1 = "723f36ecab703c5ede617732a69d0e2c7390fda6")]
    [PortOf("uc:SBase.SPlayerController:CleanStickyCamOnClient", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void CleanStickyCamOnClient()
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_CleanStickyCamOnClient)))
        {
            return;
        }

        RetirePredictedStickyCam();
        LeaveCamView();
    }

    /// <summary>The shared end of a cam view: stance flags, no jump, HUD state None, level view, FOV reset, palm off.</summary>
    internal void LeaveCamView()
    {
        bCrouchWithRoll = Pawn is { bIsCrouched: true };
        bPressedJump = false;
        GadgetHud.SetHudState(this, Name.None);
        if (Pawn is { } pawn)
        {
            Rotation = new FRotator(0, pawn.Rotation.Yaw, pawn.Rotation.Roll);
        }

        ScopeResetFOV();
        if (IAmOnClient())
        {
            SBPawn?.ChangeEnumAction(SBasePawn.SA_PALM_DESACTIVATE);
        }
    }

    /// <summary>
    /// The first sticky cam gadget among the first 4 selectable ones that is missing charges; null when none.
    /// </summary>
    private SGadStickyCam? MissingStickyCam()
    {
        SGadget? g = SBPawn?.MyGadget?.SelectNext();
        for (int i = 0; i < K.GadgetSlots && g is not null; i++)
        {
            if (g is SGadStickyCam cam && cam.Charge < cam.ClassDefaults.Charge)
            {
                return cam;
            }

            g = g.MyGadget?.SelectNext();
        }

        return null;
    }

    /// <summary>The owner lost a cam (rule 28, server to owner): the next cam gets its default doses.</summary>
    [Uc("SBase.SPlayerController.LoseStickyCam", Sha1 = "74a449d7d473ddcab96c7a93ff7fb50e857f8403")]
    [PortOf("uc:SBase.SPlayerController:LoseStickyCam", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void LoseStickyCam(byte NbStickyGasCharge)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_LoseStickyCam) { Int1 = NbStickyGasCharge }))
        {
            return;
        }

        if (MissingStickyCam() is { } cam)
        {
            cam.iNbSmokeChargeForCurrentSticky = -1;
        }
    }

    /// <summary>A cam picked up (rule 29, server to owner): one charge back, and the next cam keeps its doses.</summary>
    [Uc("SBase.SPlayerController.ReloadStickyCam", Sha1 = "64812f27313523d734bb53fa70471652e12e98e4")]
    [PortOf("uc:SBase.SPlayerController:ReloadStickyCam", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
    public void ReloadStickyCam(byte NbStickyGasCharge)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_ReloadStickyCam) { Int1 = NbStickyGasCharge }))
        {
            return;
        }

        if (MissingStickyCam() is { } cam)
        {
            cam.AddCharge();
            cam.iNbSmokeChargeForCurrentSticky = NbStickyGasCharge;
        }
    }

    // ---- orientable angles -----------------------------------------------------------------------------------------

    /// <summary>Writes an orientable object's angles; any object but the predicted cam also tells the server.</summary>
    [Uc("SBase.SPlayerController.UpdateOrientableRotation", Sha1 = "cc2d55fa1b70db6dbfb7e4f4aa879f9fa6c25915")]
    [PortOf("uc:SBase.SPlayerController:UpdateOrientableRotation", Spec = "spec/equipment/sticky-cam.md#prediction-reloaded-40-script")]
    public void UpdateOrientableRotation(SOrientableObject? O, int iPitch, int iYaw)
    {
        if (O is null)
        {
            return;
        }

        O.iPitch = iPitch;
        O.iYaw = iYaw;
        if (ReferenceEquals(O, PredictedStickyCam))
        {
            return;
        }

        ServerUpdateOrientableRotation(O, iPitch, iYaw);
    }

    /// <summary>The server's copy of the angles for the other viewers (client to server, unreliable).</summary>
    [Uc("SBase.SPlayerController.ServerUpdateOrientableRotation", Sha1 = "60ed07bf08dbe637736f415871122fc1fe38a5bd")]
    [PortOf("uc:SBase.SPlayerController:ServerUpdateOrientableRotation", Spec = "spec/devices/cam-network.md#turning-and-limits")]
    public void ServerUpdateOrientableRotation(SOrientableObject? O, int iPitch, int iYaw)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_ServerUpdateOrientableRotation) { Actor1 = O, Int1 = iPitch, Int2 = iYaw }))
        {
            return;
        }

        if (O is not null)
        {
            O.iReplicatedPitch = iPitch;
            O.iReplicatedYaw = iYaw;
        }
    }

    // ---- the clip's interaction notify ---------------------------------------------------------------------------

    /// <summary>AnimNotification_InteractNow through the current state (the class version does nothing).</summary>
    public void eventAnimNotificationInteractNow()
    {
        if (StateNode is IAnimNotificationInteractNow s)
        {
            s.AnimNotificationInteractNow(this);
        }
    }
}
