using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;

namespace OpenScct.Game.SBase;

// The state table of SBase.SPlayerController. The controller has 280 states ported by several areas, each in its
// own SPlayerController.<State>.cs file; every nested state class with a static Instance is collected here, so a
// new state file needs no edit of this one.
public partial class SPlayerController
{
    public static new readonly StateTable ClassStates = new(PlayerController.ClassStates, NestedStates.Of(typeof(SPlayerController)));

    public override StateTable GetStateTable() => ClassStates;
}
