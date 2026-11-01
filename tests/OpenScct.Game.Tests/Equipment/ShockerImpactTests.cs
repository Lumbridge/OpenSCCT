using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ShockerImpactTests
{
    private static readonly Name Shocker = new("SImpactEffectShocker");
    private static readonly Name Spark = new("SSparkShocker");

    private sealed class HitSurface(Actor surface) : IHitZones
    {
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire) => new()
        {
            bHit = true,
            Actor = surface,
            HitLocation = new FVector(200f, 0f, 100f),
            HitNormal = new FVector(-1f, 0f, 0f),
            SurfaceType = ESurfaceType.SURFACE_ConcreteHard,
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShootingAMercKeepsTheSparksWithoutLeavingAFloatingDecal(bool predicted)
    {
        var scene = new CombatScene(predicted ? ENetMode.NM_Client : ENetMode.NM_ListenServer);
        (_, SBasePawn merc) = scene.Player(false, new FVector(200f, 0f, 100f));

        ImpactMark mark = Shoot(scene, merc, predicted);

        Assert.Same(merc, mark.Surface);
        Assert.False(mark.DecalShown);
        AssertSparks(scene, mark);
        merc.Location += new FVector(100f, 0f, -40f);
        Assert.False(mark.DecalShown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShootingAWallStillLeavesAScorchAndSparks(bool predicted)
    {
        var scene = new CombatScene(predicted ? ENetMode.NM_Client : ENetMode.NM_ListenServer);

        ImpactMark mark = Shoot(scene, scene.Level.Info, predicted);

        Assert.Null(mark.Surface);
        Assert.True(mark.DecalShown);
        AssertSparks(scene, mark);
    }

    [Fact]
    public void TheServerBroadcastIdentifiesPawnHitsForOtherPlayers()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        (SPlayerController remote, SBasePawn merc) = scene.Player(false, new FVector(200f, 0f, 100f));
        remote.Player = new SimNetConnection();

        Shoot(scene, merc, predicted: false);

        (_, CombatCall call) = Assert.Single(net.ToClients, c => c.Call.Function == ImpactManager.CNAME_AddImpact);
        Assert.Same(merc, call.Actor1);
        Assert.True(call.Bool1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceivingAPawnHitKeepsSparksButNoDecalEvenIfThePawnHasGone(bool missingPawn)
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        (SPlayerController pc, _) = scene.Player(true, new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(200f, 0f, 100f));
        var call = new CombatCall(ImpactManager.CNAME_AddImpact)
        {
            Name1 = Shocker,
            Vector1 = merc.Location,
            Actor1 = missingPawn ? null : merc,
            Bool1 = true,
        };

        Assert.True(GadgetNetReceive.FromServer(pc, call));

        ImpactMark mark = Assert.Single(ImpactTable.Of(scene.Level).Slots, s => s.Live);
        Assert.False(mark.DecalShown);
        AssertSparks(scene, mark);
    }

    private static ImpactMark Shoot(CombatScene scene, Actor surface, bool predicted)
    {
        scene.Level.SetService<IHitZones>(new HitSurface(surface));
        (_, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        if (predicted)
        {
            pistol.Role = ENetRole.ROLE_SimulatedProxy;
            scene.Level.SetService(new FirePrediction());
            scene.Level.SetService<ICombatNet>(new RecordingCombatNet());
            pistol.ServerFireWeaponWithLocation(spy.Location, 0, 0, 0);
        }
        else
        {
            pistol.TraceFireWithPos(spy.Location, default, 0f, 0f, 0f);
        }

        return Assert.Single(ImpactTable.Of(scene.Level).Slots, s => s.Live);
    }

    [Fact]
    public void ReusingAPawnImpactSlotForAWallRestoresTheDecal()
    {
        var scene = new CombatScene();
        (_, SBasePawn merc) = scene.Player(false, default);
        var table = new ImpactTable();
        ImpactEffectDefaults effect = table.Defaults(Shocker);
        ImpactMark first = table.Add(effect, default, default, 0f, merc);
        Assert.False(first.DecalShown);
        for (int i = 1; i < table.Slots.Count; i++)
        {
            table.Add(effect, default, default, 0f);
        }

        ImpactMark reused = table.Add(effect, default, default, 1f);

        Assert.Same(first, reused);
        Assert.True(reused.DecalShown);
        Assert.Null(reused.Surface);
    }

    private static void AssertSparks(CombatScene scene, ImpactMark mark)
    {
        Assert.Equal(Shocker, mark.Type);
        Assert.Equal("SBase.SSparkShocker", mark.Spark.Path);
        Assert.Contains(scene.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.SpawnEmitter && e.What == Spark);
    }
}
