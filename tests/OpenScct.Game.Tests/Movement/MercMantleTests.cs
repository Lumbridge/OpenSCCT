using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>The merc's feet-ledge route from barrier-and-low-ledges.md and ExecuteAction case 202, with real clips.</summary>
public sealed class MercMantleTests
{
    private static PawnRig LedgeRig(float height, bool fast, bool blocked = false)
    {
        string characters = Path.Combine(RepoRoot, ".content", "shared", "chars");
        if (!File.Exists(Path.Combine(characters, "SPerso", "Def.anim.json")))
            Assert.Skip("no converted merc animations under .content/shared/chars");
        var collision = Floor();
        collision.AddBox(V(-500, 100, 0), V(500, 500, height));
        if (blocked) collision.AddBox(V(-500, 100, height + 150), V(500, 500, height + 200));
        var ledges = Doc(Ge(0, GEKind.LG, V(-500, 100, height), V(500, 100, height)));
        PawnRig rig = PawnRig.Create(PawnTeam.Merc, collision.Build(), ledges,
            new PawnRigOptions { CharacterContentDir = characters });
        rig.Place(V(0, 63, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(fast);
        Assert.True(rig.WaitStill(120));
        return rig;
    }

    [Theory]
    [InlineData(40, false)]
    [InlineData(64, false)]
    [InlineData(80, false)]
    [InlineData(40, true)]
    [InlineData(64, true)]
    [InlineData(80, true)]
    public void AJumpOntoALowLedgeCompletesTheMercMantle(int height, bool fast)
    {
        PawnRig rig = LedgeRig(height, fast);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_FeetLedge, 120, seen, Up),
            $"{Seen(seen)}; position={rig.Pawn.Location}; velocity={rig.Pawn.Velocity}");
        // The detect state can enter and finish within the same physics tick.
        Assert.NotNull(rig.Pawn.PawnAnim[0].Clip?.Root);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt2H, 180, seen), Seen(seen));
        AssertStandingPose(rig);
        rig.Hold(NoInput, 60);
        AssertStandingPose(rig);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.Z, height + 87, height + 92);
        Assert.InRange(rig.Pawn.Velocity.Size(), 0, 0.1f);

        float stoppedY = rig.Pawn.Location.Y;
        rig.Hold(Up, 20);
        Assert.True(rig.Pawn.Location.Y > stoppedY + 20, $"movement did not resume from {stoppedY}: {rig.Pawn.Location}");
        rig.Hold(NoInput, 30);
        AssertStandingPose(rig);

        // Walk off the far edge: root motion must no longer pin the body to the completed mantle.
        bool fell = false;
        for (int tick = 0; tick < 300; tick++)
        {
            rig.Step(Up);
            if (rig.Pawn.Physics == EPhysics.PHYS_Falling)
            {
                fell = true;
                break;
            }
        }
        Assert.True(fell, $"did not fall from the platform: {rig.Pawn.Location}; {rig.Controller.GetStateName()}");
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt2H, 180));
        rig.Hold(NoInput, 30);
        AssertStandingPose(rig);
        Assert.InRange(rig.Pawn.Location.Z, 87, 92);
    }

    [Theory]
    [InlineData(ENetRole.ROLE_Authority, false)]
    [InlineData(ENetRole.ROLE_Authority, true)]
    [InlineData(ENetRole.ROLE_SimulatedProxy, false)]
    [InlineData(ENetRole.ROLE_SimulatedProxy, true)]
    public void TheReplicatedArmedExitReleasesEitherMercMantlePose(ENetRole role, bool leftFoot)
    {
        PawnRig rig = LedgeRig(80, fast: true);
        SBasePawn pawn = rig.Pawn;
        pawn.Role = role;
        pawn.bIsLeftPart = leftFoot;
        pawn.ExecuteAction(SBasePawn.SA_FEETLEDGE);
        Assert.Equal(new Name(leftFoot ? "LedgStAlFtL" : "LedgStAlFtR"), pawn.PawnAnim[0].Sequence);
        Assert.NotNull(pawn.PawnAnim[0].Clip?.Root);
        pawn.NetworkRootMotion.LockRootMotion(pawn.iLockRootMotionType);
        pawn.PawnAnim.SetFrame(0, pawn.PawnAnim[0].EndFrame);
        pawn.OldPhysics = EPhysics.PHYS_RootMotion;
        FVector velocity = V(14, 28, 0);
        pawn.Velocity = velocity;
        pawn.PawnAnim.SetBlendParams(SBasePawn.DRAWCHANNEL, 1f);
        pawn.PawnAnim.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 1f);
        Name previousRunAnim = pawn.RunAnim;

        // Apply the replicated action directly: the controller's walking BeginState would otherwise
        // repair a missing AFTERFL animation and hide the remote-pawn regression.
        pawn.ExecuteAction(SBasePawn.SA_WALK_ST_2H_AFTERFL);

        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
        AssertStandingPose(rig);
        Assert.Equal(velocity, pawn.Velocity);
        Assert.Equal(pawn.AWalkStFd2, pawn.ForwardAnim);
        // Original AnimateWalking(86) preserves the merc's run clip and restores its armed walk set.
        Assert.Equal(previousRunAnim, pawn.RunAnim);
        Assert.Equal(pawn.AWalkStLt2, pawn.LeftAnim);
        Assert.Equal(1000f, pawn.fWalkSpeedLimit);
        Assert.Equal(2000f, pawn.fRunSpeedLimit);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.DRAWCHANNEL].BlendAlpha);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.DBLJUMPCHANNEL].BlendAlpha);
    }

    [Fact]
    public void InsufficientHeadroomRejectsTheMercMantleAndReturnsControl()
    {
        PawnRig rig = LedgeRig(80, fast: true, blocked: true);
        var seen = new List<Name>();
        Assert.False(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_FeetLedgeDetect, 120, seen, Up), Seen(seen));
        Assert.DoesNotContain(MovementStateNames.s_FeetLedge, seen);
        rig.Hold(NoInput, 60);
        Assert.Equal(MovementStateNames.s_PlayerWalkingSt2H, rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        AssertStandingPose(rig);
        float blockedY = rig.Pawn.Location.Y;
        rig.Hold(Down, 30);
        Assert.True(rig.Pawn.Location.Y < blockedY - 20);
    }

    [Fact]
    public void ALostMercLedgeReleasesTheDetectStateAndReturnsControl()
    {
        PawnRig rig = LedgeRig(80, fast: true);
        rig.Step(JumpPress);
        rig.Pawn.ZipP1 = rig.Pawn.ZipP2 = V(0, 100, 80);
        rig.Controller.eventFeetLedge();
        Assert.Equal(MovementStateNames.s_FeetLedgeDetect, rig.Controller.GetStateName());
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt2H, 180));
        rig.Hold(NoInput, 30);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        AssertStandingPose(rig);
        float stoppedY = rig.Pawn.Location.Y;
        rig.Hold(Down, 30);
        Assert.True(rig.Pawn.Location.Y < stoppedY - 20);
    }

    private static void AssertStandingPose(PawnRig rig)
    {
        Assert.Equal(rig.Pawn.AWaitSt2, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].bLoop);
        Assert.True(rig.Pawn.PawnAnim[0].bPlaying);
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);
    }
}
