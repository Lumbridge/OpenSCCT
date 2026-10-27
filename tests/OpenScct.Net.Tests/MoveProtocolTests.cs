using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net.Moves;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>The move protocol's packing and its client and server halves (spec/native/net-moves.md).</summary>
public sealed class MoveProtocolTests
{
    private static readonly FVector Forward = new(3000f, 0f, 0f);

    [Fact]
    public void ViewLosesTheLowBitAndWraps()
    {
        foreach ((int pitch, int yaw) in new[] { (0, 0), (2, 4), (3, 5), (12999, 65535), (-12000, 16384), (65534, 1) })
        {
            int view = MovePacking.PackView(pitch, yaw);
            MovePacking.UnpackView(view, out int p, out int y);
            Assert.Equal(((pitch / 2) & 0x7FFF) * 2, p);
            Assert.Equal(((yaw / 2) & 0x7FFF) * 2, y);
            Assert.InRange(p, 0, 65534);
            Assert.InRange(y, 0, 65534);
        }
    }

    [Fact]
    public void ActionsPackSixCodesAndACount()
    {
        int[] actions = [328, 1, 0, 255, 511, 77];
        MovePacking.CompressActions(actions, 6, out int val0, out int val1);
        Assert.Equal(328 | (1 << 9) | (0 << 18) | (6 << 27), val0);
        Assert.Equal(255 | (511 << 9) | (77 << 18), val1);
        Span<int> back = stackalloc int[6];
        Assert.Equal(6, MovePacking.UnCompressActions(val0, val1, back));
        Assert.Equal(actions, back.ToArray());
        Assert.Equal(0, val0 >>> 30);
        Assert.Equal(0, val1 >>> 27);
    }

    [Fact]
    public void DiversInfosCarriesPhysicsJumpSpeedAndZone()
    {
        byte merc = MovePacking.DiversInfos(EPhysics.PHYS_Falling, true, merc: true, charging: true, adrenaline: false, zoneConfig: 2);
        Assert.Equal((2 << 4) | 8 | 4 | 2, merc);
        Assert.Equal(EPhysics.PHYS_Falling, MovePacking.DiversPhysics(merc));
        Assert.True(MovePacking.DiversJump(merc));
        Assert.True(MovePacking.DiversSpeed(merc));
        Assert.Equal(2, MovePacking.DiversZone(merc));

        // A spy's adrenaline goes in the speed bit, not the zone bits as in the original (net-moves.md edge cases).
        byte spy = MovePacking.DiversInfos(EPhysics.PHYS_Walking, false, merc: false, charging: false, adrenaline: true, zoneConfig: 0);
        Assert.Equal(0, MovePacking.DiversZone(spy));
        Assert.True(MovePacking.DiversSpeed(spy));
    }

    [Fact]
    public void OldAccelKeepsFourUnitsOnXYAndTwoOnZ()
    {
        foreach (FVector a in new[] { new FVector(1234.9f, -567.3f, 99.9f), new FVector(-8188f, 8188f, -254f), new FVector(9000f, -9000f, 400f), default })
        {
            FVector back = MovePacking.UnpackOldAccel(MovePacking.PackOldAccel(a));
            Assert.InRange(MathF.Abs(back.X - Math.Clamp(a.X, -8188f, 8188f)), 0f, 4f);
            Assert.InRange(MathF.Abs(back.Y - Math.Clamp(a.Y, -8188f, 8188f)), 0f, 4f);
            Assert.InRange(MathF.Abs(back.Z - Math.Clamp(a.Z, -254f, 254f)), 0f, 2f);
        }

        Assert.Equal(-1, MovePacking.OldTimeDelta(1f, 0.8f));
        Assert.Equal(200, MovePacking.OldTimeDelta(1.1f, 1.0f));
    }

    [Fact]
    public void PackedWordsSignExtend()
    {
        int packed = MovePacking.PackW(-100, -12000);
        Assert.Equal(-100, MovePacking.GetPackedLW(packed));
        Assert.Equal(-12000, MovePacking.GetPackedHW(packed));
        Assert.Equal(30000, MovePacking.GetPackedLW(MovePacking.PackW(30000, 5)));
    }

    [Fact]
    public void SavedMovesBecomeARingAtOneHundred()
    {
        var list = new SavedMoveList();
        for (int i = 0; i < 130; i++)
        {
            list.Add(i, new FVector(i, 0f, 0f), EPhysics.PHYS_Walking, 0);
        }

        Assert.Equal(100, list.Count);
        Assert.Equal(30f, list.FirstSMove!.TimeStamp);
        Assert.Equal(129f, list.LastSMove!.TimeStamp);
        int walked = 0;
        float last = -1f;
        for (SavedMove? m = list.FirstSMove; m is not null; m = m.NextMove)
        {
            Assert.True(m.TimeStamp > last);
            last = m.TimeStamp;
            walked++;
        }

        Assert.Equal(100, walked);
    }

