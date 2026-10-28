using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Movement;

/// <summary>
/// The script notifies of the movement clips (AnimNotify_Script NotifyName to the SBasePawn function), registered
/// with the level's <see cref="AnimNotifyHandlers"/>: the split jump's push, the Vandamme pistol draw and the
/// hand-over-hand fire poses, the root-motion collision switch and the noise notify (SBasePawn.Movement.Notifies.cs),
/// the back-to-wall draw and the hand-item throw (SBasePawn.BtwFireNotifies.cs), the shotgun's per-shell reload
/// notifies (SBasePawn.ReloadNotifies.cs) and the pickup clips' hand notify (Objectives/DiskCarryRules.cs).
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
    private static readonly Name RMColOn = new("RMColOn");
    private static readonly Name AnimNotifyNoise = new("AnimNotifyNoise");
    private static readonly Name AnimNotifyPickup = new("AnimNotifyPickup");
    private static readonly Name[] EventReloadAmmoNb =
    [
        new("EventReloadAmmoNb1"), new("EventReloadAmmoNb2"), new("EventReloadAmmoNb3"), new("EventReloadAmmoNb4"),
        new("EventReloadAmmoNb5"),
    ];

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

        handlers.Register(RMColOn, a => (a as SBasePawn)?.RMColOn());
        handlers.Register(AnimNotifyNoise, a => (a as SBasePawn)?.AnimNotifyNoise());

        // The pickup clips: the picked object (or the Story disk) goes to the hand; a Disk Hunt disk frees the hand.
        handlers.Register(AnimNotifyPickup, a =>
        {
            if (a is SBasePawn pawn)
            {
                Objectives.DiskCarryRules.AnimNotifyPickup(pawn);
            }
        });

        // The shotgun's body reload clip: one notify per shell; the last shell loaded skips to the clip's end.
        handlers.Register(EventReloadAmmoNb[0], a => (a as SBasePawn)?.EventReloadAmmoNb1());
        handlers.Register(EventReloadAmmoNb[1], a => (a as SBasePawn)?.EventReloadAmmoNb2());
        handlers.Register(EventReloadAmmoNb[2], a => (a as SBasePawn)?.EventReloadAmmoNb3());
        handlers.Register(EventReloadAmmoNb[3], a => (a as SBasePawn)?.EventReloadAmmoNb4());
        handlers.Register(EventReloadAmmoNb[4], a => (a as SBasePawn)?.EventReloadAmmoNb5());

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
