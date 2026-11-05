using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Equipment;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

// Enhanced B5 SPlayerController: s_GrabbingPlayer, s_GrabBaseWithBlastDamageOnly,
// s_GrabFromLedgeBase.PawnTakeDamage and s_GrabBase.ExplosionHit.
public sealed class GrabInterruptionParityTests
{
    private static (SPlayerController Pc, SBasePawn Spy, SPlayerController HeldPc, SBasePawn Held, SBasePawn Shooter)
        Setup(string state)
    {
        var scene = new CombatScene();
        var (pc, spy) = scene.Player(true, new FVector(0, 0, 100));
        var (heldPc, held) = scene.Player(false, new FVector(70, 0, 100));
        var (_, shooter) = scene.Player(false, new FVector(300, 0, 100));
        scene.Run(1);
        // These are local owners: execute the paired native actions as well as the script states.
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        heldPc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pc.fSpawnProtectionEnd = 0;
        heldPc.fSpawnProtectionEnd = 0;
        spy.m_AttackTarget = held;
        held.m_AttackedBy = spy;
        heldPc.GotoState(new Name(state.Contains("Ledge") ? "s_GrabbedFromLedge_Begin" : "s_GrabbedByPlayer"));
        pc.GotoState(new Name(state));
        return (pc, spy, heldPc, held, shooter);
    }

    [Theory]
    [InlineData("s_GrabbingPlayer", 75)]
    [InlineData("s_GrabbingFromLedge_Begin", 75)]
    [InlineData("s_GrabbingFromLedge_Go", 75)]
    [InlineData("s_Grabbing_GotoPosition", 100)]
    [InlineData("s_GrabbingPlayer_RealBegin", 100)]
    public void NonlethalRifleHitPreservesTheGrabAndUsesThePhaseDamageGate(string state, int health)
    {
        var (pc, spy, _, held, shooter) = Setup(state);
        pc.PawnTakeDamage(25, shooter, spy.Location, new FVector(-1, 0, 0), SDamageType.SDTBase, 6, 70);
        Assert.Equal(health, spy.Health);
        Assert.Equal(new Name(state), pc.GetStateName());
        Assert.Same(held, spy.m_AttackTarget);
        Assert.Same(spy, held.m_AttackedBy);
        Assert.Equal(100, held.Health);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(40)]
    [InlineData(1)]
    public void LethalRifleHitOnHoldingSpyReleasesWithoutDamagingTheMerc(int health)
    {
        var (pc, spy, heldPc, held, shooter) = Setup("s_GrabbingPlayer");
        held.Health = health;
        pc.PawnTakeDamage(100, shooter, spy.Location, new FVector(-1, 0, 0), SDamageType.SDTBase, 6, 70);
        Assert.True(spy.Health <= 0);
        Assert.Equal(health, held.Health);
        Assert.Equal(new Name("s_ReleaseGrabbedPlayer_DMG"), pc.GetStateName());
        Assert.Equal(new Name("s_GrabbedByPlayer_Released_GrabbingDMG"), heldPc.GetStateName());
    }

    [Theory]
    [InlineData("s_GrabbingFromLedge_Begin")]
    [InlineData("s_GrabbingFromLedge_Go")]
    public void LethalRifleHitOnLedgeSpyStopsTheSpyWithoutChargingNormalGrabReleaseDamage(string state)
    {
        var (pc, spy, _, held, shooter) = Setup(state);
        pc.PawnTakeDamage(100, shooter, spy.Location, new FVector(-1, 0, 0), SDamageType.SDTBase, 6, 70);
        Assert.True(spy.Health <= 0);
        // The fixture is airborne; s_FallDropAttacked immediately enters its falling child.
        Assert.Equal(new Name("s_FallFallDropAttacked"), pc.GetStateName());
        Assert.Equal(100, held.Health);
    }

    [Theory]
    [InlineData("s_GrabbingPlayer")]
    [InlineData("s_GrabbingPlayer_RealBegin")]
    [InlineData("s_GrabbingFromLedge_Begin")]
    [InlineData("s_GrabbingFromLedge_Go")]
    public void NearbyExplosionDamagesAndBlastsTheSpyOutOfEveryGrabPhase(string state)
    {
        var (pc, spy, _, _, shooter) = Setup(state);
        pc.PawnTakeDamage(25, shooter, spy.Location, new FVector(100, 0, 0), SDamageType.SDamage_Explosion);
        // SDamage_Explosion is the lethal inner radius: the pipeline fixes it at 200 damage.
        Assert.True(spy.Health <= 0);
        Assert.Equal(new Name("s_Blast"), pc.GetStateName());
        Assert.Equal(EPhysics.PHYS_Falling, spy.Physics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InnerBlastSharesLethalDamageWithHeldMerc(bool mine)
    {
        var (pc, spy, heldPc, held, shooter) = Setup("s_GrabbingPlayer");
        // Exercise the same radius traversal and pair dispatch as mines and frag grenades.
        // Only the spy is geometrically inside this small radius; the held merc receives the shared hit.
        Actor source = mine
            ? shooter.XLevel.SpawnActor(new SMineProx(), spy.Location - new FVector(10, 0, 0), default)!
            : shooter.XLevel.SpawnActor(new SProjFrag(), spy.Location - new FVector(10, 0, 0), default)!;
        GadgetBlast.Run(new GadgetBlast.Blast(source, source.Location, source.Location,
            30, mine, 100, 100, 20, SDamageType.SDamage_Explosion, SDamageType.SDTBase,
            SDamageType.SDamage_Explosion, 70, 0, shooter, mine));
        Assert.True(spy.Health <= 0);
        Assert.True(held.Health <= 0);
        Assert.Equal(new Name("s_Blast"), pc.GetStateName());
        Assert.Equal(new Name("s_Blast"), heldPc.GetStateName());
        Assert.Null(held.m_AttackedBy);
    }

    [Theory]
    [InlineData("s_GrabbingPlayer", 0, true)]
    [InlineData("s_GrabbingFromLedge_Go", 100, false)]
    public void OuterBlastSharesDamageOnlyWhenPartnerIsWithinThirtyUnitsVertically(string state, int height, bool shared)
    {
        var (pc, spy, _, held, shooter) = Setup(state);
        held.Location += new FVector(0, 0, height);
        GadgetBlast.HurtPawn(spy, 25, shooter, spy.Location, new FVector(300, 0, 0), SDamageType.SDTBase, 70);
        Assert.True(spy.Health < 100);
        Assert.Equal(shared, held.Health < 100);
        Assert.Equal(new Name(state), pc.GetStateName());
    }
}
