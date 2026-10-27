using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Team;

/// <summary>
/// The team interactions through the player controller's own states, as the keys reach them (the Use byte, the co-op
/// key): the Use menu on a hurt mate, the revive of a downed mate, and the HUD's quick mate search
/// (spec/objectives/team-interactions.md).
/// </summary>
public sealed class TeamMovesTests
{
    private static readonly PlayerInputFrame Use = new() { bUsing = 1 };

    /// <summary>A spy at (0, -120) facing +Y, a team mate at the origin facing -Y, both with a Use menu.</summary>
    private static (PawnRig Rig, RigMate Mate) Pair(PawnTeam team = PawnTeam.Spy)
    {
        PawnRig rig = Rig(team, at: V(0f, -120f, 0f), options: ContentOptions());
        // The game's combat and equipment hooks (the Use key's grab, revive and elbow searches), as a match has them.
        OpenScct.Game.Equipment.CombatEquipment.Install(rig.Level);
        RigMate mate = rig.AddMate(team, V(0f, 0f, 0f), -16384);
        InteractionMenuRules.EnsureManager(rig.Controller);
        InteractionMenuRules.EnsureManager(mate.Controller);
        rig.Hold(NoInput, 20);
        return (rig, mate);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void HoldingUseBeforeAHurtMateOpensTheMenuAndReleasingHealsHim(PawnTeam team)
    {
        (PawnRig rig, RigMate mate) = Pair(team);
        int full = SHealMateInteraction.DefaultHealth(mate.Pawn);
        mate.Pawn.Health = 40;
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Use, new Name("s_MenuInteraction"), 30, seen), Seen(seen));
        Assert.Same(rig.Controller.IManager!.Interactions[2], rig.Controller.IManager.GetCurrentInteraction());

        rig.Hold(NoInput, 3);
        Assert.Equal(full, mate.Pawn.Health);
        Assert.StartsWith("s_PlayerWalking", rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void UseOnADownedMateRevivesHimAndBothPlayTheirClips()
    {
        Assert.SkipWhen(ContentOptions() is null, "the converted characters (clip lengths) are not in .content");
        (PawnRig rig, RigMate mate) = Pair();
        mate.Controller.Handle_DropAttackedDmg(rig.Pawn);
        var mateSeen = new List<Name> { mate.Controller.GetStateName() };
        for (int i = 0; i < 600 && !mate.Controller.IsInState(new Name("s_LyingOnGround")); i++)
        {
            rig.Step(NoInput);
            if (mateSeen[^1] != mate.Controller.GetStateName())
            {
                mateSeen.Add(mate.Controller.GetStateName());
            }
        }

        Assert.True(mate.Controller.IsInState(new Name("s_LyingOnGround")), Seen(mateSeen) + " clip " + mate.Pawn.PawnAnim[0].Sequence);
        Assert.True(mate.Pawn.bSleeping);

        // Use pressed: the reviver plays the revive, the mate gets up revived. Every frame's states and clips are kept.
        var reviver = new List<string>();
        var revived = new List<string>();
        bool up = false;
        for (int i = 0; i < 900 && !up; i++)
        {
            rig.Step(i < 3 ? Use : NoInput);
            Note(reviver, $"{rig.Controller.GetStateName()} c16 {rig.Pawn.PawnAnim[16].Sequence}");
            Note(revived, $"{mate.Controller.GetStateName()} c0 {mate.Pawn.PawnAnim[0].Sequence}");
            up = i > 3 && mate.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", System.StringComparison.Ordinal);
        }

        string story = "reviver [" + string.Join("; ", reviver) + "] mate [" + string.Join("; ", revived) + "]";
        Assert.True(up, story);
        // The reviver plays ReanStAlFd0 on channel 16; his state ends at the next channel 0 loop end (a loop raises
        // AnimEnd each pass, spec/native/ANSWERS-anim.md 1), the clip plays on.
        Assert.True(reviver.Exists(s => s.Contains("c16 ReanStAlFd0", System.StringComparison.Ordinal)), story);
        Assert.True(revived.Exists(s => s.StartsWith("s_Reanimating", System.StringComparison.Ordinal)), story);
        Assert.False(mate.Pawn.bSleeping);
        Assert.StartsWith("s_PlayerWalking", rig.Controller.GetStateName().ToString());
    }

    private static void Note(List<string> into, string what)
    {
        if (into.Count == 0 || into[^1] != what)
        {
            into.Add(what);
        }
    }

    /// <summary>A wall from y = 100 whose top is at <paramref name="top"/>, with an LG along its front edge.</summary>
    private static (CollisionDataBuilder Level, TraversalDoc Ges) Wall(float top)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        return (b, Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top))));
    }

    [Fact]
    public void TheHudsQuickMateSearchFindsAReadyHelperWithoutTouchingZipP()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, -150f, 0f));
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        rig.Hold(NoInput, 30);
        helper.Input = new PlayerInputFrame { CoopMove = true };
        for (int i = 0; i < 300 && !helper.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(helper.Pawn.bReadyForShortLadder);
        FVector before = rig.Pawn.ZipP1;
        Assert.Equal(SPlayerController.CA_ShortLadder, rig.Controller.CheckIfCanInteractWithMate(out Actor? quick, true));
        Assert.Same(helper.Pawn, quick);
        Assert.Equal(before, rig.Pawn.ZipP1);
        Assert.Equal(SPlayerController.CA_ShortLadder, rig.Controller.CheckIfCanInteractWithMate(out _));
        Assert.Equal(512f, rig.Pawn.ZipP1.Z, 1);
    }
}
