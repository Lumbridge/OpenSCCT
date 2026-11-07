using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercPoseRecoveryTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void MercSlotAimAndUpperBodyPoseRecoverWhenSnapshotsOvertakeDelayedDescriptors(NetProfile profile)
    {
        var wifi = new SimLinkProfile("wifi35", 35, 20, 1f, .5f, .5f, 25);
        using var rig = WalkerRig.Create(wifi, 823, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var dummy = level.SpawnActor(new SWeaponDefenseDummy(), default, default, pawn, pawn)!;
        pawn.WeaponDummy = dummy; dummy.iState = 1;
        PawnClip grenade = PawnActions.Clip(pawn, new Name("ReloStSpFd1"));
        PawnClip reload = PawnActions.Clip(pawn, new Name("ReloStSpFd2S"));
        var poses = new NetworkAnimChannel[1100];
        var slots = new byte[1100];
        int previousSlot = -1;
        rig.Server.BeforeWorldTick += host =>
        {
            int tick = (int)host.World.TickCount + 1;
            pawn.Physics = EPhysics.PHYS_None;
            pawn.Location = new FVector(100, 100, 1000);
            pawn.Velocity = default;
            pawn.WeaponLoaded = 2; pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
            int yaw = tick < 750 ? (tick * 1700) & 65535 : 65000;
            int aim = tick < 750 ? (yaw + (int)(MathF.Sin(tick * .11f) * 14000)) & 65535 : yaw;
            int pitch = tick < 750 ? (int)(MathF.Sin(tick * .07f) * 9000) & 65535 : 0;
            pawn.Rotation = new FRotator(0, yaw, 0);
            pawn.iDesiredYawPitch = pawn.iYawPitchFromView = (pitch << 16) | aim;
            pawn.ActiveWeaponSlot = tick < 750 ? (tick / 13) & 1 : 1;
            if (tick == 750)
            {
                pawn.PawnAnim.Stop(SBasePawn.WEAPONCHANNEL);
                pawn.PawnAnim.SetBlendAlpha(SBasePawn.WEAPONCHANNEL, 0f);
            }
            else if (tick < 750 && previousSlot != pawn.ActiveWeaponSlot)
            {
                pawn.PawnAnim.Play(SBasePawn.WEAPONCHANNEL, pawn.ActiveWeaponSlot == 1 ? grenade : reload, 1f, .1f, false);
                pawn.PawnAnim.SetBlendParams(SBasePawn.WEAPONCHANNEL, 1f, bone: new Name("B Spine1"));
            }
            previousSlot = pawn.ActiveWeaponSlot;
        };
        int compared = 0, descriptorGaps = 0;
        for (int n = 0; n < 1000; n++)
        {
            // Hold early packets, then let newer snapshots overtake them. Reliable animation/body
            // descriptions still wait behind the held packet and must recover without replaying old poses.
            if (n is 300 or 580) rig.SimLink!.SetLinkProfile("server", "client0", wifi with { OneWayLatencyMs = 900 });
            if (n is 310 or 590) rig.SimLink!.SetLinkProfile("server", "client0", null);
            rig.Step(afterServerTick: () =>
            {
                int tick = (int)rig.Server.World.TickCount;
                poses[tick] = NetworkAnimChannel.Capture(pawn.PawnAnim[SBasePawn.WEAPONCHANNEL]);
                slots[tick] = NetworkPawnAttachments.Capture(pawn).ActiveSlot;
            });
            ClientSession client = rig.Clients[0];
            if (n < 240 || client.Driver.FindActorByNetGuid((uint)pawn.ActorId) is not SBasePawn copy) continue;
            Assert.InRange(MathF.Abs(copy.PawnAnim.Aim.FullYaw), 0, MathF.PI * .425f + .001f);
            Assert.InRange(MathF.Abs(copy.PawnAnim.Aim.FullPitch), 0, MathF.PI * .425f + .001f);
            if (!client.PawnStates!.IsPoseBuffered(copy, client.RenderTimeFixed))
            {
                if (client.PawnStates.IsMotionBuffered(copy, client.RenderTimeFixed)) descriptorGaps++;
                continue;
            }
            int shown = (int)(client.RenderTimeFixed / NetTimeline.Fraction);
            float fraction = (client.RenderTimeFixed % NetTimeline.Fraction) / (float)NetTimeline.Fraction;
            if (shown < 1 || shown + 1 >= rig.Server.World.TickCount) continue;
            NetworkAnimChannel expected = NetworkAnimChannel.Interpolate(poses[shown], poses[shown + 1], fraction);
            NetworkAnimChannel actual = NetworkAnimChannel.Capture(copy.PawnAnim[SBasePawn.WEAPONCHANNEL]);
            Assert.Equal(slots[shown], NetworkPawnAttachments.For(copy).ActiveSlot);
            if ((!expected.Sequence.IsNone && expected.Alpha > .01f) || (!actual.Sequence.IsNone && actual.Alpha > .01f))
            {
                Assert.Equal(expected.Sequence, actual.Sequence);
                Assert.True(expected.StartBone == actual.StartBone,
                    $"n={n} shown={shown}+{fraction:F2} expected={expected.Sequence}/{expected.Serial}/{expected.StartBone}/{expected.Frame:F3}/{expected.Alpha:F2} " +
                    $"actual={actual.Sequence}/{actual.Serial}/{actual.StartBone}/{actual.Frame:F3}/{actual.Alpha:F2}");
                Assert.InRange(MathF.Abs(expected.Alpha - actual.Alpha), 0, .02f);
                Assert.InRange(MathF.Abs(expected.Frame - actual.Frame), 0, .02f);
            }
            compared++;
        }
        var observer = Assert.IsType<SPawnDefense_1Mesh>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.True(compared > 300, $"Only {compared} resolved pose comparisons");
        Assert.True(descriptorGaps > 0, "The delayed reliable stream must leave a gap while newer motion arrives");
        Assert.Equal(1, NetworkPawnAttachments.For(observer).ActiveSlot);
        Assert.InRange(observer.PawnAnim[SBasePawn.WEAPONCHANNEL].BlendAlpha, 0, .01f);
        Assert.InRange(MathF.Abs(observer.PawnAnim.Aim.FullYaw), 0, .01f);
        Assert.InRange(MathF.Abs(observer.PawnAnim.Aim.FullPitch), 0, .01f);
        Assert.Equal(EClientState.Joined, rig.Clients[0].State);
    }
}
