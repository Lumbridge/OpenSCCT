using System;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;
using CombatK = OpenScct.Game.Combat.K;

namespace OpenScct.Integration.Tests;

public sealed class ChargeKnockbackNetworkTests
{
    private const int Spy = 0;
    private const int Teammate = 1;
    private const int Merc = 2;

    private sealed class IdleBrain : BotBrain
    {
        public BotInput Input { get; set; }
        public override BotInput Think(BotContext context) => Input;
    }

    [Theory]
    [InlineData(NetProfile.Enhanced, false)]
    [InlineData(NetProfile.Enhanced, true)]
    [InlineData(NetProfile.Stock, false)]
    [InlineData(NetProfile.Stock, true)]
    public void SpyLiesDownAtTheSameSpotOnClientAndServerAfterAMercCharge(NetProfile profile, bool moving)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindCharacterContent() is null, "Charge knockback requires the converted root-motion clip.");
        using var rig = new MatchRig(profile);
        FVector start = rig.KnockDownSpy(moving);
        SBasePawn local = PawnOf(rig.Owner(Spy)), remote = PawnOf(rig.Authority(Spy));
        Assert.True(local.Location.X - start.X > 200f, $"The charge must displace the Spy. Start {start}, owner {local.Location}.");
        rig.WaitUntil(() => FVector.Dist(local.Location, remote.Location) < 3f, Sim.Hz / 2);
        Assert.True(local.bSleeping && remote.bSleeping);
        Assert.True(FVector.Dist(local.Location, remote.Location) < 3f,
            $"Owner landed at {local.Location} (yaw {local.SpecialYaw}), authority at {remote.Location} (yaw {remote.SpecialYaw}).");

        rig.WaitForSpyToWalk();
        Assert.True(FVector.Dist(local.Location, remote.Location) < 3f,
            $"Owner woke up at {local.Location}, authority at {remote.Location}.");
        Assert.Equal(EClientState.Joined, rig.Match.Bots[Spy].Session.State);
    }

    [Fact]
    public void ServerRejectsAnEarlyStandUpAndStillWakesTheSpyOnTime()
    {
        using var rig = new MatchRig();
        rig.KnockDownSpy();
        SPlayerController owner = rig.Owner(Spy), authority = rig.Authority(Spy);
        float beganLying = authority.fLyingOnGroundTime;
        Assert.Equal(authority.MirrorStateAcknowledgementTime, owner.MirrorStateAcknowledgementTime);

        // A modified owner can send both the transition and its animation action.
        owner.eventChangeState("s_StandUp");
        rig.Tick(40);
        Assert.True(authority.IsInState("s_LyingOnGround"));
        Assert.True(PawnOf(authority).bSleeping);

        rig.WaitUntil(() => authority.IsInState("s_StandUp"), 5 * Sim.Hz);
        Assert.True(authority.IsInState("s_StandUp"));
        Assert.InRange(authority.Level.TimeSeconds - beganLying, 2.95f, 3.1f);
        rig.WaitForSpyToWalk();
    }

    [Fact]
    public void ServerRejectsADistantReviveAndAcceptsANearbyTeammate()
    {
        using var rig = new MatchRig();
        rig.KnockDownSpy();
        SPlayerController authority = rig.Authority(Spy), mate = rig.Owner(Teammate);
        SBasePawn victim = PawnOf(authority);
        SBasePawn proxy = rig.Proxy(Teammate, Spy);
        Assert.True(FVector.Dist(PawnOf(rig.Authority(Teammate)).Location, victim.Location) > 250f);
        mate.AskServerToReanimatePawn(proxy);
        rig.Tick(40);
        Assert.True(authority.IsInState("s_LyingOnGround"));
        Assert.True(victim.bSleeping);

        rig.Place(Teammate, victim.Location.X - 110f, victim.Location.Y);
        rig.Tick(30);
        mate.AskServerToReanimatePawn(proxy);
        rig.WaitUntil(() => authority.IsInState("s_Reanimating"), Sim.Hz);
        Assert.True(authority.IsInState("s_Reanimating"), $"Authority is in {authority.GetStateName()}.");
        rig.WaitForSpyToWalk();
    }

    [Fact]
    public void DownedSpyCannotFireTheShockerLocallyOrThroughTheServer()
    {
        using var rig = new MatchRig();
        rig.KnockDownSpy();
        var owner = rig.Owner(Spy);
        var local = Assert.IsAssignableFrom<SPawnAttaque>(owner.Pawn);
        var remote = PawnOf(rig.Authority(Spy));
        var pistol = Assert.IsType<SStickyShocker>(local.MyWeapon);
        var serverPistol = Assert.IsType<SStickyShocker>(remote.MyWeapon);
        int muzzleCount = serverPistol.MuzzleCount;

        local.WeaponLoaded = 2;
        local.bWeaponTransit = false;
        float energy = local.EnergyLevel;
        pistol.Fire(1f);
        Assert.Equal(energy, local.EnergyLevel);

        CombatServices.Net(pistol).ServerCall(owner, new CombatCall("ServerFireWeaponWithLocation")
        {
            Target = pistol, Int1 = 1, Vector1 = local.Location, Rotation = local.Rotation,
        });
        rig.Tick(40);
        Assert.Equal(muzzleCount, serverPistol.MuzzleCount);

        rig.WaitForSpyToWalk();
        local.WeaponLoaded = 2;
        local.bWeaponTransit = false;
        pistol.Fire(1f);
        rig.WaitUntil(() => serverPistol.MuzzleCount > muzzleCount, Sim.Hz);
        Assert.Equal(muzzleCount + 1, serverPistol.MuzzleCount);
    }

    [Theory]
    [InlineData(NetProfile.Enhanced)]
    [InlineData(NetProfile.Stock)]
    public void ShockStopsAnActiveChargeAndRejectsAHitFromTheUnawareOwner(NetProfile profile)
    {
        using var rig = new MatchRig(profile);
        rig.Place(Spy, 600f);
        rig.Place(Merc, 300f);
        rig.Tick(120);
        SPlayerController owner = rig.Owner(Merc), authority = rig.Authority(Merc);
        SBasePawn local = PawnOf(owner), remote = PawnOf(authority), victim = PawnOf(rig.Authority(Spy));
        SBasePawn spyProxy = rig.Proxy(Merc, Spy);
        owner.eventChangeState("s_PlayerCharging");
        rig.WaitUntil(() => authority.bIsCharging && remote.Velocity.Size2D() > 100f, Sim.Hz / 2);
        Assert.True(authority.bIsCharging);
        Assert.True(remote.Velocity.Size2D() > 100f);
        Assert.InRange(FVector.Dist(victim.Location, remote.Location), 80f, CombatK.ElbowServerReach - 1f);

        int health = victim.Health;
        FVector stoppedAt = remote.Location;
        authority.TimeShock = -100f;
        authority.PawnTakeDamage(0, victim, stoppedAt, default, SDamageType.SDTStickyShocker, 2);
        Assert.False(authority.bIsCharging);
        Assert.Equal(0f, remote.Velocity.Size2D());

        Assert.True(owner.bIsCharging);
        owner.eventNotifyBump(spyProxy);
        Assert.True(owner.IsInState("s_PlayerCharging_HitEnemy"));
        rig.WaitUntil(() => local.bStickyShocking, Sim.Hz);
        Assert.True(local.bStickyShocking);
        Assert.False(owner.bIsCharging);
        Assert.Equal(0f, local.Velocity.Size2D());
        rig.Tick(30);
        Assert.Equal(health, victim.Health);
        Assert.False(rig.Authority(Spy).IsInState("s_Charge_GoBackward"));
        Assert.InRange(FVector.Dist(remote.Location, stoppedAt), 0f, 2f);
    }

    private static SBasePawn PawnOf(SPlayerController controller) => Assert.IsAssignableFrom<SBasePawn>(controller.Pawn);

    private sealed class MatchRig : IDisposable
    {
        private readonly VirtualClock _clock = new();
        private readonly IdleBrain _spy = new();
        private long _frame;

        public BotMatch Match { get; }

        public MatchRig(NetProfile profile = NetProfile.Enhanced)
        {
            var network = new SimLinkNetwork(_clock, 973, SimLinkProfile.Ping200);
            Match = new BotMatch(new BotMatchSettings
            {
                Map = MapCatalog.TestBox,
                Maps = WalkerRig.Maps,
                Classes = WalkerRig.Classes,
                Bots = BotMatchSettings.TwoVersusTwo(i => i == Spy ? _spy : new IdleBrain(), _ => new IdleBrain()),
                Profile = profile,
            }, network.Bind, "server", 0);
            Tick(900);
        }

        public SPlayerController Owner(int index) => Assert.IsType<SPlayerController>(Match.Bots[index].Controller);
        public SPlayerController Authority(int index) => Assert.IsType<SPlayerController>(Match.ServerController(Match.Bots[index]));
        public SBasePawn Proxy(int viewer, int subject) => Assert.IsAssignableFrom<SBasePawn>(
            Match.Bots[viewer].Session.Driver.FindActorByNetGuid((uint)PawnOf(Authority(subject)).ActorId));

        public void Place(int index, float x, float y = 300f)
        {
            foreach (SPlayerController controller in new[] { Owner(index), Authority(index) })
            {
                SBasePawn pawn = PawnOf(controller);
                Assert.True(pawn.SetLocation(new FVector(x, y, pawn.Location.Z)));
                pawn.Velocity = pawn.Acceleration = default;
                pawn.Rotation = default;
                controller.SetViewRotation(default);
            }
        }

        public FVector KnockDownSpy(bool moving = false)
        {
            // Facing west toward the Merc puts the knockback yaw beyond the old scaled vector's range.
            Place(Spy, moving ? 500f : 600f);
            Place(Merc, moving ? 360f : 460f);
            Tick(120);
            SPlayerController owner = Owner(Spy), authority = Authority(Spy), attacker = Authority(Merc);
            SBasePawn local = PawnOf(owner), remote = PawnOf(authority), merc = PawnOf(attacker);
            if (moving)
            {
                _spy.Input = new BotInput { BT_Up = 1, View = default(FRotator), FastSpeed = true };
                Tick(20);
                Assert.True(FVector.Dist(local.Location, remote.Location) > 5f);
            }

            FVector start = remote.Location;
            attacker.DamagePawn_C2S(remote, 35, merc, start, start - merc.Location, SDamageType.SDamage_Charged, 0, 100);
            WaitUntil(() => owner.IsInState("s_Charge_GoBackward"), Sim.Hz);
            Assert.True(owner.IsInState("s_Charge_GoBackward"));
            Assert.True(FVector.Dist(local.vLocationBeforeRM, start) < 2f,
                $"Owner anchored root motion at {local.vLocationBeforeRM}, expected {start}. Pawn at {local.Location}, velocity {local.Velocity}.");
            _spy.Input = BotInput.Idle;
            WaitUntil(() => local.bLyingOnGround && remote.bLyingOnGround, 4 * Sim.Hz);
            Assert.True(local.bLyingOnGround && remote.bLyingOnGround,
                $"Owner {owner.GetStateName()} at {local.Location}, authority {authority.GetStateName()} at {remote.Location}.");
            return start;
        }

        public void WaitForSpyToWalk()
        {
            SPlayerController owner = Owner(Spy), authority = Authority(Spy);
            WaitUntil(() => owner.StateNode is SPlayerController.s_PlayerWalking && authority.StateNode is SPlayerController.s_PlayerWalking, 10 * Sim.Hz);
            Assert.IsAssignableFrom<SPlayerController.s_PlayerWalking>(owner.StateNode);
            Assert.IsAssignableFrom<SPlayerController.s_PlayerWalking>(authority.StateNode);
        }

        public void WaitUntil(Func<bool> condition, int frames)
        {
            for (int i = 0; i < frames && !condition(); i++) Tick(1);
        }

        public void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_frame, Sim.Hz));
                Match.Step(_clock.NowMicroseconds);
            }
        }

        public void Dispose() => Match.Dispose();
    }
}
