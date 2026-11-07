using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

public sealed class BombInterruptionTests
{
    [Theory]
    [InlineData("s_PlaceBomb", false)]
    [InlineData("s_PlaceBomb_End", false)]
    [InlineData("s_PlaceBomb", true)]
    [InlineData("s_PlaceBomb_End", true)]
    public void Damage_or_a_teammates_taser_cannot_leave_an_armed_pistol_in_unarmed_movement(string state, bool taser)
    {
        var (rig, pistol) = ArmedSpyInBombState(state);
        RigMate teammate = rig.AddMate(PawnTeam.Spy, new FVector(500f, 500f, 0f), 0);

        rig.Controller.PawnTakeDamage(taser ? 0 : 1, teammate.Pawn, rig.Pawn.Location, default,
            taser ? SDamageType.SDTStickyShocker : SDamageType.SDTBase_NoAnim, taser ? -10 : 0);
        AssertRecovered(rig, pistol, taser);
    }

    [Theory]
    [InlineData("s_PlaceBomb")]
    [InlineData("s_PlaceBomb_End")]
    public void The_owners_shock_callback_holsters_the_actual_pistol_and_leaves_the_bomb_state(string state)
    {
        var (rig, pistol) = ArmedSpyInBombState(state);
        // This is the callback a remote owner receives, independently of the server's damage handler.
        rig.Controller.ClientStickyShock(-10);
        AssertRecovered(rig, pistol, taser: true);
    }

    [Fact]
    public void A_mirrored_exit_from_the_bomb_end_state_also_holsters_the_owners_weapon()
    {
        var (rig, pistol) = ArmedSpyInBombState("s_PlaceBomb_End");
        rig.Controller.GotoState(new Name("s_PlayerWalkingSt0H"));
        AssertRecovered(rig, pistol, taser: false);
    }

    private static (PawnRig Rig, SStickyShocker Pistol) ArmedSpyInBombState(string state)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        CombatEquipment.Install(rig.Level);
        SStickyShocker pistol = rig.Level.SpawnActor(new SStickyShocker(), rig.Pawn.Location, rig.Pawn.Rotation)!;
        pistol.GiveTo(rig.Pawn);
        rig.Hold(new PlayerInputFrame { bFireMode = 1 }, 6);
        rig.Hold(default, 90);
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.False(rig.Pawn.bWeaponTransit);
        var modem = rig.Level.SpawnActor(new SGadBomb { LastUsed = -100f }, rig.Pawn.Location, default)!;
        modem.GiveTo(rig.Pawn);
        rig.Controller.GotoState(new Name(state));
        Assert.True(rig.Controller.IsInState(new Name(state)));
        return (rig, pistol);
    }

    private static void AssertRecovered(PawnRig rig, SStickyShocker pistol, bool taser)
    {
        rig.Hold(default, 240);
        Assert.True(rig.Pawn.Health > 0);
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Assert.False(rig.Pawn.bWeaponTransit);
        Assert.False(pistol.bLaserOn);
        Assert.False(rig.Pawn.bStickyShocking);
        if (taser)
        {
            // A taser still applies its ordinary knockdown. At four seconds the seven-second lying timer
            // has not expired: the important immediate invariant is that the pistol already holstered.
            Assert.True(rig.Controller.IsInState(new Name("s_LyingOnGround")), rig.Controller.GetStateName().ToString());
            rig.Hold(default, (int)((rig.Controller.fLyingOnGroundDuration + 4f) * Sim.Hz));
        }
        Assert.True(rig.Controller.IsInState(new Name("s_PlayerWalkingSt0H")), rig.Controller.GetStateName().ToString());
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Assert.False(rig.Pawn.bWeaponTransit);
        Assert.False(pistol.bLaserOn);
    }
}
