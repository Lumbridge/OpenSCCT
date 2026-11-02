using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.Tests.Combat;

public sealed class CombatActivityTests
{
    private sealed class Rig
    {
        public Rig(bool localVictim = false)
        {
            World = new GameWorld(localVictim ? ENetMode.NM_ListenServer : ENetMode.NM_DedicatedServer)
                .Load(MatchLifecycleTests.SteeEStory, w =>
                {
                    MatchLifecycleTests.TwoSides(w);
                    w.Level.SpawnActor(new SObjective(), default, default);
                });
            Spy = World.Join("?Name=Spy?Team=0");
            Merc = World.Join("?Name=Merc?Team=1", local: localVictim);
            World.StartNetworkMatch(Spy);
            World.Run(3f);
            Merc.GotoState(new Name("s_PlayerWalkingSt2H"));
            Pawn = (SBasePawn)Merc.Pawn!;
            Pawn.SetPhysics(EPhysics.PHYS_None);
            World.Host.Calls.Clear();
        }

        public GameWorld World { get; }
        public SPlayerController Spy { get; }
        public SPlayerController Merc { get; }
        public SBasePawn Pawn { get; }

        public Actor Gas(bool sticky)
        {
            if (sticky)
            {
                var cam = World.Level.SpawnActor(new SStickyCamEmitter
                {
                    TimeBaseToCought = 1f, TimeBaseToFaint = 0.2f,
                }, Pawn.Location - new FVector(192f, 0f, 0f), default)!;
                cam.Instigator = Spy.Pawn;
                return cam;
            }

            var smoke = World.Level.SpawnActor(new SSmokeEmitter
            {
                RadiusEffect = 500f, TimeBaseToCought = 1f, TimeBaseToFaint = 0.2f,
            }, Pawn.Location, default)!;
            smoke.Instigator = Spy.Pawn;
            return smoke;
        }

        public void RemoteFall() => Merc.PCChangeServerState(Merc.MirrorStateAcknowledgementTime,
            new Name("s_FallDown"), new Name("DIE_InPlace"));

