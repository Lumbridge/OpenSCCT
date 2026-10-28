using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>What releasing Use in the menu does (interaction-system.md rule 14).</summary>
public enum EMenuRelease
{
    /// <summary>Use is still held, or the release was already handled for this menu.</summary>
    None,

    /// <summary>Give entry: a gadget charge went to the mate; back to the previous state.</summary>
    Give,

    /// <summary>Exit: back to the previous state.</summary>
    Leave,

    /// <summary>Plain switch, mine or sticky cam: the press-button state (crouched players stand first).</summary>
    PressButton,

    /// <summary>Any other entry: InteractionInit went to the server, which decides the next state.</summary>
    InteractionInitSent,
}

/// <summary>
/// The objective side of the Use menu on the player controller (spec/objectives/interaction-system.md rules 10 to
/// 19): when the menu opens, what releasing Use does, and the three reliable server calls that reach the objects.
/// The controller port calls these from its states; animation actions and HUD texts stay there.
/// </summary>
public static class InteractionMenuRules
{
    /// <summary><c>SPlayerController.CanAddInteract</c>: every entry may join the menu.</summary>
    [Uc("SBase.SPlayerController.CanAddInteract", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [PortOf("uc:SBase.SPlayerController:CanAddInteract", Spec = "spec/objectives/interaction-system.md#rules")]
    public static bool CanAddInteract(SPlayerController pc, SInteractObject IntObj) => true;

    /// <summary>
    /// <c>SPlayerController.DoInteraction</c> (rule 11): opens the menu when Use is held, the player is slow enough,
    /// the current state allows interacting (<paramref name="canInteract"/>, the controller's CanInteract) and an
    /// entry other than Exit is usable. True when the menu opened.
    /// </summary>
    [Uc("SBase.SPlayerController.DoInteraction", Sha1 = "f5ce506da1120de79540f11990bb198fde90e71f")]
    [PortOf("uc:SBase.SPlayerController:DoInteraction", Spec = "spec/objectives/interaction-system.md#rules")]
    public static bool DoInteraction(SPlayerController pc, bool canInteract)
    {
        if (pc.aStrafe * pc.aStrafe + pc.aForward * pc.aForward > K.MenuSpeedLimit * K.MenuSpeedLimit)
        {
            return false;
        }

        if (pc.bUsing > 0 && pc.bCanCheckUsingButton && canInteract && pc.IManager is { } manager &&
            manager.HasUsableInteractions())
        {
            ObjectiveNatives.ChangeState(pc, K.s_MenuInteraction);
            return true;
        }

        if (!pc.bCanCheckUsingButton && pc.bUsing == 0)
        {
            pc.bCanCheckUsingButton = true;
        }

        return false;
    }

    /// <summary><c>s_MenuInteraction.BeginState</c>, menu part (rule 12): the first usable entry is selected.</summary>
    [Uc("SBase.SPlayerController.s_MenuInteraction.BeginState", Sha1 = "fbc03ce0cba4c5ed911195b86a67cdac068da460")]
    [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
    public static void MenuBeginState(SPlayerController pc)
    {
        pc.IManager?.SelectFirstViewAbleItem();
        pc.bCanCheckUsingButton = true;
    }

    /// <summary>
    /// The Use release in <c>s_MenuInteraction.PlayerMove</c> (rule 14), objective part. Changes state for Exit and
    /// Give (back to PreviousState), returns <see cref="EMenuRelease.PressButton"/> for button entries (the caller
    /// picks the standing or crouched press state), and otherwise sends InteractionInit for the selected entry.
    /// </summary>
    [Uc("SBase.SPlayerController.s_MenuInteraction.PlayerMove", Sha1 = "3a9193e29e3c3aea762145118b0c882a7fa34e1b")]
    [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.PlayerMove", Spec = "spec/objectives/interaction-system.md#rules")]
    public static EMenuRelease MenuRelease(SPlayerController pc)
    {
        if (pc.bUsing != 0 || pc.IManager is not { } manager)
        {
            return EMenuRelease.None;
        }

        SInteractObject? entry = manager.GetCurrentInteraction();
        if (entry is SShareObjectInteraction share)
        {
            pc.GiveSelectedGadget(share);
            ObjectiveNatives.ChangeState(pc, pc.PreviousState, Name.None, true);
            return share.aActorToInteractWith is not null ? EMenuRelease.Give : EMenuRelease.Leave;
        }

        if (entry is SLeaveInteraction)
        {
            ObjectiveNatives.ChangeState(pc, pc.PreviousState, Name.None, true);
            return EMenuRelease.Leave;
        }

        if (!pc.bCanCheckUsingButton || entry is null)
        {
            return EMenuRelease.None;
        }

        pc.bCanCheckUsingButton = false;
        if (IsButtonEntry(entry))
        {
            return EMenuRelease.PressButton;
        }

        entry.iLastUsedTime = SvmMath.Trunc(pc.Level.TimeSeconds);
        InteractionInit(pc, entry, manager.SelectedSubInteractions);
        return EMenuRelease.InteractionInitSent;
    }

    /// <summary>A plain switch (neither hackable nor a hold switch) or a wall gadget: pressed with a button animation.</summary>
    public static bool IsButtonEntry(SInteractObject entry) =>
        entry is STriggerInteraction { bHackable: false, bMaintain: false } or SMineProxInteraction or SStickyCamInteraction
            or SMineTrapInteraction;

    /// <summary>
    /// <c>s_InteractWithObjectWaitingEvent.AnimEnd</c> on the interact channel (rule 19): the press animation ended,
    /// so the server is told to use the entry. The caller plays SA_HANDINTERACT_BACK and goes to the waiting state.
    /// </summary>
    [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent.AnimEnd", Sha1 = "0f27e380c87135472794dfd551eb1eb29a2cd5df")]
    [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEvent.AnimEnd", Spec = "spec/objectives/interaction-system.md#rules")]
    public static void PressButtonAnimEnd(SPlayerController pc)
    {
        if (pc.IManager?.GetCurrentInteraction() is { } entry)
        {
            entry.iLastUsedTime = SvmMath.Trunc(pc.Level.TimeSeconds);
            InteractionInit(pc, entry, pc.IManager.SelectedSubInteractions);
        }
    }

    // ---- reliable client-to-server calls (rule 17) -------------------------------------------------------------

    /// <summary>
    /// <c>SPlayerController.InteractionInit(Obj, Index)</c> (server): stamps the use time and lets the object decide.
    /// The server does not re-check usability, distance or team.
    /// </summary>
    [Uc("SBase.SPlayerController.InteractionInit", Sha1 = "dfb163e2813cc884d84449c5ff7e75c261c2e1ba")]
    [PortOf("uc:SBase.SPlayerController:InteractionInit", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SPlayerController.InteractionInit", Spec = "spec/objectives/interaction-system.md#network")]
    public static void InteractionInit(SPlayerController pc, SInteractObject? Obj, int Index)
    {
        if (SendToServer(pc, "InteractionInit", Obj, Index))
        {
            return;
        }

        if (Obj is null || Obj.bDeleteMe)
        {
            return;
        }

        Obj.iLastUsedTime = SvmMath.Trunc(pc.Level.TimeSeconds);
        Obj.eventInitInteract(pc, Index);
    }

    /// <summary><c>SPlayerController.Interaction(Obj, Index)</c> (server).</summary>
    [Uc("SBase.SPlayerController.Interaction", Sha1 = "c68a94b766257c79fcfd1e115e1cb20ee9643210")]
    [PortOf("uc:SBase.SPlayerController:Interaction", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SPlayerController.Interaction", Spec = "spec/objectives/interaction-system.md#network")]
    public static void Interaction(SPlayerController pc, SInteractObject? Obj, int Index)
    {
        if (SendToServer(pc, "Interaction", Obj, Index))
        {
            return;
        }

        if (Obj is { bDeleteMe: false })
        {
            Obj.eventInteract(pc, Index);
        }
    }

    /// <summary><c>SPlayerController.InteractionPost(Obj, Index)</c> (server).</summary>
    [Uc("SBase.SPlayerController.InteractionPost", Sha1 = "99d1f0eb7238317b493200d0c9519e97d25dfffb")]
    [PortOf("uc:SBase.SPlayerController:InteractionPost", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SPlayerController.InteractionPost", Spec = "spec/objectives/interaction-system.md#network")]
    public static void InteractionPost(SPlayerController pc, SInteractObject? Obj, int Index)
    {
        if (SendToServer(pc, "InteractionPost", Obj, Index))
        {
            return;
        }

        if (Obj is { bDeleteMe: false })
        {
            Obj.eventPostInteract(pc, Index);
        }
    }

    /// <summary>
    /// On a client the three calls are reliable server functions (rule 17): they go to the server's copy of the
    /// controller (the game host sends them as the replicated function), which runs them. True when sent.
    /// </summary>
    private static bool SendToServer(SPlayerController pc, string function, SInteractObject? Obj, int Index)
    {
        if (pc.Role >= ENetRole.ROLE_Authority)
        {
            return false;
        }

        GameFlow.GameFlowServices.Host(pc).ServerCall(pc, new GameCall(function) { Actor = Obj, Int = Index });
        return true;
    }

    /// <summary>
    /// <c>SPlayerController.Possess</c>, manager part (rule 9): the first possession spawns the controller's menu,
    /// owned by it; later possessions keep it.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:Possess", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "interaction-system.md rule 9: spawned once per controller on the server, kept across respawns")]
    public static SInteractionManager? EnsureManager(SPlayerController pc)
    {
        if (pc.IManager is { bDeleteMe: false })
        {
            return pc.IManager;
        }

        pc.IManager = ObjectiveNatives.Spawn<SInteractionManager>(pc, pc, pc.Location, pc.Rotation);
        return pc.IManager;
    }
}