    [Fact]
    public void FullMovesWhileAcceleratingShortMovesEveryOtherFrameStanding()
    {
        var rig = new MoveRig();
        rig.Step(Forward, 30);
        Assert.All(rig.Log.FindAll(m => m.EndsWith("Move", StringComparison.Ordinal) || m is "Full" or "Short"), m => Assert.Equal("Full", m));
        rig.Log.Clear();
        rig.Step(default, 40);
        int shorts = rig.Log.FindAll(m => m == "Short").Count;
        Assert.InRange(shorts, 19, 21);
        Assert.DoesNotContain("Full", rig.Log);
    }

    [Fact]
    public void FramesMergeWhileReliableDataIsUnacknowledged()
    {
        var rig = new MoveRig { OutstandingReliable = 40 };
        rig.Step(Forward, 60);

        // A 90 ms window holds about five 60 Hz frames per move.
        int moves = rig.Log.FindAll(m => m == "Full").Count;
        Assert.InRange(moves, 9, 14);
        Assert.Equal(0, rig.Server.MovesIgnored);
    }

    [Fact]
    public void ServerFollowsTheClientWithoutCorrectionsBeyondTheForcedOnes()
    {
        var rig = new MoveRig(delayFrames: 6);
        for (int i = 0; i < 600; i++)
        {
            rig.Step(i % 200 < 120 ? Forward : new FVector(0f, -3000f, 0f));
        }

        // The error check never fires; only the at-least-once-a-second forced corrections go out, and they match
        // the saved moves, so the client never shifts.
        Assert.Equal(0, rig.Client.CorrectionsApplied);
        long corrections = rig.Server.Sent.Corrections + rig.Server.Sent.ShortCorrections;
        Assert.InRange(corrections, 8, 11);
        Assert.InRange((rig.ClientPawn.Location - rig.ServerPawn.Location).Size(), 0f, 60f);
        Assert.Equal(0, rig.ServerController.ReinitRequests);
    }

    [Fact]
    public void ACorrectionShiftsThePresentPositionInsteadOfReplaying()
    {
        var rig = new MoveRig(delayFrames: 6);
        rig.ServerPawn.WallX = 300f;
        rig.Step(Forward, 180);
        Assert.True(rig.Client.CorrectionsApplied > 0);

        // The server stopped at the wall; the client converged on it, within fErrorMax plus one frame of motion.
        Assert.Equal(300f, rig.ServerPawn.Location.X, 3);
        rig.Step(default, 60);
        Assert.InRange(MathF.Abs(rig.ClientPawn.Location.X - rig.ServerPawn.Location.X), 0f, 2.5f);
    }

    [Fact]
    public void CorrectionsAreSkippedInRootMotion()
    {
        var rig = new MoveRig(delayFrames: 4);
        rig.ServerPawn.WallX = 50f;
        rig.Step(Forward, 30);
        rig.ClientPawn.Physics = EPhysics.PHYS_RootMotion;
        int before = rig.Client.CorrectionsApplied;
        float stamp = rig.Client.CurrentTimeStamp;
        rig.Client.MyLongClientAdjustPosition(stamp + 10f, 0f, 0f, 0f, 0f, 0f, 0f, null, 0);
        Assert.False(rig.Client.bUpdatePosition);
        Assert.Equal(stamp + 10f, rig.Client.CurrentTimeStamp);
        Assert.Equal(before, rig.Client.CorrectionsApplied);
    }

    [Fact]
    public void BigMovesCarryActionsAndAreResentThreeTimes()
    {
        var rig = new MoveRig(delayFrames: 2);
        rig.Step(Forward, 10);
        rig.ClientPawn.QueuedActions.AddRange([12, 13, 14]);
        rig.Step(Forward, 10);
        Assert.Single(rig.Log, "Big");
        Assert.Equal(3, rig.Log.FindAll(m => m == "BigResend").Count);
        Assert.Equal([12, 13, 14], rig.ServerPawn.ExecutedActions);
        Assert.Equal(rig.Client.BSMS.TimeStamp, rig.Server.BigServerMoveLastActionTime);
    }

    [Fact]
    public void PCRotMovesLeaveThePawnRotationAndIgnoreTheTimeMargin()
    {
        var rig = new MoveRig(delayFrames: 2);
        rig.ClientPawn.UsesPCRot = true;
        rig.ServerPawn.Rotation = new FRotator(0, 1000, 0);
        rig.ClientController.Rotation = new FRotator(400, 20000, 0);
        rig.Step(default, 20);
        Assert.Contains("PCRot", rig.Log);
        Assert.Equal(new FRotator(0, 1000, 0), rig.ServerPawn.Rotation);
        Assert.Equal(20000, rig.ServerController.Rotation.Yaw);
        Assert.Equal(MovePacking.PackW(20000, 400), rig.ServerPawn.iYawPitchFromView);
        Assert.Equal(MoveK.MaxTimeMarginLow, rig.Server.MaxTimeMargin);
    }

