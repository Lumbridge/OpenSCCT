using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// SBasePawn.ExecuteAction for the gadget actions (spec/native/execute-action.md cases 1, 14 to 16, 38, 39, 273 and
/// 274): the mine placement clips on the interact channel, the sticky cam palm on the palm channel, the camera network
/// flag and the no-IK switch. Reached from the movement port's interaction actions for actions they do not handle.
/// </summary>
public static class GadgetActions
{
    private static readonly Name MineStAlBg0 = new("MineStAlBg0");
    private static readonly Name MineCrAlBg0 = new("MineCrAlBg0");
    private static readonly Name MineStAlEd0 = new("MineStAlEd0");
    private static readonly Name MineCrAlEd0 = new("MineCrAlEd0");
    private static readonly Name PalmStAlNt0 = new("PalmStAlNt0");
    private static readonly Name PalmCrAlNt0 = new("PalmCrAlNt0");
    private static readonly Name PalmStAlNt2 = new("PalmStAlNt2");
    private static readonly Name PalmCrAlNt2 = new("PalmCrAlNt2");

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 38: the palm loop at rate 0.5")]
    private const float PalmRate = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 38: blend in over 0.2 s")]
    private const float PalmTween = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 39: blend out over 0.4 s")]
    private const float PalmBlendOut = 0.4f;

    /// <summary>Executes one of the gadget actions; false for any other action.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 1, 14 to 16, 38, 39, 273, 274: clips, channels, IK type, bUsingCamNetwork")]
    [Provenance(ProvenanceKind.Estimated, "Channel blend weights and bone masks (Spine1) are not modelled")]
    public static bool Execute(SBasePawn pawn, int action)
    {
        switch (action)
        {
            case SBasePawn.SA_NO_IK:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                return true;
            case SBasePawn.SA_DROPMINE_GRAB:
                PawnActions.Once(pawn, SBasePawn.INTERACTCHANNEL, pawn.bIsCrouched ? MineCrAlBg0 : MineStAlBg0, 0f);
                return true;
            case SBasePawn.SA_DROPMINE:
                PawnActions.Once(pawn, SBasePawn.INTERACTCHANNEL, pawn.bIsCrouched ? MineCrAlEd0 : MineStAlEd0, 0f);
                return true;
            case SBasePawn.SA_DROPMINE_UNGRAB:
                pawn.PawnAnim.Play(SBasePawn.INTERACTCHANNEL, PawnActions.Clip(pawn, pawn.bIsCrouched ? MineCrAlBg0 : MineStAlBg0), 1f, 0f,
                    loop: false, mirror: false, backward: true);
                return true;
            case SBasePawn.SA_PALM_USING:
                if (!pawn.PawnAnim.IsAnimating(SBasePawn.PALMCHANNEL))
                {
                    Name palm = pawn.WeaponLoaded == 0
                        ? (pawn.bIsCrouched ? PalmCrAlNt0 : PalmStAlNt0)
                        : (pawn.bIsCrouched ? PalmCrAlNt2 : PalmStAlNt2);
                    PawnActions.Loop(pawn, SBasePawn.PALMCHANNEL, palm, 0f, PalmRate);
                }

                pawn.PawnAnim.BlendToAlpha(SBasePawn.PALMCHANNEL, 1f, PalmTween);

                pawn.IK_Type = IK_Type_.IKT_NONE;
                return true;
            case SBasePawn.SA_PALM_DESACTIVATE:
                pawn.PawnAnim.BlendToAlpha(SBasePawn.PALMCHANNEL, 0f, PalmBlendOut);
                pawn.IK_Type = pawn.IK_Type_Previous;
                return true;
            case SBasePawn.SA_CAMNETWORK_BG:
                pawn.bUsingCamNetwork = true;
                return true;
            case SBasePawn.SA_CAMNETWORK_END:
                pawn.bUsingCamNetwork = false;
                return true;
            default:
                return PawnVision.ExecuteGoggleAction(pawn, action);
        }
    }
}
