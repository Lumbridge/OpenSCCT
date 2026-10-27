using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Game.Tests.Movement;
using Xunit;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The spy's flash-bang going off (spec/equipment/flash-bang.md): the mercs in range who face it and see it are
/// flashed (a zero-damage SDTFlash hit that gives their HUD the flash), every player hears it, the SFlashBang flare
/// and its light are left; smoke and flash grenades go off when they touch a pawn other than the thrower.
/// </summary>
public sealed class FlashBangTests
{
    private static readonly FVector At = new(0f, 0f, 100f);

    private static int Flashes(CombatScene Scene, SPlayerController PC) =>
        Scene.World.Events.Current.ToArray().Count(e => e.Kind == SimEventKind.Notify && e.What == CombatFx.ClientDamageHudEffect
            && e.ActorId == PC.ActorId && e.Slot == (int)SDamageType.SDTFlash);

    /// <summary>A local player as a login makes him: the controller's SPawnType is his side's.</summary>
    private static (SPlayerController PC, SBasePawn Pawn) Player(CombatScene Scene, bool Spy, FVector Where, int Yaw = 0)
    {
        (SPlayerController pc, SBasePawn pawn) = GadgetKit.Local(Scene, Spy, Where, Yaw);
        pc.SPawnType = Spy ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE;
        return (pc, pawn);
    }

    private static SProjFlashBang Grenade(CombatScene Scene, FVector Where, Pawn? Thrower = null)
    {
        SProjFlashBang flash = Scene.Level.SpawnActor(new SProjFlashBang(), Where, default)!;
        if (Thrower is not null)
        {
            flash.SetOwner(Thrower);
            flash.Instigator = Thrower;
        }

        return flash;
    }

    [Fact]
    public void TheDefaultsAreTheOriginals()
    {
        var flash = new SProjFlashBang();
        Assert.Equal(2500, flash.Range);
        Assert.Equal(3, flash.TempsExplosion);
        Assert.Equal(new AssetRef("Armes_impacts.Grenade.GR_FlashBang_explo"), flash.SoundExplode);
        Assert.Equal(5, new SGadFlashBang().Charge);
        Assert.True(new SGadFlashBang().CanQuickLaunch);
        Assert.Equal(0f, new SPlayerController().DES_fSeuilFlashBang_Cos);
    }

