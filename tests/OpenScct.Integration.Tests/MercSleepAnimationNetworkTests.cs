using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercSleepAnimationNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public bool Moving;
        public override BotInput Think(BotContext context)
        {
            return new BotInput { BT_Up = (byte)(Moving ? 1 : 0) };
        }
    }

    [Theory]
    [InlineData(NetProfile.Stock, ENetMode.NM_DedicatedServer, false, false, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_DedicatedServer, false, false, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_ListenServer, false, false, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_ListenServer, true, false, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_ListenServer, false, true, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_ListenServer, true, true, false)]
    [InlineData(NetProfile.Enhanced, ENetMode.NM_ListenServer, true, true, true)]
    public void CameraGasPlaysFallAndWakeClipsOnAuthorityOwnerAndObserver(NetProfile profile, ENetMode mode, bool moving, bool sidearm, bool stallObserver)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 506, mode == ENetMode.NM_ListenServer
            ? new SimLinkProfile("wifi35", 35, 20, 1f, 0.5f, 0.5f, 25) : SimLinkProfile.Ping200);
        var mercBrain = new IdleBrain();
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), index => index == 0 ? mercBrain : new IdleBrain()),
            NetMode = mode, Profile = profile,
        }, network.Bind, "server", 0);
        long frame = 0;
        long stallUntil = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                if (frame < stallUntil)
                {
                    match.Server.Tick(clock.NowMicroseconds);
                    for (int bot = 1; bot < match.Bots.Count; bot++) match.Bots[bot].Tick(clock.NowMicroseconds);
                }
                else match.Step(clock.NowMicroseconds);
            }
        }

        Tick(900);
        if (mode == ENetMode.NM_ListenServer)
        {
            match.ServerController(match.Bots[0])!.StartMatchOnServer();
            Tick(180);
        }
        var client = match.Bots[2];
        var observer = match.Bots[0];
        var authority = match.ServerController(client)!;
        var pawn = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var owner = Assert.IsType<SPlayerController>(client.Controller);
        var ownPawn = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        var seenPawn = Assert.IsAssignableFrom<SBasePawn>(observer.Session.Driver.FindActorByNetGuid((uint)pawn.ActorId));
        if (sidearm)
        {
            SidearmEquipment.Give(pawn, SidearmKind.Suppressed);
            Tick(120); owner.SwapWeapon(); Tick(120);
            Assert.Equal(1, pawn.ActiveWeaponSlot);
            Assert.Equal(1, ownPawn.ActiveWeaponSlot);
        }
        Name fall = pawn.ADeathStDown2H;
        var sourceClips = new HashSet<Name>();
        var ownerClips = new HashSet<Name>();
        var observerClips = new HashSet<Name>();
        var gas = authority.XLevel.SpawnActor(new SStickyCamEmitter(), pawn.Location - new FVector(192f, 0f, 0f), default)!;
        gas.Instigator = match.ServerController(observer)!.Pawn;
        var timeline = new List<string>();
        var badSleepPoses = new List<string>();
        int sleepTicks = 0;
        bool stalled = false;
        string? previous = null;
        for (int i = 0; i < 20 * Sim.Hz; i++)
        {
            if (stallObserver && !stalled && owner.btoCought && owner.fSmoke_FallDate - owner.Level.TimeSeconds is > 0f and < .25f)
            {
                // Withhold the viewing client's entire frame loop across the collapse, as a first-use
                // model load would, while the remote merc and authority continue producing packets.
                stalled = true;
                stallUntil = frame + Sim.Hz;
            }
            Tick(1);
            if (i == 20) mercBrain.Moving = moving;
            sourceClips.Add(pawn.PawnAnim[0].Sequence);
            ownerClips.Add(ownPawn.PawnAnim[0].Sequence);
            observerClips.Add(seenPawn.PawnAnim[0].Sequence);
            string current = $"{authority.GetStateName()}/{pawn.PawnAnim[0].Sequence}/{pawn.bSleeping}/{pawn.bLyingOnGround} | {owner.GetStateName()}/{ownPawn.PawnAnim[0].Sequence}/{ownPawn.bSleeping}/{ownPawn.bLyingOnGround} | {seenPawn.PawnAnim[0].Sequence}/{seenPawn.bSleeping}/{seenPawn.bLyingOnGround}";
            if (current != previous) timeline.Add($"{i}: {current}");
            previous = current;
            sleepTicks = seenPawn.bSleeping ? sleepTicks + 1 : 0;
            if (sleepTicks > 0 && seenPawn.PawnAnim[0].Sequence != fall)
                badSleepPoses.Add($"{i}: collapse starts with {seenPawn.PawnAnim[0].Sequence}");
            // The native downed blend removes locomotion overlays at 10/s. Allow 0.2s for
            // snapshot interpolation, then check the fall itself, not just its final sleeping pose.
            if (sleepTicks > 12)
            {
                for (int channel = 1; channel <= SBasePawn.LASTCHANNEL; channel++)
                    if (seenPawn.PawnAnim[channel].BlendAlpha > 0.01f)
                        badSleepPoses.Add($"{i}: channel {channel} {seenPawn.PawnAnim[channel].Sequence} alpha {seenPawn.PawnAnim[channel].BlendAlpha}");
            }
            if (pawn.bSleeping && authority.IsInState("s_LyingOnGround") && pawn.PawnAnim[0].Sequence != fall)
                badSleepPoses.Add($"{i}: authority sleeping with {pawn.PawnAnim[0].Sequence}");
        }

        Assert.True(sourceClips.Contains(fall), $"Authority missing {fall}; source {string.Join(',', sourceClips)}; owner {string.Join(',', ownerClips)}; observer {string.Join(',', observerClips)}.");
        Assert.True(!stallObserver || stalled, "The observer stall must cross the beginning of the gas collapse.");
        Assert.Contains(fall, ownerClips);
        Assert.Contains(fall, observerClips);
        Assert.True(badSleepPoses.Count == 0, string.Join('\n', badSleepPoses) + "\n" + string.Join('\n', timeline));
        Assert.False(pawn.bSleeping, string.Join('\n', timeline));
        Assert.False(pawn.bLyingOnGround);
        Assert.False(ownPawn.bSleeping);
        Assert.False(seenPawn.bSleeping);
        Assert.Equal(EClientState.Joined, client.Session.State);
    }
}