    [Fact]
    public void AZeroOldTimeDeltaIsNoOldMove()
    {
        // ProcessServerMove runs the old move only for a non-zero OldTimeDelta byte (game 0x10aac4a0): a 0 passed in
        // directly must not spend the move's time on the old acceleration.
        var rig = new MoveRig(delayFrames: 1);
        rig.Step(default, 30);
        rig.ServerPawn.Velocity = default;
        int oldAccel = MovePacking.PackOldAccel(new FVector(-3000f, 0f, 0f));
        rig.Server.MyServerMove(rig.Server.CurrentTimeStamp + 1f / 60f, Forward * 10f, rig.ServerPawn.Location, 0, 1 << 4, 0, oldAccel);
        Assert.True(rig.ServerPawn.Velocity.X > 0f, $"the move ran with the old acceleration: velocity {rig.ServerPawn.Velocity}");
    }

    [Fact]
    public void APCRotMoveWritesTheAimBeforeThePhysicsCheck()
    {
        // MyServerMoveWithPCRot writes iYawPitchFromView right after the controller's rotation, so a move rejected
        // for a physics mismatch still turns the aim (game 0x10aaec30); the full form does not.
        var rig = new MoveRig(delayFrames: 1);
        rig.Step(default, 30);
        rig.ServerPawn.Physics = EPhysics.PHYS_Falling;
        rig.ServerPawn.iYawPitchFromView = 0;
        int view = MovePacking.PackView(400, 20000);
        rig.Server.MyServerMove(rig.Server.CurrentTimeStamp + 1f / 60f, Forward * 10f, rig.ServerPawn.Location, view, 1 << 4, -1, 0);
        Assert.Equal(0, rig.ServerPawn.iYawPitchFromView);

        long ignored = rig.Server.MovesIgnored;
        rig.Server.MyServerMoveWithPCRot(rig.Server.CurrentTimeStamp + 2f / 60f, rig.ServerPawn.Location, view, 1 << 4, -1, 0);
        Assert.Equal(ignored + 1, rig.Server.MovesIgnored);
        Assert.Equal(MovePacking.PackW(20000, 400), rig.ServerPawn.iYawPitchFromView);
    }

    [Fact]
    public void TimeMarginZeroesMovesOfAFastClock()
    {
        var rig = new MoveRig(delayFrames: 1);
        rig.Step(Forward, 30);
        long applied = rig.Server.MovesApplied;

        // A speed-hacked client: stamps advance twice as fast as the server's clock.
        for (int i = 0; i < 120; i++)
        {
            float stamp = rig.Server.CurrentTimeStamp + 2f / 60f;
            rig.Server.MyServerMove(stamp, Forward * 10f, rig.ServerPawn.Location, 0, 1 << 4, -1, 0);
        }

        // The server clock stands still meanwhile: about 0.35 s of moves pass the guard, the rest are not simulated.
        Assert.InRange(rig.Server.MovesApplied - applied, 9, 12);
        Assert.InRange(rig.Server.TimeMargin, 0.3f, 0.36f);

        // Reloaded's PCRot wrapper lets a PCRot move through the same guard.
        long before = rig.Server.MovesApplied;
        rig.ServerPawn.UsesPCRot = true;
        rig.Server.MyServerMoveWithPCRot(rig.Server.CurrentTimeStamp + 2f / 60f, rig.ServerPawn.Location, 0, 1 << 4, -1, 0);
        Assert.Equal(MoveK.MaxTimeMarginLow, rig.Server.MaxTimeMargin);
        Assert.Equal(before + 1, rig.Server.MovesApplied);
    }

    [Fact]
    public void LargeDivergenceForcesAReinitAndTheHandshakeCompletes()
    {
        var rig = new MoveRig(delayFrames: 3);
        rig.Step(Forward, 10);
        rig.ServerPawn.Physics = EPhysics.PHYS_Falling;
        rig.ServerPawn.Location += new FVector(0f, 0f, 1000f);
        rig.Step(Forward, 300);
        Assert.True(rig.ServerController.ReinitRequests > 0);
        Assert.Contains("Reinit", rig.Log);
        Assert.Contains("Ready", rig.Log);
        Assert.False(rig.ServerController.bWaitingForReinit);
        Assert.True(rig.ClientPawn.Reinits > 0);
    }

    [Fact]
    public void LossyLinkStillConverges()
    {
        var rig = new MoveRig(delayFrames: 4, lossPercent: 5f, seed: 9);
        rig.ServerPawn.WallX = 500f;
        rig.Step(Forward, 240);
        rig.Step(default, 120);
        Assert.InRange(MathF.Abs(rig.ClientPawn.Location.X - rig.ServerPawn.Location.X), 0f, 2.5f);
    }
}
