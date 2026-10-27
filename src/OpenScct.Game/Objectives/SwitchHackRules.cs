using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// The objective side of the panel hack and hold states (<c>SPlayerController.s_Hack_*</c>, <c>s_Maintain_*</c>,
/// spec/objectives/switch-hack.md rules 9 to 18). The controller port calls these from its states; animation
/// actions, holstering and camera changes stay there.
/// </summary>
public static class SwitchHackRules
{
    /// <summary>
    /// <c>s_Hack_GoTo.BeginState</c>, objective part (rule 9): the selected panel's use spot becomes the
    /// repositioning target (<c>SBasePawn.vVandammePos</c>). False when the selection is not a switch entry, in which
    /// case the caller plays SA_WALK_ST_0H and changes to s_PlayerWalkingSt0H.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_GoTo.BeginState", Sha1 = "55078391729f39356d138733df6def0520226f6b")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    public static bool GoToBeginState(SPlayerController pc, out FVector Facing) => AimAtSelectedSwitch(pc, out Facing);

    /// <summary><c>s_Maintain_GoTo.BeginState</c>, objective part (rule 18): as the hack walk.</summary>
    [Uc("SBase.SPlayerController.s_Maintain_GoTo.BeginState", Sha1 = "9786c6c569b23cf830ae230f62356f5ff80ba095")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    public static bool MaintainGoToBeginState(SPlayerController pc, out FVector Facing) => AimAtSelectedSwitch(pc, out Facing);

    /// <summary>
    /// <c>s_Hack_Enter.BeginState</c> / <c>s_Maintain_Enter.AnimEnd</c>, objective part: remembers the selected entry
    /// and tells the server the hack or hold starts (Interaction sub-index 2).
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_Enter.BeginState", Sha1 = "78469abb737d0dfa935f9b96adfb7ec91d4fd813")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void EnterStart(SPlayerController pc)
    {
        pc.UsedInterraction = pc.IManager?.GetCurrentInteraction();
        InteractionMenuRules.Interaction(pc, pc.UsedInterraction, K.SubStart);
    }

    /// <summary>
    /// <c>s_Hack_Use.BeginState</c> (server, rule 12): the first hackable switch within 200 uu sets a looping timer
    /// of its iHackingTime on the controller and, when its entry is already hacking, the HUD end time; with none, a
    /// looping 3 s timer.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_Use.BeginState", Sha1 = "819b9dc67ac10f09411406a4e97c7ca19e5a7807")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Use.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void UseBeginState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        SInterrupteur? sw = FindHackableSwitch(pc);
        if (sw is null)
        {
            pc.SetTimer(K.NoTargetFallbackTimer, true);
            return;
        }

        pc.SetTimer(sw.iHackingTime, true);
        if (sw.TriggerInteraction is { } entry && entry.IsInState(K.s_Hacking))
        {
            entry.MinTriggerTime = ObjectiveNatives.PreciseElapsedTime(pc.Level) + sw.iHackingTime;
        }
    }

    /// <summary><c>s_Hack_Use.Timer</c> (server, rule 13): a living player leaves with the success label.</summary>
    [Uc("SBase.SPlayerController.s_Hack_Use.Timer", Sha1 = "069fdbab5825b7a43ec9355daf8403b28f312cfb")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Use.Timer", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void UseTimer(SPlayerController pc)
    {
        FindHackableSwitch(pc);
        if (ObjectiveNatives.IsAlive(pc))
        {
            ObjectiveNatives.MirrorState(pc, K.s_Hack_Leave, K.TriggerHack_Successfull);
        }
    }

    /// <summary>
    /// <c>s_Hack_Use.PlayerMove</c> cancel test (input machine, rule 16): more than 1000 on either input axis or the
    /// draw-weapon button. True means change to s_Hack_FastLeave.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_Use.PlayerMove", Sha1 = "8ecdb26e1abddeb4e7557d7a83e3cacab803cfe7")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Use.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
    public static bool UseShouldCancel(SPlayerController pc) =>
        SvmMath.Abs(pc.aStrafe) > K.CancelInputThreshold || SvmMath.Abs(pc.aForward) > K.CancelInputThreshold || pc.bFireMode == 1;

    /// <summary><c>s_Maintain_Use.PlayerMove</c> cancel test (rule 18): input only, no fire-mode test.</summary>
    [Uc("SBase.SPlayerController.s_Maintain_Use.PlayerMove", Sha1 = "653cf77c096b00edda3ca59b6882014603ba5525")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
    public static bool MaintainShouldCancel(SPlayerController pc) =>
        SvmMath.Abs(pc.aStrafe) > K.CancelInputThreshold || SvmMath.Abs(pc.aForward) > K.CancelInputThreshold;

    /// <summary>
    /// The InteractionPost a leave sends (rules 14 and 16): sub-index 1 after the success label, 0 after the failed
    /// label or a fast leave. Clears the remembered entry.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_FastLeave.BeginState", Sha1 = "2f08be68a33ff1063637f864eaa46dcb7ab5fa5c")]
    [Uc("SBase.SPlayerController.s_Hack_Leave@code", Sha1 = "992babfe32f63a63b95a3531ad4303e5d5a19a08")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_FastLeave.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Leave@code", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void PostAndForget(SPlayerController pc, bool success)
    {
        InteractionMenuRules.InteractionPost(pc, pc.UsedInterraction, success ? K.SubSuccess : K.SubFailed);
        pc.UsedInterraction = null;
    }

    /// <summary><c>s_Maintain_Use.BeginState</c>, objective part (rule 18): tells the server the hold has started.</summary>
    [Uc("SBase.SPlayerController.s_Maintain_Use.BeginState", Sha1 = "b8505be034a78895d565349781b76037ab8c5203")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void MaintainUseBeginState(SPlayerController pc) =>
        InteractionMenuRules.InteractionPost(pc, pc.UsedInterraction, K.SubHoldStarted);

    /// <summary>
    /// <c>s_Hack_Enter.PawnTakeDamage</c>, objective part (rule 10): a living player leaves with the failed label.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_Enter.PawnTakeDamage", Sha1 = "59d3a8b15e4c620e1244c92cf3a37f5c57b37529")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.PawnTakeDamage", Spec = "spec/objectives/switch-hack.md#rules")]
    public static void EnterTakeDamage(SPlayerController pc)
    {
        if (ObjectiveNatives.IsAlive(pc))
        {
            ObjectiveNatives.MirrorState(pc, K.s_Hack_Leave, K.TriggerHack_Failed);
        }
    }

    /// <summary>The first switch with a hack time within 200 uu of the player's pawn (it sets the hack duration).</summary>
    public static SInterrupteur? FindHackableSwitch(SPlayerController pc) =>
        pc.Pawn is { } pawn
            ? ObjectiveNatives.FirstWithin<SInterrupteur>(pc.XLevel, pawn.Location, K.SwitchHackSearchRadius, static s => s.iHackingTime != 0)
            : null;

    private static bool AimAtSelectedSwitch(SPlayerController pc, out FVector Facing)
    {
        Facing = default;
        SInterrupteur? sw = (pc.IManager?.GetCurrentInteraction() as STriggerInteraction)?.OwnerInterrupteur;
        if (sw is null || pc.Pawn is not { } pawn)
        {
            return false;
        }

        sw.GetUseSpot(pawn, out FVector spot, out Facing);
        if (pawn is SBasePawn sp)
        {
            sp.vVandammePos = spot;
        }

        return true;
    }
}
