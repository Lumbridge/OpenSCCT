using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Animation;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class DeathPresentationNetworkTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void AirborneDeathConsumesTheWholeLandingRootMotionWhenHealthArrivesAfterItsState(int victimIndex, bool delayHealth)
    {
        string? folder = WalkerRig.Maps.FindFolder(MapCatalog.TestBox);
        Assert.SkipWhen(folder is null, "TestBox content is required.");
        string? chars = PlayMapLoader.FindCharsDir(folder!);
        Assert.SkipWhen(chars is null, "Converted character animations are required.");
        var animations = PawnAnimationSource.FromContent(chars!);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 2931, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=2?LivesDEF=2?TimesRespawnsATT=10",
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            match.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 900; i++) Tick();
        void Install(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        Install(match.Server.World.Level);
        foreach (BotClient bot in match.Bots) Install(bot.Session.World.Level);
        BotClient victim = match.Bots[victimIndex];
        SPlayerController authority = match.ServerController(victim)!;
        SPlayerController owner = victim.Controller!;
        var source = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var own = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        Assert.True(PawnActions.Clip(source, new Name("LandStXxBk2")).Frames > 2);
        bool jumped = false, hit = false, heldLandingHealth = false;
        long deathStateFrame = 0;
        match.Server.BeforeWorldTick += _ =>
        {
            if (hit || source.Physics != EPhysics.PHYS_Falling || source.Velocity.Z <= 0) return;
            hit = true;
            authority.PawnTakeDamage(1000, null, source.Location, default, SDamageType.SDTBase);
        };
        victim.Session.BeforeWorldTick += _ =>
        {
            if (!jumped) { jumped = true; owner.eventJump(); }
            if (!hit || !delayHealth) return;
            // Explicit packet-order fixture: the mirrored inert-death state can precede the pawn's
            // Health property. Hold only that property stale through the first half-second of landing, leaving every state,
            // action, animation, movement packet and callback on the connected production path.
            if (deathStateFrame == 0 && owner.StateNode is SPlayerController.s_LandingInerte)
                deathStateFrame = frame;
            if (deathStateFrame == 0) return;
            bool held = frame < deathStateFrame + 30;
            own.Health = held ? 100 : source.Health;
            heldLandingHealth |= held && owner.StateNode is SPlayerController.s_LandingInerte;
        };
        var states = new HashSet<string>();
        for (int i = 0; i < 300; i++) { Tick(); states.Add($"{owner.GetStateName()}/{own.Physics}/{own.PawnAnim[0].Sequence}"); }
        Assert.True(hit, "The authority never received the ordinary jump before damage.");
        Assert.True(delayHealth == heldLandingHealth, $"delay={delayHealth} health={own.Health} states={string.Join(',', states)} source={authority.GetStateName()}");
        Assert.True(source.Health <= 0 && own.Health <= 0);
        Assert.False(match.Server.Game!.bGameEnded);
        Assert.True(owner.StateNode is SPlayerController.s_Dead);
        Assert.Equal(source.PawnAnim[0].Sequence, own.PawnAnim[0].Sequence);
        Assert.InRange((source.Location - own.Location).Size(), 0, 3);
    }

    private sealed class IdleBrain : BotBrain
    {
        public bool CrouchPressed;
        public override BotInput Think(BotContext context)
        {
            var input = new BotInput { bCrouch = (byte)(CrouchPressed ? 1 : 0) };
            CrouchPressed = false;
            return input;
        }
    }

    public static IEnumerable<object[]> CorpseCases()
    {
        for (int victim = 0; victim < 2; victim++)
            foreach (bool crouched in new[] { false, true })
            {
                for (int direction = 0; direction < 5; direction++)
                    yield return [victim, 1, crouched, direction];
                yield return [victim, 2, crouched, 0];
            }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SameTickFinalLifeDamagePublishesOneOriginalOrderedOutcomeToBothPlayers(int firstVictim)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 2801, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=1?LivesDEF=1",
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            match.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 900; i++) Tick();
        SPlayerController first = match.ServerController(match.Bots[firstVictim])!;
        SPlayerController second = match.ServerController(match.Bots[1 - firstVictim])!;
        SBasePawn firstBody = Assert.IsAssignableFrom<SBasePawn>(first.Pawn);
        SBasePawn secondBody = Assert.IsAssignableFrom<SBasePawn>(second.Pawn);
        int survivingHealth = secondBody.Health;
        bool hit = false;
        match.Server.BeforeWorldTick += _ =>
        {
            if (hit) return;
            hit = true;
            // Both ordinary damage deliveries occur in one authority tick. Original Killed immediately
            // checks team lives and enters GameEnded, whose damage handler ignores subsequent hits.
            // Preserve that order instead of inventing a draw or accepting another death after results.
            first.PawnTakeDamage(1000, secondBody, firstBody.Location, default, SDamageType.SDTBase);
            second.PawnTakeDamage(1000, firstBody, secondBody.Location, default, SDamageType.SDTBase);
        };
        for (int i = 0; i < 180; i++) Tick();
        Assert.True(match.Server.Game!.bGameEnded);
        Assert.Equal(1 - firstVictim, match.Server.Game.GameReplicationInfo!.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, match.Server.Game.GameReplicationInfo.WinningReason);
        Assert.True(firstBody.Health <= 0);
        Assert.Equal(0, first.PlayerReplicationInfo!.NumLives);
        Assert.Equal(1, first.PlayerReplicationInfo.Deaths);
        Assert.Equal(survivingHealth, secondBody.Health);
        Assert.Equal(1, second.PlayerReplicationInfo!.NumLives);
        Assert.Equal(0, second.PlayerReplicationInfo.Deaths);
        Assert.Equal(1, second.PlayerReplicationInfo.Kills);
        foreach (BotClient client in match.Bots)
        {
            SPlayerController owner = client.Controller!;
            Assert.True(owner.bGameEnded);
            Assert.Equal("GameEnded", owner.GetStateName().ToString());
            Assert.Equal(1 - firstVictim, owner.GameReplicationInfo!.WinningTeam);
            Assert.Equal((byte)EEndReason.END_TEAMDEATH, owner.GameReplicationInfo.WinningReason);
            Assert.Equal(EClientState.Joined, client.Session.State);
        }
    }

    [Theory]
    [MemberData(nameof(CorpseCases))]
    public void LethalDamagePlaysTheAuthoredCorpseClipOnAuthorityOwnerAndObserver(int victimIndex, int lives, bool crouched, int direction)
    {
        string? folder = WalkerRig.Maps.FindFolder(MapCatalog.TestBox);
        Assert.SkipWhen(folder is null, "TestBox content is required.");
        string? chars = PlayMapLoader.FindCharsDir(folder!);
        Assert.SkipWhen(chars is null, "Converted character animations are required.");
        var animations = PawnAnimationSource.FromContent(chars!);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 2701, SimLinkProfile.Get("100"));
        var brains = new[] { new IdleBrain(), new IdleBrain() };
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = $"?GameMode=5?TimeLimit=0?LivesATT={lives}?LivesDEF={lives}?TimesRespawnsATT=10",
            Bots = [new("Spy", BotTeam.Spy, () => brains[0]), new("Merc", BotTeam.Merc, () => brains[1])],
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            match.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 900; i++) Tick();
        void Install(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        Install(match.Server.World.Level);
        foreach (BotClient bot in match.Bots) Install(bot.Session.World.Level);
        BotClient owner = match.Bots[victimIndex], viewer = match.Bots[1 - victimIndex];
        var pc = match.ServerController(owner)!;
        var body = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        if (body.bIsCrouched != crouched)
        {
            brains[victimIndex].CrouchPressed = true;
            for (int i = 0; i < 120; i++) Tick();
        }
        Assert.Equal(crouched, body.bIsCrouched);
        Assert.Equal(crouched, Assert.IsAssignableFrom<SBasePawn>(owner.Controller!.Pawn).bIsCrouched);
        Name[] authored = body.SPawnType == PawnType.SPAWNDEFENSE
            ? crouched
                ? [body.ADeathCrDown2H, body.ADeathCrForward2H, body.ADeathCrBack2H, body.ADeathCrLeft2H, body.ADeathCrRight2H]
                : [body.ADeathStDown2H, body.ADeathStForward2H, body.ADeathStBack2H, body.ADeathStLeft2H, body.ADeathStRight2H]
            : crouched
                ? [body.ADeathCrDown, body.ADeathCrForward, body.ADeathCrBack, body.ADeathCrLeft, body.ADeathCrRight]
                : [body.ADeathStDown, body.ADeathStForward, body.ADeathStBack, body.ADeathStLeft, body.ADeathStRight];
        Name expected = authored[direction];
        body.Rotation.GetAxes(out FVector forward, out FVector right, out _);
        FVector momentum = direction switch { 1 => forward, 2 => -forward, 3 => -right, 4 => right, _ => default };
        EDeathDirection selected = CombatNatives.ChooseDeathAnimation(body, momentum);
        Assert.Equal((EDeathDirection)direction, selected);
        Assert.True(animations.TryGetClip(body, expected, out PawnClip clip) && clip.Frames > 2);
        var sourceClips = new HashSet<Name>();
        var ownerClips = new HashSet<Name>();
        var viewerClips = new HashSet<Name>();
        bool hit = false;
        match.Server.BeforeWorldTick += _ =>
        {
            if (hit) return;
            hit = true;
            // Normal authoritative damage path; directional momentum selects each authored death.
            pc.PawnTakeDamage(1000, null, body.Location, momentum, SDamageType.SDTBase);
        };
        for (int i = 0; i < 300; i++)
        {
            Tick();
            sourceClips.Add(body.PawnAnim[0].Sequence);
            if (owner.Controller?.Pawn is SBasePawn own) ownerClips.Add(own.PawnAnim[0].Sequence);
            if (viewer.Session.Driver.FindActorByNetGuid((uint)body.ActorId) is SBasePawn seen)
                viewerClips.Add(seen.PawnAnim[0].Sequence);
        }
        Assert.True(body.Health <= 0);
        Assert.Equal(lives - 1, pc.PlayerReplicationInfo!.NumLives);
        Assert.Equal(lives == 1, match.Server.Game!.bGameEnded);
        Assert.True(sourceClips.Contains(expected), $"Authority missing {expected}; saw {string.Join(',', sourceClips)}; owner {string.Join(',', ownerClips)}; observer {string.Join(',', viewerClips)}.");
        Assert.True(ownerClips.Contains(expected), $"Owner missing {expected}; saw {string.Join(',', ownerClips)}.");
        Assert.True(viewerClips.Contains(expected), $"Observer missing {expected}; saw {string.Join(',', viewerClips)}.");
    }
}
