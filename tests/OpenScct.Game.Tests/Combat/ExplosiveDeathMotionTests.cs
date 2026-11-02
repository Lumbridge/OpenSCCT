using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

public sealed class ExplosiveDeathMotionTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    public void MovingMercKilledByExplosiveContainerStopsLocomotionDuringDeathClip(float blastX, float blastY)
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Hold(Forward, 30);
        Assert.True(rig.Pawn.Velocity.Size2D() > 100f);
        Assert.False(rig.Pawn.Acceleration.IsZero());
        FVector at = rig.Pawn.Location;
        SGamePlayObject container = Detonate(rig, new FVector(blastX, blastY, 0f), SDamageType.SDamage_Charged);

        Assert.True(container.bAlreadyDestroyed);
        Assert.True(rig.Pawn.Health <= 0);
        Assert.True(rig.Controller.IsInState("s_Dead"));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.True(rig.Pawn.bSleeping);
        for (int tick = 0; tick < 45; tick++) rig.Step(default);
        Assert.InRange((rig.Pawn.Location - at).Size2D(), 0f, .01f);
    }

    [Theory]
    [InlineData(PawnTeam.Merc, -1f)]
    [InlineData(PawnTeam.Spy, 1f)]
    public void ContainerDeathKeepsAnimationRootMotion(PawnTeam team, float blastY)
    {
        PawnRig rig = Rig(team);
        rig.Hold(Forward, 30);

        Detonate(rig, new FVector(0f, blastY, 0f), SDamageType.SDamage_Charged);

        Assert.True(rig.Controller.IsInState("s_Dead"));
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
        Name sequence = rig.Pawn.PawnAnim[0].Sequence;
        float frame = rig.Pawn.PawnAnim[0].Frame;
        for (int tick = 0; tick < 3; tick++) rig.Step(default);
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
        Assert.Equal(sequence, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].Frame > frame);
    }

    [Fact]
    public void LethalBlastKeepsItsFlightAndLandingRootMotion()
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Hold(Forward, 30);
        FVector at = rig.Pawn.Location;

        Detonate(rig, new FVector(1f, 0f, 0f), SDamageType.SDamage_Explosion);

        Assert.True(rig.Pawn.Health <= 0);
        Assert.True(rig.Controller.IsInState("s_Blast"));
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.Physics);
        Assert.True(rig.Pawn.Velocity.X > 0f);
        for (int tick = 0; tick < 10; tick++) rig.Step(default);
        Assert.True(rig.Pawn.Location.X > at.X + 20f);
        Assert.True(rig.Pawn.Location.Z > at.Z);
        for (int tick = 0; tick < 180 && !rig.Controller.IsInState("s_Dead"); tick++) rig.Step(default);
        Assert.True(rig.Controller.IsInState("s_Dead"));
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
    }

    private static SGamePlayObject Detonate(PawnRig rig, FVector direction, SDamageType damageType)
    {
        SGamePlayObject container = rig.Level.SpawnActor(new SGamePlayObject
        {
            bNoDelete = false, bDamageable = true, HitPoints = 1, bCanDamageActor = true,
            DamageActorRange = 500, DamageActorPoints = 600, DamageActorType = damageType,
        }, rig.Pawn.Location - direction * 120f, default)!;
        container.eventTakeDamage(1, null, container.Location, default, SDamageType.SDTBase);
        return container;
    }
}
