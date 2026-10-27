using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// The objective side of the spy's terminal states (<c>SPlayerController.s_Computer_*</c>, spec/objectives/
/// terminal-hack.md rules 8 to 27): terminal searches, the hack timer, progress keeping and messages. The
/// controller port calls these from its state functions; animation actions and camera changes stay there.
/// </summary>
public static class TerminalHackRules
{
    /// <summary>
    /// <c>SPlayerController.UseComputer</c> (server): only a living spy pawn (SPawnAttaque) starts the walk to the
    /// terminal, whatever UsableBy says.
    /// </summary>
    [Uc("SBase.SPlayerController.UseComputer", Sha1 = "a7c8f13a2f65d8d1b2391f430573e75976e7860d")]
    [PortOf("uc:SBase.SPlayerController:UseComputer", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void UseComputer(SPlayerController pc)
    {
        if (pc.Pawn is SPawnAttaque { Health: > 0 })
        {
            ObjectiveNatives.MirrorState(pc, K.s_Computer_GoTo);
        }
    }

    /// <summary>The first terminal within <paramref name="Radius"/> of the player's pawn, in actor-list order.</summary>
    public static SComputerObjectiveTrigger? FindTerminal(SPlayerController pc, float Radius) =>
        pc.Pawn is { } pawn ? ObjectiveNatives.FirstWithin<SComputerObjectiveTrigger>(pc.XLevel, pawn.Location, Radius) : null;

    /// <summary>
    /// <c>s_Computer_GoTo.PlayerTick</c> (input machine), objective part: aims the repositioning at the use spot of
    /// the first terminal within 300 uu (sets <c>SBasePawn.vVandammePos</c> and returns the facing). False when no
    /// terminal is near: the caller plays SA_WALK_ST_0H and changes to s_PlayerWalkingSt0H.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_GoTo.PlayerTick", Sha1 = "7045751d7f3aa5b5a901a125fd4fcc12847eddfd")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.PlayerTick", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static bool GoToPlayerTick(SPlayerController pc, out FVector Facing)
    {
        Facing = default;
        SComputerObjectiveTrigger? terminal = FindTerminal(pc, K.TerminalGoToSearchRadius);
        if (terminal is null || pc.Pawn is not { } pawn)
        {
            return false;
        }

        terminal.GetUseSpot(pawn, out FVector spot, out Facing);
        if (pawn is SBasePawn sp)
        {
            sp.vVandammePos = spot;
        }

        return true;
    }

    /// <summary>
    /// <c>s_Computer_GoTo.NotifyBump</c> / <c>NotifyHitWall</c>: anything but a terminal aborts the walk to the spot
    /// (the caller plays SA_EXIT_REPOS and goes to the waiting state).
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_GoTo.NotifyBump", Sha1 = "aaff7e94b646dea91c5543a7d96b4f34eaa6566b")]
    [Uc("SBase.SPlayerController.s_Computer_GoTo.NotifyHitWall", Sha1 = "070342c323718459c639bb505493e54993fc72f4")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.NotifyBump", Spec = "spec/objectives/terminal-hack.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.NotifyHitWall", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static bool GoToAbortsOn(Actor? Other) => Other is not SComputerObjectiveTrigger;

    /// <summary>
    /// <c>s_Computer_Use.BeginState</c> (server): the first terminal within 200 uu starts hacking with a looping
    /// CaptureTime timer on the controller; with none, a looping 3 s timer only leaves the state.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Use.BeginState", Sha1 = "df6d59a6cf8706a82c5b0a44b42de238e67b4f43")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void UseBeginState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        SComputerObjectiveTrigger? terminal = FindTerminal(pc, K.TerminalUseSearchRadius);
        if (terminal is not null)
        {
            pc.SetTimer(terminal.CaptureTime, true);
            terminal.eventTriggering(true);
        }
        else
        {
            pc.SetTimer(K.NoTargetFallbackTimer, true);
        }
    }

    /// <summary>
    /// <c>s_Computer_Use.Timer</c> (server): a living spy takes the terminal within 200 uu and stops its hack, then
    /// (unless the game ended) leaves through s_Computer_Leave.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Use.Timer", Sha1 = "8fe0521f0e81bf530ca015788560503068e268a5")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.Timer", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void UseTimer(SPlayerController pc)
    {
        if (!ObjectiveNatives.IsAlive(pc))
        {
            return;
        }

        if (FindTerminal(pc, K.TerminalUseSearchRadius) is { } terminal)
        {
            terminal.eventTakeObjective();
            terminal.eventTriggering(false);
        }

        if (!pc.IsInState(K.GameEnded))
        {
            ObjectiveNatives.MirrorState(pc, K.s_Computer_Leave);
        }
    }

    /// <summary>
    /// <c>s_Computer_Use.EndState</c>, objective part: every terminal within 200 uu stops hacking (progress kept).
    /// The caller plays SA_COMPUTER_ENTERBACK for a living pawn.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Use.EndState", Sha1 = "1911ecf3e5c65aa231513fe9a82abcae155627e4")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.EndState", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void UseEndState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority || pc.Pawn is not { } pawn)
        {
            return;
        }

        float r2 = K.TerminalUseSearchRadius * K.TerminalUseSearchRadius;
        foreach (SComputerObjectiveTrigger terminal in pc.XLevel.AllActors<SComputerObjectiveTrigger>())
        {
            if ((terminal.Location - pawn.Location).SizeSquared() < r2)
            {
                terminal.eventTriggering(false);
            }
        }
    }

    /// <summary>
    /// <c>s_Computer_Use.PlayerMove</c> cancel test (input machine): moving more than 1000 on either input axis or
    /// pressing the draw-weapon button. True means change to s_Computer_FastLeave.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Use.PlayerMove", Sha1 = "ae3fae762eb1078e9926f86479ef66372a9a89ac")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.PlayerMove", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static bool UseShouldCancel(SPlayerController pc) =>
        SvmMath.Abs(pc.aStrafe) > K.CancelInputThreshold || SvmMath.Abs(pc.aForward) > K.CancelInputThreshold || pc.bFireMode == 1;

    /// <summary>
    /// <c>s_Computer_FastLeave.BeginState</c>, server part: tells everyone the hack was interrupted and stops the
    /// hack of the terminal behind the server's menu selection.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_FastLeave.BeginState", Sha1 = "0c17912807d5a68ac73a6fe7d20edafc70765a4a")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_FastLeave.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void FastLeaveBeginState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        ObjectiveMessages.Broadcast(pc, K.MsgInterrupted);
        StopSelectedTerminal(pc);
    }

