using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Animation;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class OwnerLandingAndResultsTests
{
    private sealed class Clock : IClock { public long NowMicroseconds { get; set; } }

    [Fact]
    public void HeadJumpKnockoutLowersTheRifleAndRestoresAnOrdinaryJumpAfterWaking()
    {
        using var rig = new SessionRig(PlaySide.Merc);
        var pc = rig.Session.LocalController!;
        var source = rig.Session.ServerController!;
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn);
        var authority = Assert.IsAssignableFrom<SPAWNDEFENSE>(source.Pawn);
        var rifle = Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon);
        Assert.True(PawnActions.Clip(pawn, new Name("XxxxStAlAk2")).Frames > 2,
            "The head-jump recovery must run converted character clips with their authored notifies.");
        var mask = Assert.IsAssignableFrom<SGadGasMask>(PawnInventory.FindGadgetType(pawn, new ClassRef("SBase.SGadGasMask")));
        int charge = mask.Charge;
        Assert.Equal(2, pawn.WeaponLoaded);
        Assert.False(pawn.bGasMask);
        // State-entry fixture for the head-jump outcome. The collision/hit detector is not under test.
        source.eventMirrorState(new Name("s_FallDown"), new Name("DIE_DropAttacked"));
        bool lowered = false, hidden = false, layDown = false;
        var clips = new HashSet<Name>();
        for (int i = 0; i < 1050; i++)
        {
            rig.Step(1);
            lowered |= pawn.WeaponLoaded == 0;
            hidden |= !rifle.bDrawWeapon;
            layDown |= pc.IsInState("s_LyingOnGround");
            clips.Add(pawn.PawnAnim[0].Sequence);
            Assert.False(pawn.bGasMask);
            Assert.False(authority.bGasMask);
        }
        Assert.True(lowered, "The head-jump fall clip never lowered the rifle.");
        Assert.True(hidden, "The lowered first-person rifle never hid during the knockout.");
        Assert.True(layDown);
        Assert.Contains(new Name("XxxxStAlAk2"), clips);
        Assert.Equal(charge, mask.Charge);
        Assert.Equal(2, pawn.WeaponLoaded);
        Assert.Equal(2, authority.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
        Assert.True(pc.CheckIfCanFire());
        Assert.StartsWith("s_PlayerWalking", pc.GetStateName().ToString(), StringComparison.Ordinal);
        rig.Session.Input.Set("BT_Up", true);
        rig.Step(12);
        rig.Session.Input.Commands.Add(new InputCommand("Jump"));
        rig.Session.Input.Up = 280;
        bool airborne = false;
        var actions = new HashSet<int>();
        for (int i = 0; i < 150; i++)
        {
            rig.Step(1);
            rig.Session.Input.Up = 0;
            airborne |= pawn.Physics == EPhysics.PHYS_Falling;
            actions.Add(pawn.sAction);
        }
        Assert.True(airborne);
        Assert.DoesNotContain(SBasePawn.SA_LAND_ST_2H_HIGH, actions);
        Assert.DoesNotContain(SBasePawn.SA_LAND_ST_0H_HIGH, actions);
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryArmedMercJumpDoesNotChooseTheHighFallRecovery(bool moving)
    {
        using var rig = new SessionRig(PlaySide.Merc);
        var pc = rig.Session.LocalController!;
        var pawn = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        Assert.Equal(2, pawn.WeaponLoaded);
        if (moving) rig.Session.Input.Set("BT_Up", true);
        rig.Step(12);
        rig.Session.Input.Commands.Add(new InputCommand("Jump"));
        rig.Session.Input.Up = 280;
        bool airborne = false;
        float highest = pawn.Location.Z, lowest = pawn.Location.Z;
        var actions = new HashSet<int>();
        for (int i = 0; i < 150; i++)
        {
            rig.Step(1);
            rig.Session.Input.Up = 0;
            airborne |= pawn.Physics == EPhysics.PHYS_Falling;
            highest = Math.Max(highest, pawn.Location.Z);
            lowest = Math.Min(lowest, pawn.Location.Z);
            actions.Add(pawn.sAction);
        }
        Assert.True(airborne);
        Assert.InRange(highest - lowest, 65, 110);
        Assert.DoesNotContain(SBasePawn.SA_LAND_ST_2H_HIGH, actions);
        Assert.DoesNotContain(SBasePawn.SA_LAND_ST_0H_HIGH, actions);
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
        Assert.True(pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal),
            $"state={pc.GetStateName()} hands={pawn.WeaponLoaded} apex={pc.fZMaxFalling} position={pawn.Location}");
    }

    [Theory]
    [InlineData(PlaySide.Spy)]
    [InlineData(PlaySide.Merc)]
    public void RoundResultsStopPreviouslyHeldMovementOnOwnerAndAuthority(PlaySide side)
    {
        using var rig = new SessionRig(side);
        rig.Session.Input.Set("BT_Up", true);
        rig.Step(24);
        var pc = rig.Session.LocalController!;
        var source = rig.Session.ServerController!;
        Assert.True(pc.Pawn!.Velocity.Size2D() > 100);
        // The same server-to-owner transition used by SGameInfo.s_MatchOver; TestBox's
        // lightweight PlayLevel loader has no full game-rules actor.
        source.XLevel.GetService<IGameHost>()!.ClientCall(source, new GameCall("ClientGameEnded"));
        source.GameHasEnded();
        // The button remains held, as it can be when the result banner replaces gameplay.
        rig.Step(120);
        Assert.True(pc.bGameEnded);
        Assert.True(source.bGameEnded);
        Assert.Equal(FVector.Zero, source.Pawn!.Acceleration);
        Assert.InRange(source.Pawn.Velocity.Size2D(), 0, 1);
        Assert.Equal(FVector.Zero, pc.Pawn.Acceleration);
        Assert.InRange(pc.Pawn.Velocity.Size2D(), 0, 1);
        foreach (var body in new[] { (SBasePawn)source.Pawn, (SBasePawn)pc.Pawn })
        {
            // Run/forward/back/side use 4..8, and outgoing locomotion sets use 9..13.
            // Merc forward movement uses 5 even when the run overlay on 4 is inactive.
            for (int channel = 4; channel < 14; channel++)
                Assert.InRange(body.PawnAnim[channel].BlendAlpha, 0, 0.01f);
        }
    }

    private sealed class SessionRig : IDisposable
    {
        private readonly Clock _clock = new();
        public readonly PlaySession Session;
        public SessionRig(PlaySide side)
        {
            Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
            Session = new PlaySession(new PlaySettings
            {
                Map = "TestBox", Side = side, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
                SimLink = SimLinkProfile.Get("100"), MatchSeed = 119,
            }, _clock);
            Step(600);
        }
        public void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                Session.Step(_clock.NowMicroseconds);
                Session.Input.EndTick();
            }
        }
        public void Dispose() => Session.Dispose();
    }
}