        public GameCall[] SleepCalls => World.Host.Named("ReceiveLocalizedMessage")
            .Where(m => m.Call.Int is K.MsgSmokeSleep or K.MsgStickyCameraSleep).Select(m => m.Call).ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoteGasFallPublishesOneAttributedSleepPerRecipient(bool sticky)
    {
        var rig = new Rig();
        rig.Gas(sticky);
        rig.World.Run(0.3f);
        rig.RemoteFall();
        rig.RemoteFall();

        Assert.Equal(2, rig.SleepCalls.Length);
        Assert.All(rig.SleepCalls, call =>
        {
            Assert.Equal(sticky ? K.MsgStickyCameraSleep : K.MsgSmokeSleep, call.Int);
            Assert.Same(rig.Spy.PlayerReplicationInfo, call.Pri1);
            Assert.Same(rig.Merc.PlayerReplicationInfo, call.Pri2);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalCoughPublishesOneAttributedSleep(bool sticky)
    {
        var rig = new Rig(localVictim: true);
        rig.Gas(sticky);
        for (int i = 0; i < 30; i++)
        {
            rig.World.World.Step();
            rig.Pawn.SetBase(rig.World.Level.Info);
            rig.Merc.CombatPlayerTick(Sim.DeltaTime);
        }

        Assert.Equal(2, rig.SleepCalls.Length);
        Assert.All(rig.SleepCalls, call => Assert.Equal(sticky ? K.MsgStickyCameraSleep : K.MsgSmokeSleep, call.Int));
    }

    [Theory]
    [InlineData("early")]
    [InlineData("stale")]
    [InlineData("mask")]
    [InlineData("dead")]
    [InlineData("melee")]
    [InlineData("wrong-ack")]
    public void UnconfirmedOrInterruptedExposureDoesNotPublishSleep(string interruption)
    {
        var rig = new Rig();
        Actor smoke = rig.Gas(sticky: false);
        rig.World.Run(interruption == "early" ? 0.1f : 0.3f);
        switch (interruption)
        {
            case "stale":
                smoke.Destroy();
                rig.World.Run(0.8f);
                break;
            case "mask":
                rig.Pawn.bGasMask = true;
                break;
            case "dead":
                rig.Pawn.Health = 0;
                break;
            case "melee":
                rig.Pawn.bIsCrouched = true;
                rig.Merc.Handle_ElbowDmg(new FVector(1f, 0f, 0f), rig.Spy.Pawn);
                break;
            case "wrong-ack":
                rig.Merc.PCChangeServerState(rig.Merc.MirrorStateAcknowledgementTime + 1f,
                    new Name("s_FallDown"), new Name("DIE_InPlace"));
                Assert.Empty(rig.SleepCalls);
                return;
        }

        rig.RemoteFall();
        Assert.Empty(rig.SleepCalls);
    }

    [Fact]
    public void WakingAllowsANewSleepButRequiresAFreshFaintDeadline()
    {
        var rig = new Rig();
        rig.Gas(sticky: false);
        rig.World.Run(0.3f);
        rig.RemoteFall();
        Assert.Equal(2, rig.SleepCalls.Length);
        rig.Pawn.bSleeping = rig.Pawn.bLyingOnGround = false;
        rig.Merc.GotoState(new Name("s_PlayerWalkingSt2H"));
        rig.World.Run(0.3f);
        rig.RemoteFall();
        Assert.Equal(4, rig.SleepCalls.Length);
    }

    [Fact]
    public void DelayedCommandClockDoesNotHideConfirmedServerExposure()
    {
        var rig = new Rig();
        rig.Gas(sticky: false);
        rig.World.Run(0.3f);
        using (rig.World.Level.CommandTime(60, 1f, Sim.DeltaTime)) rig.RemoteFall();
        Assert.Equal(2, rig.SleepCalls.Length);
    }

    [Fact]
    public void MaskInterruptsTheExposureEpisodeBeforeItIsRemoved()
    {
        var rig = new Rig();
        rig.Gas(sticky: false);
        rig.World.Run(0.3f);
        rig.Pawn.bGasMask = true;
        rig.World.Run(0.1f);
        rig.Pawn.bGasMask = false;
        rig.World.Run(0.1f);
        rig.RemoteFall();
        Assert.Empty(rig.SleepCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingGasCreditsTheSourceThatSetTheFaintDeadline(bool stickyFirst)
    {
        var rig = new Rig();
        Actor first = rig.Gas(stickyFirst);
        Actor second = rig.Gas(!stickyFirst);
        var smoke = (SSmokeEmitter)(stickyFirst ? second : first);
        smoke.TimeBaseToFaint = 1f;
        smoke.Instigator = rig.Pawn;
        rig.World.Run(0.3f);
        rig.RemoteFall();
        Assert.Equal(2, rig.SleepCalls.Length);
        Assert.All(rig.SleepCalls, call =>
        {
            Assert.Equal(K.MsgStickyCameraSleep, call.Int);
            Assert.Same(rig.Spy.PlayerReplicationInfo, call.Pri1);
        });
    }

    [Fact]
    public void StrikeAndGrabKnockoutsCarryAttackerThenVictim()
    {
        var strike = new Rig();
        strike.Pawn.bIsCrouched = true;
        strike.Merc.Handle_ElbowDmg(new FVector(1f, 0f, 0f), strike.Spy.Pawn);
        Assert.Equal(2, strike.World.Host.Messages(K.MsgStrikeKnockDown).Count);
        Assert.All(strike.World.Host.Messages(K.MsgStrikeKnockDown), m =>
        {
            Assert.Same(strike.Spy.PlayerReplicationInfo, m.Call.Pri1);
            Assert.Same(strike.Merc.PlayerReplicationInfo, m.Call.Pri2);
        });

        var grab = new Rig();
        ((SBasePawn)grab.Spy.Pawn!).m_AttackTarget = grab.Pawn;
        grab.Pawn.m_AttackedBy = (SBasePawn)grab.Spy.Pawn;
        grab.Spy.AskServerToUnGrabPawn(GrabReleaseMethode_.KO);
        Assert.Equal(2, grab.World.Host.Messages(K.MsgFallenUnconscious).Count);
        Assert.All(grab.World.Host.Messages(K.MsgFallenUnconscious), m =>
        {
            Assert.Same(grab.Spy.PlayerReplicationInfo, m.Call.Pri1);
            Assert.Same(grab.Merc.PlayerReplicationInfo, m.Call.Pri2);
        });
    }

    [Fact]
    public void LethalRemoteDropDoesNotAlsoAnnounceAKnockout()
    {
        var rig = new Rig();
        rig.Pawn.Health = 1;
        rig.Spy.TellServerForDropAttack(rig.Pawn);
        Assert.True(rig.Pawn.Health <= 0);
        Assert.Empty(rig.World.Host.Messages(K.MsgNeutralized));
    }
}
