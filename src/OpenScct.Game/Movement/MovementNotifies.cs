using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Movement;

/// <summary>
/// The script notifies of the movement clips (AnimNotify_Script NotifyName to the SBasePawn function), registered
/// with the level's <see cref="AnimNotifyHandlers"/>: the split jump's push, the Vandamme pistol draw and the
/// hand-over-hand fire poses (SBasePawn.Movement.Notifies.cs).
/// </summary>
public static class MovementNotifies
{
    private static readonly Name ImpulsionSplit = new("ImpulsionSplit");
    private static readonly Name NotifyTakeArmControlForSplitJump = new("NotifyTakeArmControlForSplitJump");
    private static readonly Name ChangeAnimsForDrawUndrawSplitJump = new("ChangeAnimsForDrawUndrawSplitJump");
    private static readonly Name NotifyTakeArmControlForPipH2 = new("NotifyTakeArmControlForPipH2");
    private static readonly Name NotifyTakeArmControlForPipH4 = new("NotifyTakeArmControlForPipH4");

    /// <summary>Registers the movement notifies with <paramref name="level"/>'s handlers (again is harmless).</summary>
    public static void Install(Level level)
    {
        AnimNotifyHandlers handlers = AnimNotifyHandlers.Of(level);
        handlers.Register(ImpulsionSplit, a => (a as SBasePawn)?.ImpulsionSplit());
        handlers.Register(NotifyTakeArmControlForSplitJump, a => (a as SBasePawn)?.NotifyTakeArmControlForSplitJump());
        handlers.Register(ChangeAnimsForDrawUndrawSplitJump, a => (a as SBasePawn)?.ChangeAnimsForDrawUndrawSplitJump());
        handlers.Register(NotifyTakeArmControlForPipH2, a => (a as SBasePawn)?.NotifyTakeArmControlForPipH2());
        handlers.Register(NotifyTakeArmControlForPipH4, a => (a as SBasePawn)?.NotifyTakeArmControlForPipH4());
    }
}