    /// <summary><c>s_Computer_Leave.BeginState</c>, server part: stops the hack of the terminal behind the selection.</summary>
    [Uc("SBase.SPlayerController.s_Computer_Leave.BeginState", Sha1 = "4cd1f91397d6550e93e18e2482648a3c340694a6")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void LeaveBeginState(SPlayerController pc)
    {
        if (pc.Role == ENetRole.ROLE_Authority)
        {
            StopSelectedTerminal(pc);
        }
    }

    /// <summary>
    /// <c>s_Computer_Enter.PawnTakeDamage</c>, objective part (before the standard damage): a living spy leaves
    /// through s_Computer_Leave, and everyone is told the hack was interrupted.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Enter.PawnTakeDamage", Sha1 = "1c3c31d459a3c06ca548f863b1bd01fd513429c0")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.PawnTakeDamage", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void EnterTakeDamage(SPlayerController pc)
    {
        if (ObjectiveNatives.IsAlive(pc))
        {
            ObjectiveNatives.MirrorState(pc, K.s_Computer_Leave);
        }

        ObjectiveMessages.Broadcast(pc, K.MsgInterrupted);
    }

    /// <summary>
    /// <c>s_Computer_Use.PawnTakeDamage</c>, objective part: every hit tells everyone the hack was interrupted
    /// (even one that does not stop it, rule 27). The damage reactions themselves belong to the combat port.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Computer_Use.PawnTakeDamage", Sha1 = "fda9443ed36f965255504ae6fcb5158ddf128ff0")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.PawnTakeDamage", Spec = "spec/objectives/terminal-hack.md#rules")]
    public static void UseTakeDamageMessage(SPlayerController pc) => ObjectiveMessages.Broadcast(pc, K.MsgInterrupted);

    // Triggering(false) on the owner of the server's own menu selection (it can differ from the client's).
    private static void StopSelectedTerminal(SPlayerController pc)
    {
        if (pc.IManager?.GetCurrentInteraction()?.Owner is SComputerObjectiveTrigger terminal)
        {
            terminal.eventTriggering(false);
        }
    }
}
