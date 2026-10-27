using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Movement;

/// <summary>
/// The script notifies of the movement clips (AnimNotify_Script NotifyName to the SBasePawn function), registered
/// with the level's <see cref="AnimNotifyHandlers"/>: the split jump's push, the Vandamme pistol draw and the
/// hand-over-hand fire poses (SBasePawn.Movement.Notifies.cs), the back-to-wall draw and the hand-item throw
/// (SBasePawn.BtwFireNotifies.cs).
/// </summary>
public static class MovementNotifies
{
    private static readonly Name ImpulsionSplit = new("ImpulsionSplit");
    private static readonly Name NotifyTakeArmControlForSplitJump = new("NotifyTakeArmControlForSplitJump");
    private static readonly Name ChangeAnimsForDrawUndrawSplitJump = new("ChangeAnimsForDrawUndrawSplitJump");
    private static readonly Name NotifyTakeArmControlForPipH2 = new("NotifyTakeArmControlForPipH2");
    private static readonly Name NotifyTakeArmControlForPipH4 = new("NotifyTakeArmControlForPipH4");
    private static readonly Name NotifyTakeArmControlForBTWFireLeft = new("NotifyTakeArmControlForBTWFireLeft");
    private static readonly Name NotifyTakeArmControlForBTWFireRight = new("NotifyTakeArmControlForBTWFireRight");
    private static readonly Name NotifyDrawUndrawWeapon = new("NotifyDrawUndrawWeapon");
    private static readonly Name NotifyChangeHandWeapon = new("NotifyChangeHandWeapon");
    private static readonly Name AnimNotifyThrow = new("AnimNotifyThrow");
    private static readonly Name NotifyUndrawWeapon = new("NotifyUndrawWeapon");

    /// <summary>Registers the movement notifies with <paramref name="level"/>'s handlers (again is harmless).</summary>
    public static void Install(Level level)
    {
        AnimNotifyHandlers handlers = AnimNotifyHandlers.Of(level);
        handlers.Register(ImpulsionSplit, a => (a as SBasePawn)?.ImpulsionSplit());
        handlers.Register(NotifyTakeArmControlForSplitJump, a => (a as SBasePawn)?.NotifyTakeArmControlForSplitJump());
        handlers.Register(ChangeAnimsForDrawUndrawSplitJump, a => (a as SBasePawn)?.ChangeAnimsForDrawUndrawSplitJump());
        handlers.Register(NotifyTakeArmControlForPipH2, a => (a as SBasePawn)?.NotifyTakeArmControlForPipH2());
        handlers.Register(NotifyTakeArmControlForPipH4, a => (a as SBasePawn)?.NotifyTakeArmControlForPipH4());
        handlers.Register(NotifyTakeArmControlForBTWFireLeft, a => (a as SBasePawn)?.NotifyTakeArmControlForBTWFireLeft());
        handlers.Register(NotifyTakeArmControlForBTWFireRight, a => (a as SBasePawn)?.NotifyTakeArmControlForBTWFireRight());

        // Only the back-to-wall draw clips: the other clips with these notifies load the weapon at their action.
        handlers.Register(NotifyDrawUndrawWeapon, a => (a as SBasePawn)?.BtwDrawNotify(bChangeHand: false));
        handlers.Register(NotifyChangeHandWeapon, a => (a as SBasePawn)?.BtwDrawNotify(bChangeHand: true));
        handlers.Register(AnimNotifyThrow, a => (a as SBasePawn)?.AnimNotifyThrow());

        // The spy's holster clips (DrawStSpEd2, DrawCrSpEd2): the pistol goes away (4), its AnimEnd unloads it (0).
        handlers.Register(NotifyUndrawWeapon, a =>
        {
            if (a is SPawnAttaque spy)
            {
                Equipment.PawnWeapons.NotifyUndrawWeapon(spy);
            }
        });
    }
}
