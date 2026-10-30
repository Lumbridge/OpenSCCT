using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>
/// Replays the SteeE crane chain (spec/devices/steee-chains.md, crane chain timeline) on a synthetic level laid out
/// like it: receivers in the map's dispatch order, the crane and wall sequences with their decoded frame counts,
/// rates and notify times, the crushing duct, the wall and crane collision swaps, the hidden explosives, the shaker,
/// the music and the GE switch.
/// </summary>
public sealed class SteeECraneChainTests
{
    private const float Tick = 1f / 60f;

    private static readonly AssetRef CraneMesh = new("sqt13_anm.sqt_grue");
    private static readonly AssetRef WallAnimMesh = new("sqt13_anm.sqt_mur");
    private static readonly AssetRef WallBefore = new("SteeE.sqt_murkc_avant");
    private static readonly AssetRef WallAfter = new("SteeE.sqt_murkc_apres");
    private static readonly AssetRef CraneColl1 = new("SteeE.sqt_grue_coll01");
    private static readonly AssetRef CraneColl2 = new("SteeE.sqt_grue_coll02");
    private static readonly AssetRef Extinguisher = new("SteeE.squ_mer_obj_extinct");
    private static readonly AssetRef Stress = new("Squ_Mus_Stress");

    private sealed class Chain
    {
        public DeviceScene S = null!;
        public Sender Switch = null!;
        public SAnimatedMesh Crane = null!;
        public Mover Duct = null!;
        public SGamePlayObject Wall = null!;
        public SGamePlayObject CraneCollision = null!;
        public SAnimatedMesh WallCollapse = null!;
        public SShaker Shaker = null!;
        public SGamePlayObject Explosive346 = null!;
        public SGamePlayObject Explosive2889 = null!;
        public MusicTrigger Music = null!;
        public SGETrigger GETrigger = null!;
        public Light Flare = null!;
        public TestSpy Spy = null!;
        public TestMerc UnderDuct = null!;
        public TestMerc NearExplosive = null!;
        public EventProbe BlastProbe = null!;
        public EventProbe TubeProbe = null!;
        public EventProbe Blast01Probe = null!;
        public readonly List<string> Order = new();
        public readonly List<(AssetRef Wall, AssetRef CraneColl)> SeenByEmitter483 = new();
    }

    /// <summary>A stand-in for one of the crane's debris emitters that logs its place in the dispatch.</summary>
    private sealed class OrderProbe : Actor
    {
        public string Label = "";
        public List<string>? Order;
        public System.Action? OnTrigger;

        protected override void Trigger(Actor? Other, Pawn? EventInstigator)
        {
            Order?.Add(Label);
            OnTrigger?.Invoke();
        }
    }

    private static UAnimNotify_Sound Sound(string sound, float volume, int radius) =>
        new() { Sound = new AssetRef(sound), Volume = volume, Radius = radius };

    private static UAnimNotify_Trigger Event(string name) => new() { EventName = name };

