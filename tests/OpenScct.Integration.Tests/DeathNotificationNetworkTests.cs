using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class DeathNotificationNetworkTests
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
    public void DelayedDeadNotificationCannotReturnAHealthyRespawnToSpectating(int victimIndex, bool stateRequest)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 904, SimLinkProfile.A);
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
        float spawnSetupDeadline = owner.fSpawnProtectionEnd;
        for (int i = 0; i < 3; i++)
        {
            bot.Session.World.Level.GetService<IGameHost>()!.ServerCall(owner, new GameCall("AntiBugTellServerIAmDead"));
            Tick(6);
        }
        Assert.Equal(spawnSetupDeadline, owner.fSpawnProtectionEnd);
        var oldBody = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        network.SetLinkProfile(bot.Name, "server", new SimLinkProfile("held-dead-notification", 0, 0, 0) { OneWayLatencyMs = 11000 });
        bot.Session.World.Level.GetService<IGameHost>()!.ServerCall(owner, new GameCall("AntiBugTellServerIAmDead"));
        source.PawnTakeDamage(1000, null, oldBody.Location, default, SDamageType.SDTBase);
        Tick(600);
        var replacement = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        Assert.NotSame(oldBody, replacement);
        Assert.True(replacement.Health > 0);
        if (stateRequest)
        {
            // A delayed owner's corpse animation can finish after possession has already supplied a new
            // acknowledgement stamp. A matching stamp alone cannot authorize death of a living pawn.
            network.SetLinkProfile(bot.Name, "server", null);
            owner.GotoState(new Name("s_Dead"));
            bot.Session.World.Level.GetService<IGameHost>()!.ServerCall(owner, new GameCall("PCChangeServerState")
            {
                Float1 = source.MirrorStateAcknowledgementTime, Name1 = new Name("s_Dead"),
            });
        }
        Tick(120); // The old life's notification has now arrived.
        Assert.Same(replacement, source.Pawn);
        Assert.True(replacement.Health > 0);
        Assert.False(source.IsInState("s_Dead"), "Old-life notification killed the new controller state.");
        Assert.False(source.IsSpectating(), "Old-life notification returned a healthy player to spectating.");
        Assert.False(owner.IsInState("s_Dead"), "The rejected death request left the owner in death.");
        Assert.False(owner.IsSpectating(), "The rejected death request left the owner spectating.");
        Assert.Equal(1, source.PlayerReplicationInfo!.Deaths);

        // The repair still works for the current dead body; simply suppressing the RPC is not a fix.
        network.SetLinkProfile(bot.Name, "server", null);
        Tick(720);
        // Restore the ACK path before checking possession: the artificial 11-second upstream delay
        // also holds the new pawn's reliable channel opening, independently of the stale death request.
        Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        Assert.Same(owner.Pawn, owner.ViewTarget);
        // PlaySession restores a spy's third-person mode after WaitingForPawn.EndState; BotMatch has no
        // presentation layer. The merc's first-person flag is directly observable in this headless fixture.
        if (victimIndex == 2) Assert.False(owner.bBehindView);
        Assert.False(owner.IsInState("s_Dead"));
        Assert.False(owner.IsSpectating());
        var current = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        current.Health = 0;
        source.GotoState(default);
        bot.Session.World.Level.GetService<IGameHost>()!.ServerCall(owner, stateRequest
            ? new GameCall("PCChangeServerState") { Float1 = source.MirrorStateAcknowledgementTime, Name1 = new Name("s_Dead") }
            : new GameCall("AntiBugTellServerIAmDead"));
        Tick(3);
        Assert.True(source.IsInState("s_Dead"));
    }
}
