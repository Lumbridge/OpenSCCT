using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenScct.Animation;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Content;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class AnimationStudioTests
{
    private static System.Collections.Generic.List<AnimationController> Controllers(World world)
    {
        var result = new System.Collections.Generic.List<AnimationController>();
        foreach (AnimationController controller in world.Level.AllActors<AnimationController>()) result.Add(controller);
        return result;
    }
    private static AnimationProject Project()
    {
        var skeleton = new SkeletonDefinition { Bones = [new() { Name = "Root" }] };
        var clip = new AnimationClip { Name = "Move", Skeleton = skeleton.Id, Duration = 1, Loop = true,
            Tracks = [new() { Bone = "Root", Channel = TrackChannel.Translation, Keys = [new() { Time = 0 }, new() { Time = 1, Value = new(1, 0, 0, 0) }] }] };
        var node = new GraphNode { Clip = clip.Id };
        var graph = new AnimationGraph { Skeleton = skeleton.Id, Nodes = [node], States = [new() { Name = "Idle", Node = node.Id }, new() { Name = "Active", Node = node.Id }],
            Parameters = [new() { Name = "Go", Kind = ParameterKind.Trigger }], Transitions = [new() { From = "Idle", To = "Active", Parameter = "Go", Operator = ConditionOperator.Triggered }] };
        var sequence = new AnimationSequence { Duration = 1, Tracks = [new() { Kind = SequenceTrackKind.Event, Events = [new() { Time = 0, Kind = AnimationEventKind.Trigger, Target = "Probe" }, new() { Time = .5f, Kind = AnimationEventKind.Trigger, Target = "Probe" }] }] };
        return new() { Skeletons = [skeleton], Clips = [clip], Graphs = [graph], Sequences = [sequence], Bindings = [new() { Target = "actor:Prop", Slot = "graph", Graph = graph.Id, Mode = ReplacementMode.Gameplay }, new() { Target = "sequence:start", Slot = "StartSequence", Sequence = sequence.Id, Mode = ReplacementMode.Gameplay }] };
    }
    public sealed class Probe : Actor
    {
        public int Calls;
        public Probe() { RemoteRole = ENetRole.ROLE_None; }
        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Calls++;
    }
    [Theory]
    [InlineData("A")]
    [InlineData("100")]
    public void ControllersReplicateStateAndLateJoinWithoutReplayingEvents(string profile)
    {
        var clock = new VirtualClock(); var network = new SimLinkNetwork(clock, 1743, SimLinkProfile.Get(profile));
        var classes = NetClasses.Create(); var maps = new MapCatalog();
        using var serverTransport = network.Bind("server");
        using var server = new ServerHost(serverTransport, new ServerSettings { Maps = maps, Classes = classes });
        AnimationProject project = Project(); server.World.Level.SetService(new AnimationWorld(project));
        Probe probe = server.World.Level.Spawn<Probe>(tag: new Name("Probe"))!;
        AnimationController graph = server.World.Level.Spawn<AnimationController>(tag: new Name("Graph"))!; graph.BindingIndex = 0; graph.Initialize();
        AnimationController sequence = server.World.Level.Spawn<AnimationController>(tag: new Name("StartSequence"))!; sequence.BindingIndex = 1; sequence.Initialize();
        using var transport = network.Bind("client");
        using var client = new ClientSession(transport, "server", 0, new ClientSettings { Maps = maps, Classes = classes });
        long tick = 0;
        void Step(int count, ClientSession? late = null)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++tick, Sim.Hz));
                server.Tick(clock.NowMicroseconds); client.Tick(clock.NowMicroseconds);
                if (client.State == EClientState.Joined && client.World.Level.GetService<AnimationWorld>() is null) client.World.Level.SetService(new AnimationWorld(AnimationJson.Clone(project)));
                if (late is null) continue;
                late.Tick(clock.NowMicroseconds);
                if (late.State == EClientState.Joined && late.World.Level.GetService<AnimationWorld>() is null) late.World.Level.SetService(new AnimationWorld(AnimationJson.Clone(project)));
            }
        }
        Step(300); Assert.Equal(EClientState.Joined, client.State);
        graph.SetParameter("Go", 1); sequence.eventTrigger(null, null); Step(150);
        Assert.Equal(2, probe.Calls);
        AnimationController remoteGraph = Assert.Single(Controllers(client.World), c => c.BindingIndex == 0);
        Assert.Equal("Active", remoteGraph.StateName);
        Assert.Equal("Active", remoteGraph.EvaluatedState);
        Assert.NotNull(remoteGraph.Pose("actor:Prop", out _));
        using var lateTransport = network.Bind("late");
        using var lateClient = new ClientSession(lateTransport, "server", 0, new ClientSettings { Maps = maps, Classes = classes });
        Step(300, lateClient);
        Assert.Equal(EClientState.Joined, lateClient.State);
        Assert.Equal("Active", Assert.Single(Controllers(lateClient.World), c => c.BindingIndex == 0).StateName);
        Assert.False(Assert.Single(Controllers(lateClient.World), c => c.BindingIndex == 1).Playing);
        Assert.Equal(2, probe.Calls);
    }
    [Fact]
    public void VisualBindingPreservesTimingAndGameplayBindingSuppliesRootMotion()
    {
        AnimationProject project = Project(); project.Bindings.Clear();
        project.Bindings.Add(new() { Target = "SPerso.ATT_01", Slot = "WaitStAlFd0", Clip = project.Clips[0].Id });
        var pawn = new SPawnAttaque { Mesh = new("SPerso.ATT_01") };
        Assert.True(PawnAnimationSource.Default.TryGetClip(pawn, new Name("WaitStAlFd0"), out PawnClip stock));
        var visual = new AnimationWorld(project).PawnSource(PawnAnimationSource.Default);
        Assert.True(visual.TryGetClip(pawn, new Name("WaitStAlFd0"), out PawnClip unchanged)); Assert.Same(stock, unchanged);
        project.Bindings[0].Mode = ReplacementMode.Gameplay;
        var gameplay = new AnimationWorld(project).PawnSource(PawnAnimationSource.Default);
        Assert.True(gameplay.TryGetClip(pawn, new Name("WaitStAlFd0"), out PawnClip authored));
        Assert.Equal(1, authored.OneShotSeconds, .0001f); Assert.NotNull(authored.RootTrack);
        Assert.Equal(100, authored.RootTrack.Positions[^1].X, .001f);
    }
    [Fact]
    public void SelectedPacksParticipateInTheConnectionContractAndSelectionInvalidatesCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "svm-animation-contract-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "sim"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, AnimationBundle.RelativePath), new AnimationBundle { Project = Project() }.ToBytes());
            var maps = new MapCatalog(); ulong stock = maps.CharacterContentHash; maps.AnimationPackFolder = root;
            Assert.NotEqual(stock, maps.CharacterContentHash); maps.AnimationPackFolder = null; Assert.Equal(stock, maps.CharacterContentHash);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void MismatchedSelectedPacksCannotJoinGameplay()
    {
        string root = Path.Combine(Path.GetTempPath(), "svm-animation-handshake-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "sim"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, AnimationBundle.RelativePath), new AnimationBundle { Project = Project() }.ToBytes());
            var clock = new VirtualClock(); var network = new SimLinkNetwork(clock, 1711, SimLinkProfile.A);
            var classes = NetClasses.Create(); var serverMaps = new MapCatalog { AnimationPackFolder = root }; var clientMaps = new MapCatalog { AnimationPackFolder = null };
            using var serverTransport = network.Bind("server");
            using var server = new ServerHost(serverTransport, new ServerSettings { Maps = serverMaps, Classes = classes, GameOptions = "?GameMode=5?LivesATT=2?LivesDEF=2" });
            using var transport = network.Bind("client");
            using var client = new ClientSession(transport, "server", 0, new ClientSettings { Maps = clientMaps, Classes = classes, Gameplay = true });
            for (int tick = 1; tick <= 300 && client.State == EClientState.Connecting; tick++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz)); server.Tick(clock.NowMicroseconds); client.Tick(clock.NowMicroseconds);
            }
            Assert.Equal(EClientState.Rejected, client.State); Assert.Equal(ControlMessage.VersionMismatchText, client.RejectReason);
            Assert.Null(client.Map); Assert.Empty(server.Players);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ViewModelNotifiesFollowChannelLoopingAndDoNotRepeatAtOneShotCompletion()
    {
        AnimationProject project = Project(); project.Bindings.Clear();
        project.Clips[0].Events = [new() { Time = 0, Kind = AnimationEventKind.Trigger, Target = "Probe" }];
        project.Bindings.Add(new() { Target = "Test.Weapon", Slot = "Action", Clip = project.Clips[0].Id, Mode = ReplacementMode.Gameplay });
        var world = new World(1); Probe probe = world.Level.Spawn<Probe>(tag: new Name("Probe"))!;
        Actor owner = world.Level.Spawn<Probe>()!; owner.Mesh = new("Test.Weapon");
        var source = new StudioViewAnimationSource(new AnimationWorld(project));
        world.Level.SetService<OpenScct.Game.Equipment.IViewAnimationSource>(source);
        var anim = new OpenScct.Game.Equipment.ViewModelAnim { ResolveClip = (name, fallback) => source.Resolve(owner, name, fallback) };
        anim.Play(0, new Name("Action"), new(31, 30)); anim.Advance(1.1f, owner, (_, _) => { });
        Assert.Equal(1, probe.Calls);
        project.Clips[0].Loop = false;
        anim.Loop(0, new Name("Action")); anim.Advance(2.1f, owner, (_, _) => { });
        Assert.Equal(4, probe.Calls);
    }
    [Fact]
    public void AuthoredFirstPersonHolsterUsesItsOwnGameplayDurationAndFrameSeeking()
    {
        AnimationProject project = Project(); project.Bindings.Clear(); project.Clips[0].Duration = 2;
        project.Bindings.Add(new() { Target = "SWeaponFirst.1DEF_01", Slot = "FirstHolsterStFd2", Clip = project.Clips[0].Id, Mode = ReplacementMode.Gameplay });
        var source = new StudioViewAnimationSource(new AnimationWorld(project));
        var owner = new Probe { Mesh = new("SWeaponFirst.1DEF_01") };
        var anim = new OpenScct.Game.Equipment.ViewModelAnim { ResolveClip = (name, fallback) => source.Resolve(owner, name, fallback) };
        var slot = OpenScct.Game.Equipment.ViewModelClips.FirstDrawStEd2;
        var stock = OpenScct.Game.Equipment.ViewModelClips.DataOf(slot);
        anim.Play(0, slot, stock, Backward: true); Assert.Equal(2, anim.Remaining(0), .0001f);
        anim.SetFrame(0, (anim.Data(0).Frames - 1) / 2f, stock);
        Assert.Equal(1, anim.Remaining(0), .0001f); Assert.Equal(1, anim.Elapsed(0, stock), .0001f);
    }
    [Fact]
    public void CoordinateAdapterMatchesOriginalAxes()
    {
        foreach (FRotator rotation in new[] { new FRotator(2000, 8000, -3000), new FRotator(-3000, -16000, 4000), default })
        {
            FRotator restored = StudioSpace.ToUe(StudioSpace.FromUe(rotation));
            Assert.InRange(Math.Abs(FRotator.NormalizeAxis(restored.Yaw - rotation.Yaw)), 0, 12);
            Assert.InRange(Math.Abs(FRotator.NormalizeAxis(restored.Pitch - rotation.Pitch)), 0, 12);
            Assert.InRange(Math.Abs(FRotator.NormalizeAxis(restored.Roll - rotation.Roll)), 0, 12);
        }
    }
}