    private static Chain Build()
    {
        var c = new Chain();
        DeviceScene s = c.S = DeviceScene.Create(ENetMode.NM_DedicatedServer);
        s.Level.Info.GEActivationMask = 4;

        // Crane sequence: 451 frames at 40 fps; wall collapse: 101 frames at 30 fps (sqt13_anm.ukx).
        s.Animations.Add(CraneMesh, new MeshAnimSequence("sqt_grue_seq", 451, 40f,
        [
            new MeshAnimNotify(0.972239f, Event("blast01")),
            new MeshAnimNotify(0.910887f, Event("tueletube")),
            new MeshAnimNotify(0.000222f, Sound("Squ_Grue_Seq_Start", 4f, 200)),
            new MeshAnimNotify(0.170732f, Sound("Seq_Grue_Loop", 3.6f, 200)),
            new MeshAnimNotify(0.219512f, Sound("Seq_Grue_Loop", 2.8f, 200)),
            new MeshAnimNotify(0.636297f, Sound("Squ_Grue_Seq_Stop", 4f, 200)),
            new MeshAnimNotify(0.841020f, Sound("Squ_Grue_Seq_Drop", 6f, 200)),
        ]));
        s.Animations.Add(WallAnimMesh, new MeshAnimSequence("sqt_mur_seq", 101, 30f,
            [new MeshAnimNotify(0.00099f, Sound("MC_mur_destroy", 1f, 300))]));

        c.Switch = s.Add(new Sender(), V.At(-264f, 1078f, -999f));
        c.Flare = s.Add(new Light(), V.At(-1189f, 1374f, 1933f));

        // Saved Actors order: crane, the two alarm sound triggers, the duct, then the blast01 receivers.
        c.Crane = s.Add(new SAnimatedMesh { Mesh = CraneMesh, DrawScale = 10f }, V.At(-1189f, 1374f, 1648f), tag: "blast");
        c.Crane.Animations.Add("sqt_grue_seq");
        c.Crane.ActorsToAttach.Add(new FAttachActor { BoneName = "Dummy01", ActorName = c.Flare });
        foreach (FVector at in new[] { V.At(-1215f, 1130f, -264f), V.At(-2753f, 621f, -434f) })
        {
            s.Add(new SoundTrigger { Sound = new AssetRef("Seq_alarme"), SoundVolume = 255, SoundRadius = 300f }, at, tag: "blast");
        }

        FVector ductBase = V.At(-3186f, 442f, 1580f);
        c.Duct = s.Add(new Mover
        {
            InitialState = "TriggerOpenOnceOnly",
            MoverEncroachType = EMoverEncroachType.ME_CrushWhenEncroach,
            BasePos = ductBase,
        }, ductBase, tag: "tueletube");
        c.Duct.KeyPos[1] = V.At(0f, 0f, -3920f);
        s.Collider(c.Duct, V.At(-60f, -60f, -120f), V.At(60f, 60f, 120f));

        c.Wall = s.Add(new SGamePlayObject
        {
            StaticMesh = WallBefore,
            TriggeredMesh = WallAfter,
            HitPoints = 10,
        }, V.At(-3600f, 1700f, -900f), tag: "blast01");
        c.Wall.DestroyOnEventFrom.Add(c.Crane);
        s.Collider(c.Wall, V.At(-20f, -200f, -150f), V.At(20f, 200f, 150f), variants: 1);

        OrderProbe Emitter(string label, System.Action? onTrigger = null) =>
            s.Add(new OrderProbe { Label = label, Order = c.Order, OnTrigger = onTrigger }, V.At(-3200f, 600f, -1000f), tag: "blast01");

        Emitter("Emitter483", () => c.SeenByEmitter483.Add((c.Wall.StaticMesh, c.CraneCollision.StaticMesh)));
        Emitter("Emitter287");
        c.CraneCollision = s.Add(new SGamePlayObject { StaticMesh = CraneColl1, TriggeredMesh = CraneColl2, HitPoints = 10 },
            V.At(-1189f, 1374f, 1648f), tag: "blast01");
        Emitter("Emitter2155");
        c.WallCollapse = s.Add(new SAnimatedMesh { Mesh = WallAnimMesh, bHidden = true }, V.At(-3223f, 610f, -1102f), tag: "blast01");
        c.WallCollapse.Animations.Add("sqt_mur_seq");
        Emitter("Emitter305");
        Emitter("Emitter349");
        c.Shaker = s.Add(new SShaker(), V.At(-3200f, 600f, -1000f), tag: "blast01");
        c.Shaker.ShakeEvents.Add(new FShakeEvent { EventSender = c.Crane, strenght = 1000, speed = 10000, FadeOut = 1000, bTilt = true, bShake = true });
        c.Shaker.ShakeEvents.Add(new FShakeEvent { EventSender = c.Crane, strenght = 800, speed = 10000, FadeOut = 2000, bTilt = true });
        Emitter("Emitter511");
        c.Explosive346 = s.Add(HiddenExplosive(c.Crane), V.At(-3242f, 848f, -1062f), tag: "blast01");
        c.Explosive2889 = s.Add(HiddenExplosive(c.Crane), V.At(-3247f, 262f, -1062f), tag: "blast01");
        c.Music = s.Add(new MusicTrigger { Song = Stress, bToggle = true }, V.At(-3000f, 600f, -900f), tag: "blast01");
        c.GETrigger = s.Add(new SGETrigger { GEMask = 4, bToggle = true }, V.At(-3186f, 600f, -808f), tag: "blast01");

        // Probes after every receiver, so they see each event once its receivers are done.
        c.BlastProbe = s.Add(new EventProbe(), default, tag: "blast");
        c.TubeProbe = s.Add(new EventProbe(), default, tag: "tueletube");
        c.Blast01Probe = s.Add(new EventProbe(), default, tag: "blast01");

        s.Begin();

        c.Spy = s.Player<TestSpy>(V.At(-264f, 1078f, -999f));
        c.UnderDuct = s.Player<TestMerc>(V.At(-3186f, 442f, -1500f));
        c.NearExplosive = s.Player<TestMerc>(V.At(-3242f, 700f, -1062f));
        return c;
    }

