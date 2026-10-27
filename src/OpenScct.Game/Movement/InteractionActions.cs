using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// SBasePawn.ExecuteAction for the interaction actions the Use states play (spec/native/execute-action.md cases 13,
/// 196 to 200, 226 to 229, 244, 245, 290, 322 and 323): the terminal's enter, loop and back clips, the panel hack's,
/// the press-button clip on the interact channel, the pickups, the bomb plant and the Story disk pickup and drop. Reached from <see cref="TraversalActions"/> for actions it does not handle.
/// </summary>
public static class InteractionActions
{
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 196 and 199: t0.1")]
    private const float EnterTween = 0.1f;

    private static readonly Name KbrdStAlBg0 = new("KbrdStAlBg0");
    private static readonly Name KbrdStAlNt0 = new("KbrdStAlNt0");
    private static readonly Name HackStAlBg0 = new("HackStAlBg0");
    private static readonly Name ButtStAIFd0 = new("ButtStAIFd0");
    private static readonly Name ButtStAIFd2 = new("ButtStAIFd2");
    private static readonly Name PickCrAlFd0 = new("PickCrAlFd0");
    private static readonly Name PickCrAlUp0 = new("PickCrAlUp0");
    private static readonly Name PickStAlFd0 = new("PickStAlFd0");
    private static readonly Name PickStAlUp0 = new("PickStAlUp0");
    private static readonly Name MineStAlBg0 = new("MineStAlBg0");
    private static readonly Name MineCrAlBg0 = new("MineCrAlBg0");
    private static readonly Name MineStAlEd0 = new("MineStAlEd0");
    private static readonly Name MineCrAlEd0 = new("MineCrAlEd0");
    private static readonly Name ExtrStAlBg0 = new("ExtrStAlBg0");
    private static readonly Name ExtrStAlEd0 = new("ExtrStAlEd0");

    /// <summary>Executes one of the interaction actions (else the gadget actions); false for any other action.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 13, 196 to 200, 226 to 229, 244, 245, 290, 322 and 323: physics, clips, IK type")]
    [Provenance(ProvenanceKind.Decoded, "ExecuteAction game 0x10ad6840 cases 0xc4 and 0xc7: the clip, then the quick-gadget cancel (bit 0x10 of 0x2b0: palm to 0 over 0.1 s, clear, eventNotifyQuickLaunch)")]
    [Provenance(ProvenanceKind.Estimated, "The channel blend weights are not modelled")]
    public static bool Execute(SBasePawn pawn, int action)
    {
        switch (action)
        {
            case SBasePawn.SA_COMPUTER_ENTER:
                PawnActions.Walking(pawn);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                PawnActions.Once(pawn, 0, KbrdStAlBg0, EnterTween);
                MovesetActions.QuickGadgetCancel(pawn);
                return true;
            case SBasePawn.SA_COMPUTER_ENTERBACK:
                PawnActions.Walking(pawn);
                Back(pawn, 0, KbrdStAlBg0);
                return true;
            case SBasePawn.SA_COMPUTER_LOOP:
                PawnActions.Walking(pawn);
                PawnActions.Loop(pawn, 0, KbrdStAlNt0, 0f);
                return true;
            case SBasePawn.SA_HACK_ENTER:
                PawnActions.Walking(pawn);
                pawn.IK_Type = IK_Type_.IKT_NONE;
                pawn.PawnAnim.Stop(SBasePawn.INTERACTCHANNEL);
                PawnActions.Once(pawn, 0, HackStAlBg0, EnterTween);
                MovesetActions.QuickGadgetCancel(pawn);
                return true;
            case SBasePawn.SA_HACK_ENTERBACK:
                PawnActions.Walking(pawn);
                Back(pawn, 0, HackStAlBg0);
                return true;
            // Cases 226 to 229, 244, 245, 322 and 323: the pickups, the bomb plant and the disk pickup and drop, one
            // clip on channel 0 with a 0.1 s tween (objectives/disk-hunt.md, bomb-target.md, disk-flag.md).
            case SBasePawn.SA_PICKUP_CR_FD:
                PawnActions.Once(pawn, 0, PickCrAlFd0, EnterTween);
                return true;
            case SBasePawn.SA_PICKUP_CR_UP:
                PawnActions.Once(pawn, 0, PickCrAlUp0, EnterTween);
                return true;
            case SBasePawn.SA_PICKUP_ST_FD:
                PawnActions.Once(pawn, 0, PickStAlFd0, EnterTween);
                return true;
            case SBasePawn.SA_PICKUP_ST_UP:
                PawnActions.Once(pawn, 0, PickStAlUp0, EnterTween);
                return true;
            case SBasePawn.SA_BOMB:
                PawnActions.Once(pawn, 0, pawn.bIsCrouched ? MineCrAlBg0 : MineStAlBg0, EnterTween);
                return true;
            case SBasePawn.SA_BOMB_END:
                PawnActions.Once(pawn, 0, pawn.bIsCrouched ? MineCrAlEd0 : MineStAlEd0, EnterTween);
                return true;
            case SBasePawn.SA_PICKUP_FLAG:
                PawnActions.Once(pawn, 0, ExtrStAlBg0, EnterTween);
                return true;
            case SBasePawn.SA_DROP_FLAG:
                PawnActions.Once(pawn, 0, ExtrStAlEd0, EnterTween);
                return true;
            case SBasePawn.SA_HANDINTERACT:
                PawnActions.Once(pawn, SBasePawn.INTERACTCHANNEL, pawn.WeaponLoaded > 0 ? ButtStAIFd2 : ButtStAIFd0, 0f);
                return true;
            case SBasePawn.SA_HANDINTERACT_BACK:
                Back(pawn, SBasePawn.INTERACTCHANNEL, pawn.WeaponLoaded > 0 ? ButtStAIFd2 : ButtStAIFd0);
                pawn.bActionsSendByServerMove = true;
                return true;
            default:
                return Equipment.GadgetActions.Execute(pawn, action);
        }
    }

    /// <summary>A clip played from its end back to its start (AnimEnd at frame 0).</summary>
    private static void Back(SBasePawn pawn, int channel, Name sequence) =>
        pawn.PawnAnim.Play(channel, PawnActions.Clip(pawn, sequence), 1f, 0f, loop: false, mirror: false, backward: true);
}
