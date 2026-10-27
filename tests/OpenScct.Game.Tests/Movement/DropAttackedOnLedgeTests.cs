using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// A spy hanging from a ledge (OnLedge) takes a drop attack (spec/combat/drop-attack.md rules 9 and 11): the global
/// handler mirrors s_FallDropAttacked, whose BeginState, the pawn not walking, plays SA_FALL_INERTE (falling physics)
/// and goes to s_FallFallDropAttacked; the pawn falls and lands in s_FallDown. Playtest (MounE): a bot walking along
/// the lip stepped onto the hanging player, and the player stayed in s_FallFallDropAttacked at PHYS_Ledge for the rest
/// of the match with no control.
/// </summary>
public sealed class DropAttackedOnLedgeTests
{
    private static readonly Name s_FallDropAttacked = new("s_FallDropAttacked");
    private static readonly Name s_FallFallDropAttacked = new("s_FallFallDropAttacked");

    private static GERecord Ge(int index, GEKind kind, params FVector[] points) =>
        new(index, $"GE{index}", kind, points, 0, GEPlatform.Common, GESource.Synthetic("drop attack test"));

    private static TraversalDoc Doc(params GERecord[] ges) => new("1.0", "test", ges);

    [Fact]
    public void AHangingSpyDropAttackedFallsOffTheLedge()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 100f, 0f), V(300f, 600f, 250f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-300f, 100f, 250f), V(300f, 100f, 250f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, b.Build(), ges);
        rig.Place(V(0f, 60f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        rig.Step(new PlayerInputFrame { Jump = true });
        for (int i = 0; i < 120 && !rig.Controller.IsInState(MovementStateNames.OnLedge); i++)
        {
            rig.Step(default);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), $"not hanging: {rig.Controller.GetStateName()}");
        rig.Hold(default, 20);

        // The victim side of the drop attack (rule 9): the global handler's MirrorState.
        rig.Controller.eventMirrorState(s_FallDropAttacked);
        var seen = new List<string> { $"after mirror: {rig.Controller.GetStateName()} {rig.Pawn.Physics} action {rig.Pawn.sAction} role {rig.Controller.Role}/{rig.Controller.RemoteRole}" };
        for (int i = 0; i < 180; i++)
        {
            rig.Step(default);
            string s = $"{rig.Controller.GetStateName()}/{rig.Pawn.Physics}";
            if (seen.Count == 0 || seen[^1] != s)
            {
                seen.Add(s);
            }
        }

        string trace = string.Join(", ", seen);
        Assert.True(rig.Pawn.Physics != EPhysics.PHYS_Ledge, trace);
        Assert.Contains(seen, s => s.StartsWith(s_FallFallDropAttacked.ToString(), System.StringComparison.Ordinal) && s.EndsWith("PHYS_Falling", System.StringComparison.Ordinal));
        Assert.False(rig.Controller.IsInState(s_FallFallDropAttacked), trace);
        Assert.True(rig.Pawn.Location.Z < 100f, $"z {rig.Pawn.Location.Z}: {trace}");
    }
}