    private static SGamePlayObject HiddenExplosive(Actor crane)
    {
        var gpo = new SGamePlayObject
        {
            StaticMesh = Extinguisher,
            bHidden = true,
            bDamageable = true,
            HitPoints = 1,
            bCanDamageActor = true,
            DamageActorPoints = 600,
            DamageActorRange = 500,
            DamageActorType = SDamageType.SDamage_Charged,
        };
        gpo.TakeDamageTypes.Add(SDamageType.Crushed);
        gpo.DestroyOnEventFrom.Add(crane);
        gpo.DamagedMeshes.Add(new FDamagedMesh { Percent = 0, bDisableCollision = true });
        gpo.SpawnableObjects.Add(new FSpawnInfo { SpawnClass = new ClassRef("SBase.SGrenadeExplosion"), MinPercent = 100, MaxPercent = 100 });
        return gpo;
    }

    /// <summary>
    /// The switch (first in the list) fires its event inside tick 31 (T0); runs to <paramref name="seconds"/> after
    /// it and returns T0. Receivers later in the list react in the same tick, so a decoded time is met within a tick.
    /// </summary>
    private static float Run(Chain c, float seconds)
    {
        c.S.Step(30);
        c.Switch.FireNextTick("blast", c.Spy);
        c.S.Step();
        float t0 = c.Switch.FiredAt;
        c.S.StepSeconds(seconds - Tick);
        return t0;
    }

    [Fact]
    public void CraneChainFollowsTheDecodedTimeline()
    {
        Chain c = Build();
        float t0 = Run(c, 15f);

        // blast: the crane takes the spy as its instigator and plays; both alarms sound.
        Assert.Single(c.BlastProbe.Calls);
        Assert.Same(c.Spy, c.Crane.Instigator);
        Assert.Equal(2, c.S.Sounds.Of(SimEventKind.PlaySound, "Seq_alarme").Count);

        // tueletube at t * 451 / 40 = 10.270 s, blast01 at 10.962 s: first tick at or after.
        (float tube, _, _, Actor? tubeOther, Pawn? tubeInstigator) = Assert.Single(c.TubeProbe.Calls);
        V.Near(10.270f, tube - t0, Tick + 1e-3f);
        Assert.Same(c.Crane, tubeOther);
        Assert.Null(tubeInstigator);
        (float blast01, _, _, Actor? blastOther, _) = Assert.Single(c.Blast01Probe.Calls);
        V.Near(10.962f, blast01 - t0, Tick + 1e-3f);
        Assert.Same(c.Crane, blastOther);

        // The crane's notify sounds, in time order (package order lists blast01 first; firing follows time).
        List<SimEvent> starts = c.S.Sounds.Of(SimEventKind.PlaySound, "Squ_Grue_Seq_Start");
        Assert.Single(starts);
        Assert.Equal(4f, starts[0].Volume);
        Assert.Equal(200f, starts[0].Radius);
        Assert.Equal(2, c.S.Sounds.Of(SimEventKind.PlaySound, "Seq_Grue_Loop").Count);
        Assert.Single(c.S.Sounds.Of(SimEventKind.PlaySound, "Squ_Grue_Seq_Drop"));

        // The crane ends at (N - 1) / R = 11.25 s, holding its last pose in s_Wait.
        Assert.True(c.Crane.IsInState("s_Wait"));
        Assert.False(c.Crane.IsAnimating);
        Assert.Equal(450f / 451f, c.Crane.AnimFrame, 5);
    }

