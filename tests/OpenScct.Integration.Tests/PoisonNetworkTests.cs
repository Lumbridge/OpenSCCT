using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PoisonNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void PoisonHealthAndCureReachOwnersAndLateViewersWithoutClientDrainOrRespawnLeak(NetProfile profile)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 746, SimLinkProfile.Ping200);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Profile = profile,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=4?LivesDEF=4?TimesRespawnsATT=1",
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        ClientSession? observer = null;
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                observer?.Tick(clock.NowMicroseconds);
            }
        }

        Tick(900);
        var bot = match.Bots[0];
        var owner = Assert.IsType<SPlayerController>(bot.Controller);
        var controller = match.ServerController(bot)!;
        var source = Assert.IsAssignableFrom<SBasePawn>(match.ServerController(match.Bots[1])!.Pawn);
        var pawn = Assert.IsAssignableFrom<SPawnAttaque>(controller.Pawn);
        pawn.bAlwaysRelevant = source.bAlwaysRelevant = true;
        Tick(60);

        Infect();
        Assert.Equal(100, pawn.Health); // The mine's injection itself does no immediate damage.
        Tick(120);
        Assert.InRange(pawn.Health, 87, 89);
        // Hold the authority at a stable health to compare exact delivered values, despite network latency.
        pawn.fPoisonned_HealthLostPerSecond = 0;
        Tick(60);
        CheckPoisoned(bot.Session);
        CheckPoisoned(match.Bots[1].Session);

        using var late = new ClientSession(network.Bind("late"), "server", clock.NowMicroseconds,
            new ClientSettings
            {
                PlayerName = "PoisonViewer", Gameplay = true, Classes = WalkerRig.Classes, Maps = WalkerRig.Maps,
                LoginOptions = "?SpectatorOnly=true?Team=255",
            }, ownsTransport: true);
        observer = late;
        Tick(240);
        Assert.True(late.State == EClientState.Joined, late.RejectReason);
        CheckPoisoned(late);

        var infirmary = match.Server.World.Level.SpawnActor(new SInfirmary { bNoDelete = false }, default, default)!;
        infirmary.Use(controller);
        Tick(120);
        Assert.Equal(100, pawn.Health);
        Assert.False(pawn.bPoisonned);
        foreach (var client in new[] { bot.Session, match.Bots[1].Session, late })
        {
            var copy = Copy(client, pawn);
            Assert.Equal(100, copy.Health);
            Assert.False(copy.bPoisonned);
        }

        int deaths = controller.PlayerReplicationInfo!.Deaths;
        pawn.Health = 1;
        pawn.bHealthDecrease = 1;
        pawn.fPoisonned_HealthLostPerSecond = 6;
        Infect();
        Tick(600);
        Assert.True(pawn.Health <= 0);
        Assert.False(pawn.bPoisonned);
        Assert.Equal(deaths + 1, controller.PlayerReplicationInfo.Deaths);
        var replacement = Assert.IsAssignableFrom<SPawnAttaque>(controller.Pawn);
        Assert.NotSame(pawn, replacement);
        var ownedReplacement = Assert.IsAssignableFrom<SPawnAttaque>(owner.Pawn);
        Assert.Same(Copy(bot.Session, replacement), ownedReplacement);
        Assert.Equal(100, replacement.Health);
        Assert.Equal(100, ownedReplacement.Health);
        Assert.False(replacement.bPoisonned);
        Assert.False(ownedReplacement.bPoisonned);
        Assert.Null(replacement.PoisonnedBy);
        Assert.Null(ownedReplacement.PoisonnedBy);

        void Infect()
        {
            controller.PawnTakeDamage(0, source, pawn.Location, default, SDamageType.SDamage_Poison);
            pawn.PoisonnedBy = source; // The mine writes this after its zero-damage injection.
            Assert.True(pawn.bPoisonned);
        }

        void CheckPoisoned(ClientSession client)
        {
            var copy = Copy(client, pawn);
            Assert.True(copy.bPoisonned);
            Assert.Equal(pawn.Health, copy.Health);
            Assert.Same(Copy(client, source), copy.PoisonnedBy);
            Assert.NotEqual(ENetRole.ROLE_Authority, copy.Role);
            int health = copy.Health;
            copy.NativeTickHealth(1000f);
            Assert.Equal(health, copy.Health);
            Assert.True(copy.bPoisonned);
        }

        static SBasePawn Copy(ClientSession client, SBasePawn authority) => Assert.IsAssignableFrom<SBasePawn>(
            client.Driver.FindActorByNetGuid((uint)authority.ActorId));
    }
}