    [Fact]
    public void AMercFacingItWithinRangeIsFlashedWithoutDamage()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = Player(scene, false, new FVector(-600f, 0f, 100f));
        int health = merc.Health;
        Grenade(scene, At).Explode(At, FVector.Zero);
        Assert.Equal(1, Flashes(scene, pc));
        Assert.Equal(health, merc.Health);
    }

    [Fact]
    public void AMercWithItBehindHimIsNotFlashed()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = Player(scene, false, new FVector(-600f, 0f, 100f), 32768);
        Grenade(scene, At).Explode(At, FVector.Zero);
        Assert.Equal(0, Flashes(scene, pc));
    }

    [Fact]
    public void AMercOutOfRangeIsNotFlashed()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController near, _) = Player(scene, false, new FVector(-2490f, 0f, 100f));
        (SPlayerController far, _) = Player(scene, false, new FVector(-2510f, 0f, 100f));
        Grenade(scene, At).Explode(At, FVector.Zero);
        Assert.Equal(1, Flashes(scene, near));
        Assert.Equal(0, Flashes(scene, far));
    }

    [Fact]
    public void ASpyFacingItIsNotFlashed()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController spy, _) = Player(scene, true, new FVector(-600f, 0f, 100f));
        Grenade(scene, At).Explode(At, FVector.Zero);
        Assert.Equal(0, Flashes(scene, spy));
    }

    [Fact]
    public void AWallBetweenHidesTheFlash()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(-300f));
        (SPlayerController pc, _) = Player(scene, false, new FVector(-600f, 0f, 100f));
        Grenade(scene, At).Explode(At, FVector.Zero);
        Assert.Equal(0, Flashes(scene, pc));
    }

    [Fact]
    public void ItBangsLeavesTheFlareWithItsLightAndGoes()
    {
        CombatScene scene = GadgetKit.Scene();
        SProjFlashBang flash = Grenade(scene, At);
        flash.Explode(At, FVector.Zero);
        Assert.True(flash.bDeleteMe);
        Assert.Contains(scene.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound
            && e.What == new Name(flash.SoundExplode.Path));
        SFlashBang flare = Assert.Single(GadgetKit.All<SFlashBang>(scene));
        Assert.Equal(At, flare.Location);
        Assert.Single(GadgetKit.All<SFlashLight>(scene));
    }

    [Fact]
    public void AFlashBangTouchingAMercGoesOffAHundredUnitsBackAlongItsFlight()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = Player(scene, true, new FVector(-1000f, 0f, 100f));
        (SPlayerController pc, SBasePawn merc) = Player(scene, false, new FVector(300f, 0f, 100f), 32768);
        SProjFlashBang flash = Grenade(scene, new FVector(290f, 0f, 120f), spy);
        flash.Velocity = new FVector(800f, 0f, 0f);
        flash.eventTouch(merc);
        Assert.True(flash.bDeleteMe);
        Assert.False(flash.bBounce);
        Assert.Equal(1, Flashes(scene, pc));
        SFlashBang flare = Assert.Single(GadgetKit.All<SFlashBang>(scene));
        Assert.Equal(190f, flare.Location.X, 2f);
    }

    [Fact]
    public void TouchingItsThrowerOrAnotherActorDoesNotSetItOff()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = Player(scene, true, new FVector(-1000f, 0f, 100f));
        SProjFlashBang flash = Grenade(scene, At, spy);
        flash.eventTouch(spy);
        Assert.False(flash.bDeleteMe);
        Assert.False(flash.bAlreadyRebounded);
        flash.eventTouch(scene.Level.SpawnActor(new SFlashLight(), At, default)!);
        Assert.False(flash.bDeleteMe);
        Assert.True(flash.bAlreadyRebounded);
    }

    [Fact]
    public void ASmokeGrenadeTouchingAPawnLetsTheCloudOutWhereItIs()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = Player(scene, true, new FVector(-1000f, 0f, 100f));
        (_, SBasePawn merc) = Player(scene, false, new FVector(300f, 0f, 100f));
        SProjSmoke smoke = scene.Level.SpawnActor(new SProjSmoke(), new FVector(290f, 0f, 120f), default)!;
        smoke.SetOwner(spy);
        smoke.eventTouch(merc);
        Assert.True(smoke.Exploded);
        SSmokeEmitter cloud = Assert.Single(GadgetKit.All<SSmokeEmitter>(scene));
        Assert.Equal(290f, cloud.Location.X, 1f);
    }

    [Fact]
    public void AClientCopyTouchingAPawnJustGoes()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = Player(scene, true, new FVector(-1000f, 0f, 100f));
        (SPlayerController pc, SBasePawn merc) = Player(scene, false, new FVector(-600f, 0f, 100f));
        SProjFlashBang copy = Grenade(scene, At, spy);
        copy.Role = ENetRole.ROLE_SimulatedProxy;
        copy.eventTouch(merc);
        Assert.True(copy.bDeleteMe);
        Assert.Empty(GadgetKit.All<SFlashBang>(scene));
        Assert.Equal(0, Flashes(scene, pc));
    }

    /// <summary>The report: a quick-launched flash-bang popped on the floor with no flash.</summary>
    [Fact]
    public void AQuickFlashBangFlashesAMercInFrontOfTheSpy()
    {
        PawnRig rig = MovementTestKit.Rig(PawnTeam.Spy);
        CombatEquipment.Install(rig.Level);
        Loadouts.UpdateInventory(rig.Pawn, EGameMode.GM_Neutralization);
        var still = new PlayerInputFrame();
        rig.Hold(still, (int)(K.QuickLaunchCooldown * Sim.Hz) + 10);
        SBasePawn spy = rig.Pawn;
        FVector ahead = spy.Location + (spy.Rotation.Vector() * 500f);
        SBasePawn merc = rig.Level.SpawnActor<SBasePawn>(new SPawnDefense_1Mesh(), ahead, (spy.Rotation.Vector() * -1f).Rotation())!;
        SPlayerController pc = rig.Level.SpawnActor(new SPlayerController(), ahead, default)!;
        pc.Possess(merc);
        merc.Rotation = (spy.Rotation.Vector() * -1f).Rotation();
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pc.SPawnType = PawnType.SPAWNDEFENSE;

        var launcher = (SGadSmokeGrenade)spy.SelectedGadget!;
        launcher.ProjectileClass = new ClassRef("SBase.SProjFlashBang");
        launcher.ThrowGren(K.QuickThrowForce, spy.Location, new FRotator(K.QuickThrowPitch, 0, 0), true);
        int flashed = 0;
        for (int i = 0; i < Sim.Hz && flashed == 0; i++)
        {
            rig.Step(still);
            flashed += rig.Level.World.Events.Current.ToArray().Count(e => e.Kind == SimEventKind.Notify
                && e.What == CombatFx.ClientDamageHudEffect && e.ActorId == pc.ActorId && e.Slot == (int)SDamageType.SDTFlash);
        }

        Assert.Equal(1, flashed);
    }
}