    [Fact]
    public void DuctDropsCrushesWithTheSpysCreditAndLocks()
    {
        Chain c = Build();
        float t0 = Run(c, 10.20f);
        Assert.Equal(1580f, c.Duct.Location.Z, 2);

        float tube = -1f;
        float stopped = -1f;
        float crushed = -1f;
        for (int i = 0; i < 120; i++)
        {
            c.S.Step();
            if (tube < 0f && c.Duct.bInterpolating)
            {
                tube = c.S.Time - t0;
            }

            if (crushed < 0f && c.UnderDuct.Controller is TestController { PawnDamage.Count: > 0 })
            {
                crushed = c.S.Time - t0;
            }

            if (stopped < 0f && c.Duct.IsInState("s_TemporaryInactive"))
            {
                stopped = c.S.Time - t0;
            }
        }

        // 3920 uu in MoveTime 1.0 from 10.27 s, then locked for the round at Z -2340.
        V.Near(10.27f, tube, 2 * Tick);
        V.Near(11.27f, stopped, 3 * Tick);
        Assert.Equal(-2340f, c.Duct.Location.Z, 2);
        Assert.True(c.Duct.IsInState("s_TemporaryInactive"));

        // The merc under the duct line is crushed on the way down, credited to the hacking spy: Health + 100 SDTBase.
        Assert.InRange(crushed, 10.27f, 11.27f);
        var controller = (TestController)c.UnderDuct.Controller!;
        (int damage, Pawn? by, SDamageType type) = controller.PawnDamage[0];
        Assert.Equal(200, damage);
        Assert.Same(c.Spy, by);
        Assert.Equal(SDamageType.SDTBase, type);
        Assert.True(c.UnderDuct.Health <= 0);

        // A second tueletube does nothing to a locked duct.
        c.Switch.TriggerEvent("tueletube", c.Crane, null);
        c.S.Step(90);
        Assert.Equal(-2340f, c.Duct.Location.Z, 2);
    }

