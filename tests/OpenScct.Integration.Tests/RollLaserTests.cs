using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class RollLaserTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ChangingPoseTicksAndShotQueriesReuseBuffersAfterWarmup(bool mirror, bool rootLocked)
    {
        using var file = new CharacterFixture(headUnderSpine: true);
        Scene scene = new(file, "CrouchPose");
        scene.Pawn.PawnAnim.SetBlendAlpha(SBasePawn.DBLJUMPCHANNEL, 0f);
        scene.Pawn.PawnAnim.PlayAnim(0, new PawnClip(new Name("CrouchPose"), 3, 2f), 1f, 0f, false);
        scene.Pawn.PawnAnim.SetMirror(0, mirror);
        scene.Pawn.PawnAnim.Aim.FullYaw = .2f;
        scene.Pawn.PawnAnim.Directions.SetBoneDirection(new Name("B Spine2"), new FRotator(0, 8192, 0), .5f);
        scene.Pawn.Physics = rootLocked ? EPhysics.PHYS_RootMotion : EPhysics.PHYS_None;
        scene.Pawn.iLockRootMotionType = rootLocked ? 1 : 0;
        var pills = new List<PillShape>(15);
        void Sample(int tick)
        {
            scene.Pawn.PawnAnim.SetFrame(0, tick % 60 / 90f);
            scene.Pose.UpdatePose(scene.Pawn);
            pills.Clear();
            scene.Pose.GetPills(scene.Pawn, pills);
        }
        for (int i = 0; i < 64; i++) Sample(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) Sample(i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(15, pills.Count);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("RollStJpFdL")]
    [InlineData("RollStJpFdR")]
    public void DiveClearsALowBeamOnlyWhileItsAnimatedBodyClearsIt(string sequence)
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, sequence);
        scene.SetTime(0f);
        scene.Laser.CheckPresence();
        Assert.Equal(1, scene.Alarm.Count);
        Assert.Same(scene.Pawn, scene.Alarm.LastInstigator);

        scene.SetTime(0.5f);
        scene.Laser.CheckPresence();
        Assert.Equal(1, scene.Alarm.Count);
        Assert.Equal(scene.Laser.LaserEndLocation, scene.Laser.PrevLaserEndLocation);
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(15, pills.Count);
        // Three frames at 2 fps: the midpoint is UE frame 1/3, not normalized GLB time 1/3.
        Assert.Equal(85f, pills[0].A.Z, 3);

        scene.SetTime(1f);
        scene.Laser.CheckPresence();
        Assert.Equal(2, scene.Alarm.Count);
        Assert.NotEqual(scene.Laser.LaserEndLocation, scene.Laser.PrevLaserEndLocation);
    }

    [Theory]
    [InlineData("RollStJpFdL")]
    [InlineData("RollStJpFdR")]
    public void AirborneDiveStillTripsABeamThatTouchesTheBody(string sequence)
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, sequence);
        scene.SetTime(0.5f);
        scene.SetBeamHeight(85f);
        scene.Laser.CheckPresence();
        Assert.Equal(1, scene.Alarm.Count);
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.25f, true)]
    [InlineData(1f, false)]
    public void EnteringTheRollBlendsCollisionInsteadOfImmediatelyUsingTheAirbornePose(float alpha, bool trips)
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "RollStJpFdL");
        scene.SetTime(0.5f);
        scene.Pawn.PawnAnim.SetBlendAlpha(SBasePawn.DBLJUMPCHANNEL, alpha);
        scene.Laser.CheckPresence();
        Assert.Equal(trips ? 1 : 0, scene.Alarm.Count);
    }

    [Theory]
    [InlineData(250f, false)]
    [InlineData(251f, true)]
    [InlineData(385f, true)]
    [InlineData(460f, true)]
    [InlineData(461f, false)]
    [InlineData(603.2f, false)]
    public void OriginalCrossingSpeedWindowStillOverridesAClearPose(float speed, bool trips)
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "RollStAlFdL");
        scene.SetTime(0.5f);
        scene.Pawn.Velocity = new FVector(0f, speed, 0f);
        scene.Laser.CheckPresence();
        Assert.Equal(trips ? 1 : 0, scene.Alarm.Count);
    }

    [Fact]
    public void AngledDiveKeepsTheDecodedSpeedMinusAlongBeamRule()
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "RollStJpFdL");
        scene.SetTime(0.5f);
        scene.Pawn.Velocity = new FVector(300f, 520f, 0f); // speed 600, across about 300
        scene.Laser.CheckPresence();
        Assert.Equal(1, scene.Alarm.Count);
    }

    [Fact]
    public void PlacementUsesMeshOrientationThenPawnYawAndPrePivot()
    {
        using var file = new CharacterFixture(rotOrigin: -16384);
        Scene scene = new(file, "RollStJpFdL");
        scene.SetTime(0.5f);
        scene.Pawn.Location = new FVector(100f, 200f, 300f);
        scene.Pawn.PrePivot = new FVector(7f, 11f, -20f);
        scene.Pawn.Rotation = new FRotator(4096, 16384, 2048);
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        // The two quarter turns cancel; pitch and roll do not tilt the drawn character.
        Assert.Equal(111.172f, pills[0].A.X, 3);
        Assert.Equal(208.624f, pills[0].A.Y, 3);
        Assert.Equal(315f, pills[0].A.Z, 3);
    }

    [Fact]
    public void UnknownClipsKeepTheExistingFallback()
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "UnknownRoll");
        var expected = new List<PillShape>();
        var actual = new List<PillShape>();
        EstimatedPillPose.Instance.GetPills(scene.Pawn, expected);
        scene.Pose.GetPills(scene.Pawn, actual);
        Assert.Equal(expected, actual);
        Assert.Same(EstimatedPillPose.Instance, RollPillPose.FromContent(null));
        Assert.Same(EstimatedPillPose.Instance, RollPillPose.FromContent(Path.Combine(file.Root, "missing")));
    }

    [Theory]
    [InlineData("CrouchPose", false)]
    [InlineData("LadderPose", false)]
    [InlineData("GrabPose", false)]
    [InlineData("KnockoutPose", false)]
    [InlineData("CrouchPose", true)]
    [InlineData("LadderPose", true)]
    [InlineData("GrabPose", true)]
    [InlineData("KnockoutPose", true)]
    public void BothTeamsUseInstalledNonRollAnimationForHitZones(string sequence, bool merc)
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, sequence, merc);
        scene.Pawn.bIsRolling = false;
        scene.SetTime(0.5f);
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(15, pills.Count);
        Assert.Equal(85f, pills[0].A.Z, 3);
        var zones = new TraceHitZones(scene.Pose);
        SBasePawn shooter = scene.World.Level.SpawnActor(new SPawnDefense_1Mesh(), new FVector(-200f, 0f, 85f), default)!;
        Assert.Same(scene.Pawn, zones.TraceBone(shooter, new FVector(200f, 0f, 85f), shooter.Location, false).Actor);
    }

    [Fact]
    public void RootMotionPoseDoesNotApplyTheActorsDisplacementTwice()
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "LadderPose");
        scene.Pawn.PawnAnim.SetBlendAlpha(SBasePawn.DBLJUMPCHANNEL, 0f);
        scene.Pawn.PawnAnim.PlayAnim(0, new PawnClip(new Name("LadderPose"), 3, 2f), 1f, 0f, false);
        scene.Pawn.PawnAnim.SetFrame(0, 1f / 3f);
        scene.Pawn.Physics = EPhysics.PHYS_RootMotion;
        scene.Pawn.iLockRootMotionType = 1;
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(50f, pills[0].A.Z, 3); // The actor already carries the root's 35 uu ascent.
    }

    [Fact]
    public void RootUnlockTweenStartsAtTheVisibleLockedPose()
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "LadderPose");
        scene.Pawn.PawnAnim.SetBlendAlpha(SBasePawn.DBLJUMPCHANNEL, 0f);
        scene.Pawn.PawnAnim.PlayAnim(0, new PawnClip(new Name("LadderPose"), 3, 2f), 1f, 0f, false);
        scene.Pawn.PawnAnim.SetFrame(0, 1f / 3f);
        scene.Pawn.Physics = EPhysics.PHYS_RootMotion;
        scene.Pawn.iLockRootMotionType = 1;
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(50f, pills[0].A.Z, 3);
        scene.Pawn.Physics = EPhysics.PHYS_Falling;
        scene.Pawn.PawnAnim.PlayAnim(0, new PawnClip(new Name("CrouchPose"), 3, 2f), 1f, .1f, false);
        pills.Clear();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(50f, pills[0].A.Z, 3);
    }

    [Theory]
    [InlineData(1f, 2.376f, 4.172f)]
    [InlineData(.5f, 4.63013f, 1.27000f)]
    public void BoneDirectionsMoveDescendantHitZonesAndReleaseWithoutPoseAccumulation(float alpha, float x, float y)
    {
        using var file = new CharacterFixture(headUnderSpine: true);
        Scene scene = new(file, "CrouchPose");
        scene.Pawn.PawnAnim.Directions.SetBoneDirection(new Name("B Spine2"), new FRotator(0, 16384, 0), alpha);
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(x, pills[0].A.X, 3);
        Assert.Equal(y, pills[0].A.Y, 3);
        scene.Pawn.PawnAnim.Directions.Clear();
        pills.Clear();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(4.172f, pills[0].A.X, 3);
        Assert.Equal(-2.376f, pills[0].A.Y, 3);
    }

    [Fact]
    public void PredictionReplayDoesNotOverwritePoseHistoryAndNetworkResetClearsIt()
    {
        using var file = new CharacterFixture();
        Scene scene = new(file, "CrouchPose");
        scene.Pawn.PawnAnim.Owner = scene.Pawn;
        scene.SetTime(.5f);
        scene.Pawn.PawnAnim.UpdateTweenCache();
        scene.World.IsReplaying = true;
        scene.SetTime(0f);
        scene.Pawn.PawnAnim.UpdateTweenCache();
        scene.World.IsReplaying = false;
        scene.Pawn.PawnAnim.PlayAnim(SBasePawn.DBLJUMPCHANNEL, new PawnClip(new Name("GrabPose"), 3, 2f), 1f, .2f, false);
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(85f, pills[0].A.Z, 3);
        scene.Pawn.ResetNetworkHistory();
        pills.Clear();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(50f, pills[0].A.Z, 3);
    }

    [Fact]
    public void FirstQueryDuringATweenMatchesAContinuouslyQueriedPawn()
    {
        using var file = new CharacterFixture();
        Scene sparse = new(file, "CrouchPose"), frequent = new(file, "CrouchPose");
        var sparsePills = new List<PillShape>();
        var frequentPills = new List<PillShape>();
        foreach (Scene scene in new[] { sparse, frequent })
        {
            scene.Pawn.PawnAnim.Owner = scene.Pawn;
            scene.SetTime(.5f);
            scene.Pawn.PawnAnim.UpdateTweenCache(); // The simulation captures the standing pose without a trace.
            scene.Pawn.PawnAnim.PlayAnim(SBasePawn.DBLJUMPCHANNEL, new PawnClip(new Name("GrabPose"), 3, 2f), 1f, .2f, false);
            scene.Pawn.PawnAnim.UpdateTweenCache();
        }
        for (int i = 0; i < 10; i++)
        {
            frequentPills.Clear();
            frequent.Pose.GetPills(frequent.Pawn, frequentPills);
        }
        foreach (Scene scene in new[] { sparse, frequent })
        {
            scene.Pawn.PawnAnim.SetFrame(SBasePawn.DBLJUMPCHANNEL, -1f / 6f);
            scene.Pawn.PawnAnim.UpdateTweenCache();
        }
        frequentPills.Clear();
        sparse.Pose.GetPills(sparse.Pawn, sparsePills);
        frequent.Pose.GetPills(frequent.Pawn, frequentPills);
        Assert.Equal(67.5f, sparsePills[0].A.Z, 3);
        Assert.Equal(frequentPills, sparsePills);
    }

    [Fact]
    public void FullBodyAimTurnsTheHeadWithTheVisibleSpine()
    {
        using var file = new CharacterFixture(headUnderSpine: true);
        Scene scene = new(file, "CrouchPose");
        scene.Pawn.PawnAnim.Aim.FullPitch = MathF.PI / 2f;
        var pills = new List<PillShape>();
        scene.Pose.GetPills(scene.Pawn, pills);
        Assert.Equal(54.172f, pills[0].A.Z, 3);
        Assert.Equal(0f, pills[0].A.X, 3);
    }

    [Fact]
    public void PartialSkeletonFallsBackInsteadOfReturningIncompleteHitZones()
    {
        using var file = new CharacterFixture(omitHead: true);
        Scene scene = new(file, "RollStJpFdL");
        scene.SetTime(0.5f);
        var expected = new List<PillShape>();
        var actual = new List<PillShape>();
        EstimatedPillPose.Instance.GetPills(scene.Pawn, expected);
        scene.Pose.GetPills(scene.Pawn, actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DedicatedServerAndOrdinaryClientInstallContentPose()
    {
        using var file = new CharacterFixture();
        var maps = new MapCatalog(Path.Combine(file.Root, "maps"));
        using var rig = WalkerRig.Create(null, 771, 1,
            new ServerSettings { Maps = maps, GameOptions = "?GameMode=5?LivesATT=2?LivesDEF=2" },
            new ClientSettings { Maps = maps, Gameplay = true });
        for (int i = 0; i < 300 && rig.Clients[0].State == EClientState.Connecting; i++) rig.Step();
        Assert.Equal(EClientState.Joined, rig.Clients[0].State);
        Assert.IsType<RollPillPose>(rig.Server.World.Level.GetService<IPillPose>());
        Assert.IsType<RollPillPose>(rig.Clients[0].World.Level.GetService<IPillPose>());
    }

    [Fact]
    public void PlayMapLoaderInstallsContentPoseWhenClientSkipsGameplayServices()
    {
        using var file = new CharacterFixture();
        string repo = MapCatalog.FindRepoRoot(AppContext.BaseDirectory)!;
        var loader = new PlayMapLoader(SvMap.Load(Path.Combine(repo, "fixtures", "testbox")), "", file.Characters);
        var world = new World(771, ENetMode.NM_Client);
        loader.Load(world, isClient: true);
        Assert.IsType<RollPillPose>(world.Level.GetService<IPillPose>());
    }

    private sealed class AlarmReceiver : Actor
    {
        public int Count;
        public Pawn? LastInstigator;
        protected override void Trigger(Actor? Other, Pawn? EventInstigator)
        {
            Count++;
            LastInstigator = EventInstigator;
        }
    }

    private sealed class Scene
    {
        public readonly World World = new(771, ENetMode.NM_DedicatedServer);
        public SBasePawn Pawn { get; }
        public SLaserTrigger Laser { get; }
        public AlarmReceiver Alarm { get; }
        public IPillPose Pose { get; }

        public Scene(CharacterFixture file, string sequence, bool merc = false)
        {
            Pose = RollPillPose.FromContent(file.Characters);
            World.Level.SetService<IPillPose>(Pose);
            Pawn = World.Level.SpawnActor<SBasePawn>(merc ? new SPawnDefense_1Mesh() : new SPawnAttaque_1Mesh(), new FVector(0, 0, 50), default)!;
            Pawn.CollisionRadius = 35f;
            Pawn.CollisionHeight = 50f;
            Pawn.PrePivot = default;
            Pawn.bIsRolling = true;
            Pawn.bIsCrouched = true;
            Pawn.Velocity = new FVector(0, 603.2f, 0);
            Pawn.PawnAnim.PlayAnim(SBasePawn.DBLJUMPCHANNEL, new PawnClip(new Name(sequence), 3, 2f), 1f, 0f, false);
            Pawn.PawnAnim.SetBlendAlpha(SBasePawn.DBLJUMPCHANNEL, 1f);
            Alarm = World.Level.SpawnActor(new AlarmReceiver { Tag = new Name("roll-alarm") }, default, default)!;
            Laser = World.Level.SpawnActor(new SLaserTrigger
            {
                bStatic = false, bNoDelete = false, SpawnPerCent = 100, Event = new Name("roll-alarm"), LaserLength = 400,
            }, new FVector(-200, 0, 50), default)!;
            SetBeamHeight(50f);
        }

        public void SetTime(float seconds) => Pawn.PawnAnim.SetFrame(SBasePawn.DBLJUMPCHANNEL, seconds * 2f / 3f);
        public void SetBeamHeight(float height)
        {
            Laser.Location = new FVector(-200, 0, height);
            Laser.LaserEndLocation = new FVector(200, 0, height);
        }
    }

    /// <summary>Synthetic skeleton and three-key poses only; no game animation or mesh data.</summary>
    private sealed class CharacterFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "roll-laser-" + Guid.NewGuid().ToString("N"));
        public string Characters => Path.Combine(Root, "shared", "chars");

        public CharacterFixture(int rotOrigin = 0, bool omitHead = false, bool headUnderSpine = false)
        {
            string[] bones = ["B Head", "B Spine2", "B Pelvis", "B L UpperArm", "B L ForeArm", "B L Hand",
                "B L Thigh", "B L Calf", "B L Foot", "B L Toe0", "B R UpperArm", "B R ForeArm", "B R Hand",
                "B R Thigh", "B R Calf", "B R Foot", "B R Toe0"];
            var nodes = new JsonArray { new JsonObject { ["name"] = "B" } };
            foreach (string name in bones.Where(b => !omitHead || b != "B Head")) nodes.Add(new JsonObject { ["name"] = name });
            nodes[0]!["children"] = new JsonArray(Enumerable.Range(1, nodes.Count - 1).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
            if (headUnderSpine)
            {
                nodes[0]!["children"] = new JsonArray(Enumerable.Range(2, nodes.Count - 2).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
                nodes[2]!["children"] = new JsonArray(1);
            }
            var animations = new JsonArray();
            foreach (string name in new[] { "RollStJpFdL", "RollStJpFdR", "RollStAlFdL", "RollStAlFdR",
                "CrouchPose", "LadderPose", "GrabPose", "KnockoutPose" })
            {
                JsonNode animation = JsonNode.Parse("""
                    {"samplers":[{"input":0,"output":1}],
                     "channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}
                    """)!;
                animation["name"] = name;
                animations.Add(animation);
            }

            JsonObject root = JsonNode.Parse("""
                {"asset":{"version":"2.0"},"buffers":[{"byteLength":48}],
                 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":36}],
                 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"SCALAR"},
                              {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"}]}
                """)!.AsObject();
            root["nodes"] = nodes;
            root["animations"] = animations;
            root["extras"] = new JsonObject { ["svmap"] = new JsonObject { ["rotOrigin"] = new JsonArray(0, rotOrigin, 0) } };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
            int padded = (json.Length + 3) & ~3;
            string folder = Path.Combine(Characters, "SPerso");
            Directory.CreateDirectory(folder);
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)(12 + 8 + padded + 8 + 48));
            writer.Write((uint)padded); writer.Write(0x4e4f534au); writer.Write(json);
            for (int i = json.Length; i < padded; i++) writer.Write((byte)' ');
            writer.Write(48u); writer.Write(0x004e4942u);
            foreach (float value in new[] { 0f, 0.5f, 1f, 0f, 0f, 0f, 0f, 0.35f, 0f, 0f, 0f, 0f }) writer.Write(value);
            writer.Flush();
            File.WriteAllBytes(Path.Combine(folder, "ATT_01.glb"), output.ToArray());
            File.WriteAllBytes(Path.Combine(folder, "DEF_01.glb"), output.ToArray());
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
