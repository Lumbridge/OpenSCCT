using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The impact manager's marks and their broadcast (spec/equipment/impact-manager.md).</summary>
public sealed class ImpactManagerTests
{
    private static readonly Name Beton = new("SImpactEffectBeton");
    private static readonly Name AddImpact = new("AddImpact");

    /// <summary>Every trace hits a concrete wall 500 uu ahead of the shooter (normal back toward it).</summary>
    private sealed class WallAhead : IHitZones
    {
        public int Traces;

        public BoneTraceResult TraceBone(Actor Shooter, FVector TraceEnd, FVector TraceStart, bool bRealFire)
        {
            Traces++;
            FVector dir = (TraceEnd - TraceStart).SafeNormal();
            return new BoneTraceResult
            {
                Actor = Shooter.XLevel.Info,
                bHit = true,
                HitLocation = TraceStart + dir * 500f,
                HitNormal = -dir,
                SurfaceType = ESurfaceType.SURFACE_ConcreteHard,
            };
        }
    }

    [Fact]
    public void MarksFillFreeSlotsThenReplaceTheOldest()
    {
        var table = new ImpactTable();
        ImpactEffectDefaults beton = table.Defaults(Beton);
        Assert.Equal("sfx.Impacts.decal_beton", beton.ImpactMaterial.Path);
        Assert.Equal("SBase.SSparkBeton", beton.SparkEmitter.Path);
        ImpactMark first = table.Add(beton, new FVector(1f, 0f, 0f), default, 0.5f);
        Assert.Equal(15, first.Timer);
        for (int i = 1; i < K.ImpactSlots; i++)
        {
            table.Add(beton, new FVector(i, 0f, 0f), default, 0.5f + (i * 0.01f));
        }

        Assert.Equal(K.ImpactSlots, table.LiveCount);

        // Full: the oldest (smallest timer, the first of equals) is reused at once.
        ImpactMark reused = table.Add(beton, new FVector(99f, 0f, 0f), default, 2f);
        Assert.Same(first, reused);
        Assert.Equal(2, reused.Serial);
        Assert.Equal(17, reused.Timer);
    }

    [Fact]
    public void TheDecalSitsTenUnitsOutAlongTheRotation()
    {
        var table = new ImpactTable();
        ImpactMark m = table.Add(table.Defaults(Beton), new FVector(100f, 0f, 0f), new FVector(-1f, 0f, 0f).Rotation(), 0f);
        Assert.Equal(90f, m.DecalLocation.X, 3);
        Assert.Equal("sfx.Impacts.decal_beton", m.Material.Path);
        Assert.True(m.Mesh.IsNone);
    }

    [Fact]
    public void TheBulletHoleIsDrawnWithItsMesh()
    {
        var table = new ImpactTable();
        ImpactMark m = table.Add(table.Defaults(new Name("SImpactEffectHole")), default, default, 0f);
        Assert.Equal("SEffectStatic.Impacts.light_hole", m.Mesh.Path);
        Assert.Equal(4f, m.DrawScale);
        Assert.True(m.Unlit);
    }

    [Fact]
    public void MarksAreFreedByTheOneSecondTimerAfterFifteenSeconds()
    {
        var table = new ImpactTable();
        table.Update(0f);
        table.Add(table.Defaults(Beton), default, default, 0.2f);
        table.Update(15f);
        Assert.Equal(1, table.LiveCount);
        table.Update(16f);
        Assert.Equal(0, table.LiveCount);
    }

    [Fact]
    public void ARifleShotOnAWallLeavesAMarkAndSendsItToEveryRemotePlayer()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        var wall = new WallAhead();
        scene.Level.SetService<IHitZones>(wall);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        (SPlayerController remote, _) = scene.Player(true, new FVector(0f, 500f, 100f));
        remote.Player = new SimNetConnection();
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(0.1f);

        rifle.ServerFireWeapon(1);

        Assert.Equal(1, wall.Traces);
        ImpactTable table = ImpactTable.Of(scene.Level);
        ImpactMark mark = Assert.Single(table.Slots, s => s.Live);
        Assert.Equal(Beton, mark.Type);
        (PlayerController to, CombatCall call) = Assert.Single(net.ToClients, c => c.Call.Function == AddImpact);
        Assert.Same(remote, to);
        Assert.Equal(Beton, call.Name1);
        Assert.Equal(mark.Location, call.Vector1);
        Assert.DoesNotContain(net.ToClients, c => ReferenceEquals(c.To, pc));
    }

    [Fact]
    public void TheClientAddsTheMarkItIsSentAndRaisesTheSparkAndTheSound()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        (SPlayerController pc, _) = scene.Player(false, new FVector(0f, 0f, 100f));
        scene.Run(1f / Sim.Hz);
        var call = new CombatCall(AddImpact) { Name1 = new Name("SImpactEffectMetal"), Vector1 = new FVector(10f, 20f, 30f) };
        Assert.True(GadgetNetReceive.FromServer(pc, call));
        ImpactMark mark = Assert.Single(ImpactTable.Of(scene.Level).Slots, s => s.Live);
        Assert.Equal("sfx.Impacts.decal_metal", mark.Material.Path);
        SimEvent[] events = scene.World.Events.Current.ToArray();
        Assert.Contains(events, e => e.Kind == SimEventKind.SpawnEmitter && e.What == new Name("SMetalSpark"));
        Assert.Contains(events, e => e.Kind == SimEventKind.PlaySound && e.What.ToString().EndsWith("RA_ImpactShot_MetS", System.StringComparison.Ordinal));
        Assert.Contains(events, e => e.Kind == SimEventKind.Notify && e.What == ImpactManager.ImpactAdded);
    }

    [Fact]
    public void ADedicatedServerSendsTheMarkButKeepsNone()
    {
        var scene = new CombatScene(ENetMode.NM_DedicatedServer);
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        (SPlayerController remote, SBasePawn pawn) = scene.Player(false, new FVector(0f, 0f, 100f));
        remote.Player = new SimNetConnection();
        ImpactManager.AddImpact(pawn, Beton, default, default);
        Assert.Single(net.ToClients);
        Assert.Equal(0, ImpactTable.Of(scene.Level).LiveCount);
    }
}