    [Fact]
    public void Blast01ReceiversRunInDispatchOrderWithTheirEffects()
    {
        Chain c = Build();
        Run(c, 15f);

        // Dispatch order: the wall swaps before Emitter483 runs, the crane collision after it.
        Assert.Equal(["Emitter483", "Emitter287", "Emitter2155", "Emitter305", "Emitter349", "Emitter511"], c.Order);
        Assert.Equal((WallAfter, CraneColl1), Assert.Single(c.SeenByEmitter483));

        // The wall and the crane collision swap meshes; the wall's collision goes with its old mesh.
        Assert.Equal(WallAfter, c.Wall.StaticMesh);
        Assert.Equal(CraneColl2, c.CraneCollision.StaticMesh);
        var colliders = new List<int>();
        c.S.Collision!.GetActorColliders(c.Wall, colliders);
        Assert.False(c.S.Collision.GetCollider(Assert.Single(colliders)).Enabled);

        // The wall collapse becomes visible and plays its 101-frame sequence at 30 fps with its sound.
        Assert.False(c.WallCollapse.bHidden);
        Assert.Single(c.S.Sounds.Of(SimEventKind.PlaySound, "MC_mur_destroy"));

        // Every player: two ClientShake calls in entry order; the camera keeps the last tilt and the shake. The merc
        // under the duct is out of Explosive346's reach (d2 359816) but inside its ShakeRange 2000, so its blast
        // shakes him once more: 50 S / d2 = 555, 2000 (S / d2) = 22000 (natives2-damage.md rule 1.3.4).
        foreach (Pawn p in new Pawn[] { c.Spy, c.UnderDuct, c.NearExplosive })
        {
            var pc = (TestController)p.Controller!;
            if (ReferenceEquals(p, c.UnderDuct))
            {
                Assert.Equal([(1000, 10000, 1000, true, true), (800, 10000, 2000, true, false), (555, 22000, 500, true, true)], pc.Shakes);
                continue;
            }

            Assert.Equal([(1000, 10000, 1000, true, true), (800, 10000, 2000, true, false)], pc.Shakes);
            Assert.Equal(800, pc.m_camera!.m_tiltPitch);
            Assert.Equal(2000, pc.m_camera.m_tiltFadeOut);
            Assert.Equal(1000, pc.m_camera.m_shakeRoll);
            Assert.Equal(1000, pc.m_camera.m_shakeFadeOut);
        }

        // The hidden explosives blow: broken, no collision, an explosion visual, 600 SDamage_Charged within 500 uu,
        // credited to the spy.
        foreach (SGamePlayObject explosive in new[] { c.Explosive346, c.Explosive2889 })
        {
            Assert.Equal(0, explosive.HitPoints);
            Assert.True(explosive.bAlreadyDestroyed);
            Assert.Equal(EDrawType.DT_None, explosive.DrawType);
            Assert.False(explosive.bCollideActors);
            Assert.IsType<SGrenadeExplosion>(explosive.SpawnableObjects[0].SpawnedActor);
            Assert.Same(c.Spy, explosive.Instigator);
        }

        // Through the controller with no instigator (credited by the script's GPO fallback) and the linear falloff:
        // 148 uu from Explosive346 gives int(352 * 600 / 500) = 422.
        Assert.Contains((422, (Pawn?)null, SDamageType.SDamage_Charged), ((TestController)c.NearExplosive.Controller!).PawnDamage);
        Assert.DoesNotContain(((TestController)c.Spy.Controller!).PawnDamage, d => d.Type == SDamageType.SDamage_Charged);

        // The stress music starts and the wall-top ledge (GE mask bit 4) is gone.
        Assert.Single(c.S.Sounds.Of(SimEventKind.PlayMusic, "Squ_Mus_Stress"));
        Assert.Equal(Stress, c.Music.GetCurrentMusic());
        Assert.Equal(0, c.S.Level.Info.GEActivationMask);

        // The warning lamps hang on the crane's bones.
        Assert.Same(c.Crane, c.Flare.Base);
        Assert.Equal(new Name("Dummy01"), c.Flare.AttachmentBone);
    }

    [Fact]
    public void WallCollapseHoldsItsLastFrameAfterThreeSeconds()
    {
        Chain c = Build();
        float t0 = Run(c, 10.95f);
        Assert.True(c.WallCollapse.bHidden);
        float end = -1f;
        for (int i = 0; i < 400 && end < 0f; i++)
        {
            c.S.Step();
            if (!c.WallCollapse.bHidden && !c.WallCollapse.IsAnimating)
            {
                end = c.S.Time - t0;
            }
        }

        // blast01 at 10.962 s plus (N - 1) / R = 100 / 30 s: 14.295 s.
        V.Near(14.295f, end, 3 * Tick);
        Assert.Equal(100f / 101f, c.WallCollapse.AnimFrame, 5);
        Assert.True(c.WallCollapse.IsInState("s_Wait"));
    }
}
