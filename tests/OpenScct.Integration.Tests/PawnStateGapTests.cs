using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PawnStateGapTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void NormalSnapshotBeforeBodyKeyOrderingDoesNotReportStateGaps(NetProfile profile)
    {
        using var rig = WalkerRig.Create(new SimLinkProfile("clean200", 200, 0, 0), 510, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        SBasePawn pawn = AddPawn(rig);
        rig.Server.BeforeWorldTick += host =>
        {
            bool crouch = ((host.World.TickCount + 1) / 17 & 1) != 0;
            pawn.bIsCrouched = crouch;
            pawn.ZoneConfig = (byte)(crouch ? 1 : 0);
        };
        for (int i = 0; i < 900; i++) rig.Step();

        Assert.True(rig.Clients[0].PawnStates!.PresentationRecordsReceived > 30);
        Assert.Equal(0, rig.Clients[0].PawnStates!.StateGaps);
    }

    [Fact]
    public void MissingDisplayedStateCountsOnceUntilRecoveryAndReportsTheNextOutage()
    {
        NetClassRegistry classes = NetClasses.Create();
        RepExtension extension = classes.Find(typeof(SPawnAttaque))!.Extension!;
        int index = extension.Functions.FindIndex(f => f.Name == "SvmPawnSnapshot");
        RepExtensionFunction original = extension.Functions[index];
        bool missingBodyKey = false;
        extension.Functions[index] = original with { Receive = (actor, reader, connection) =>
        {
            if (!missingBodyKey) { original.Receive(actor, reader, connection); return; }
            // Keep a complete, valid snapshot but reference the next body key, as if that reliable key
            // were delayed. Preserve the rest of the actor bunch and its normal receive path.
            var source = new FBitReader();
            source.SetData(reader.Buffer, reader.AbsolutePosition, reader.BitsLeft);
            uint epoch = source.ReadIntPacked(), tick = source.ReadIntPacked(), state = source.ReadIntPacked();
            int oldHeaderBits = source.Position;
            var rewritten = new FBitWriter(4096);
            rewritten.WriteIntPacked(epoch); rewritten.WriteIntPacked(tick); rewritten.WriteIntPacked(state + 1);
            int newHeaderBits = rewritten.NumBits;
            while (source.BitsLeft > 0)
            {
                int bits = Math.Min(32, source.BitsLeft);
                rewritten.WriteBits(source.ReadBits(bits), bits);
            }
            var received = new FBitReader(rewritten.Buffer, rewritten.NumBits);
            original.Receive(actor, received, connection);
            Assert.False(received.IsError);
            reader.Skip(oldHeaderBits + received.Position - newHeaderBits);
        } };
        using var rig = WalkerRig.Create(null, 511, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true, Classes = classes });
        SBasePawn pawn = AddPawn(rig);
        void Step(int count) { for (int i = 0; i < count; i++) rig.Step(); }
        Step(180);
        ClientSession client = rig.Clients[0];
        var copy = Assert.IsType<SPawnAttaque>(client.Driver.FindActorByNetGuid((uint)pawn.ActorId));
        PawnStateReplication states = client.PawnStates!;
        Assert.Equal(0, states.StateGaps);

        missingBodyKey = true;
        Step(90);
        Assert.Equal(1, states.StateGaps);
        Assert.False(states.IsPoseBuffered(copy, client.RenderTimeFixed));
        Step(90);
        Assert.Equal(1, states.StateGaps);

        missingBodyKey = false;
        pawn.Health = 99;
        Step(90);
        Assert.Equal(1, states.StateGaps);
        Assert.True(states.IsPoseBuffered(copy, client.RenderTimeFixed));
        missingBodyKey = true;
        Step(90);
        Assert.Equal(2, states.StateGaps);
    }

    private static SBasePawn AddPawn(WalkerRig rig)
    {
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        rig.Server.BeforeWorldTick += _ =>
        {
            pawn.Physics = EPhysics.PHYS_None;
            pawn.Location = new FVector(100, 100, 1000);
            pawn.Velocity = default;
        };
        return pawn;
    }
}
