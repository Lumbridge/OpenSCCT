using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CombatLifetimeNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void DamageSentByThePreviousPawnCannotKillItsReplacement(int victimIndex, bool deferAfterReceive)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 717, SimLinkProfile.A);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=4?LivesDEF=4?TimesRespawnsATT=1",
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }
        Tick(900);
        var bot = match.Bots[victimIndex];
        var owner = Assert.IsType<SPlayerController>(bot.Controller);
        var source = match.ServerController(bot)!;
        var body = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        int deaths = source.PlayerReplicationInfo!.Deaths;
        var serverBridge = match.Server.World.Level.GetService<NetCombatBridge>()!;
        serverBridge.Trace = [];
        // The connection survives while this directed link holds a reliable self-damage call across a real death
        // and respawn. Every retransmission has the same delay, so it cannot shortcut the intended ordering.
        System.Action? heldCall = null;
        var previous = match.Server.Driver.DeferReceivedFunction;
        if (deferAfterReceive)
        {
            match.Server.Driver.DeferReceivedFunction = (actor, function, args, connection, handler) =>
            {
                if (ReferenceEquals(actor, source) && function.Name == NetCombatBridge.FunctionName && heldCall is null)
                {
                    heldCall = () => handler(actor, function, new NetCallArgs(), connection);
                    return true;
                }
                return previous?.Invoke(actor, function, args, connection, handler) == true;
            };
        }
        else network.SetLinkProfile(bot.Name, "server", new SimLinkProfile("held-owner", 0, 0, 0) { OneWayLatencyMs = 11000 });
        bot.Session.World.Level.GetService<NetCombatBridge>()!.ServerCall(owner,
            new CombatCall(new Name("PawnTakeDamage")) { Int1 = 1000, DamageType = SDamageType.Suicided });
        if (deferAfterReceive)
        {
            Tick(30);
            Assert.NotNull(heldCall);
        }
        source.PawnTakeDamage(1000, null, body.Location, default, SDamageType.SDTBase);
        Assert.True(body.Health <= 0);
        Tick(600);
        var replacement = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        Assert.NotSame(body, replacement);
        Assert.True(replacement.Health > 0);
        Assert.Equal(deaths + 1, source.PlayerReplicationInfo.Deaths);
        if (deferAfterReceive)
        {
            match.Server.Driver.DeferReceivedFunction = previous;
            heldCall!();
        }
        Tick(180);
        Assert.Equal(EClientState.Joined, bot.Session.State);
        Assert.Equal(deaths + 1, source.PlayerReplicationInfo.Deaths);
        Assert.Same(replacement, source.Pawn);
        Assert.True(replacement.Health > 0);
        Assert.Contains("stale pawn PawnTakeDamage", serverBridge.Trace);
        network.SetLinkProfile(bot.Name, "server", null);
        Tick(720); // Let the deliberately delayed reliable stream drain before testing a fresh request.
        // A fresh request from the new possession must still work after old reliable calls have been discarded.
        // Eleven seconds also exceeds movement history: its explicit recovery may replace the pawn without a death.
        var recovered = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        Assert.Equal(deaths + 1, source.PlayerReplicationInfo.Deaths);
        int health = recovered.Health;
        serverBridge.Trace.Clear();
        var clientBridge = bot.Session.World.Level.GetService<NetCombatBridge>()!;
        clientBridge.Trace = [];
        owner.PawnTakeDamage(10, null, recovered.Location, default, SDamageType.SDTBase);
        Tick(120);
        Assert.True(recovered.Health < health,
            $"source {source.GetStateName()} pawn {source.Pawn?.ActorId} health {recovered.Health}, owner {owner.GetStateName()} pawn {owner.Pawn?.ActorId}, " +
            $"session {bot.Session.State}, waiting {clientBridge.Waiting}, server {string.Join(';', serverBridge.Trace)}, client {string.Join(';', clientBridge.Trace)}");
        Assert.Equal(recovered.Health, owner.Pawn!.Health);
    }
}
