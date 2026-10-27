using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/animated-mesh.md: SAnimatedMesh states, channel timing and notifies.</summary>
public sealed class AnimatedMeshTests
{
    private const float Tick = 1f / 60f;

    private static readonly AssetRef MeshRef = new("Test.Prop");

    /// <summary>Sequence A: 61 frames at 30 fps (2 s to its end), an event notify at the middle; B: 31 frames at 30 fps.</summary>
    private static (DeviceScene S, SAnimatedMesh Mesh, EventProbe Mid, Sender Switch) Prop(System.Action<SAnimatedMesh>? setup = null)
    {
        DeviceScene s = DeviceScene.Create();
        s.Animations.Add(MeshRef, new MeshAnimSequence("A", 61, 30f, [new MeshAnimNotify(0.5f, new UAnimNotify_Trigger { EventName = "mid" })]));
        s.Animations.Add(MeshRef, new MeshAnimSequence("B", 31, 30f));
        Sender sw = s.Add(new Sender());
        SAnimatedMesh mesh = s.Add(new SAnimatedMesh { Mesh = MeshRef }, tag: "prop");
        mesh.Animations.Add("A");
        setup?.Invoke(mesh);
        EventProbe mid = s.Add(new EventProbe(), tag: "mid");
        s.Begin();
        return (s, mesh, mid, sw);
    }

    private static float Fire(DeviceScene s, Sender sw, Pawn? instigator = null, bool untrigger = false)
    {
        if (untrigger)
        {
            s.Level.UntriggerEvent("prop", sw, instigator);
            s.Step();
            return s.Time - Tick;
        }

        sw.FireNextTick("prop", instigator);
        s.Step();
        return sw.FiredAt;
    }

    [Fact]
    public void StartUpShowsTheFirstFrameStillAndPadsTheLoopCounts()
    {
        (DeviceScene s, SAnimatedMesh mesh, _, _) = Prop(m =>
        {
            m.Animations.Add("B");
            m.bChainedAnims = true;
        });
        Assert.True(mesh.IsInState("s_Wait"));
        Assert.Equal(EDrawType.DT_Mesh, mesh.DrawType);
        Assert.Equal(EPhysics.PHYS_None, mesh.Physics);
        Assert.Equal([1, 0], mesh.LoopStatus_0forInfinite);
        Assert.Equal(new Name("A"), mesh.AnimSequence!.Name);
        Assert.False(mesh.IsAnimating);
        Assert.Equal(0f, mesh.AnimFrame);
        s.StepSeconds(1f);
        Assert.Equal(0f, mesh.AnimFrame);
    }

    [Fact]
    public void ATriggerPlaysTheSequenceFiresItsNotifyOnTimeAndHoldsTheLastFrame()
    {
        (DeviceScene s, SAnimatedMesh mesh, EventProbe mid, Sender sw) = Prop(m => m.bHidden = true);
        TestSpy spy = s.Player<TestSpy>(default);
        float t0 = Fire(s, sw, spy);
        Assert.True(mesh.IsInState("s_AnimateForward"));
        Assert.False(mesh.bHidden);
        Assert.Equal(EPhysics.PHYS_RootMotion, mesh.Physics);
        Assert.Same(spy, mesh.Instigator);
        s.StepSeconds(3f);

        // Notify at t 0.5 of 61 frames at 30 fps: 1.0167 s; the end at (N - 1) / R = 2 s.
        (float when, _, _, Actor? other, Pawn? by) = Assert.Single(mid.Calls);
        V.Near(0.5f * 61f / 30f, when - t0, Tick + 1e-3f);
        Assert.Same(mesh, other);
        Assert.Null(by);
        Assert.True(mesh.IsInState("s_Wait"));
        Assert.False(mesh.IsAnimating);
        Assert.Equal(60f / 61f, mesh.AnimFrame, 5);
    }

    [Fact]
    public void ATriggerWithoutAnInstigatorKeepsTheOldOneAndARetriggerReplaysTheNotifies()
    {
        (DeviceScene s, SAnimatedMesh mesh, EventProbe mid, Sender sw) = Prop();
        TestSpy spy = s.Player<TestSpy>(default);
        Fire(s, sw, spy);
        s.StepSeconds(2.5f);
        Fire(s, sw);
        Assert.Same(spy, mesh.Instigator);
        Assert.InRange(mesh.AnimFrame, 0f, 0.01f);
        s.StepSeconds(2.5f);
        Assert.Equal(2, mid.Calls.Count);
    }

    [Fact]
    public void ATriggerDuringTheSequenceOnlyAsksToContinue()
    {
        (DeviceScene s, SAnimatedMesh mesh, EventProbe mid, Sender sw) = Prop();
        Fire(s, sw);
        s.StepSeconds(0.5f);
        Fire(s, sw);
        Assert.True(mesh.bShouldContinue);
        s.StepSeconds(2f);
        Assert.Single(mid.Calls);
        Assert.False(mesh.bShouldContinue);
        Assert.True(mesh.IsInState("s_Wait"));
    }

    [Fact]
    public void LoopCountsReplayASequenceAndZeroLoopsForever()
    {
        (DeviceScene s, SAnimatedMesh mesh, EventProbe mid, Sender sw) = Prop(m => m.LoopStatus_0forInfinite.Add(3));
        Fire(s, sw);
        s.StepSeconds(7f);
        Assert.Equal(3, mid.Calls.Count);
        Assert.True(mesh.IsInState("s_Wait"));

        (DeviceScene s2, SAnimatedMesh forever, EventProbe mid2, Sender sw2) = Prop(m => m.LoopStatus_0forInfinite.Add(0));
        Fire(s2, sw2);
        s2.StepSeconds(8f);
        Assert.Equal(4, mid2.Calls.Count);
        Assert.True(forever.IsInState("s_AnimateForward"));

        // An UnTrigger stops it at the next end.
        s2.Level.UntriggerEvent("prop", sw2, null);
        Assert.True(forever.bShouldStop);
        s2.StepSeconds(2.1f);
        Assert.True(forever.IsInState("s_Wait"));
    }

    [Fact]
    public void ChainedSequencesPlayInTurnAndWrapToWait()
    {
        (DeviceScene s, SAnimatedMesh mesh, _, Sender sw) = Prop(m =>
        {
            m.Animations.Add("B");
            m.bChainedAnims = true;
            m.LoopStatus_0forInfinite.Add(1);
            m.LoopStatus_0forInfinite.Add(1);
        });
        Fire(s, sw);
        s.StepSeconds(2.1f);
        Assert.Equal(new Name("B"), mesh.AnimSequence!.Name);
        Assert.Equal(1, mesh.iCurrentAnim);
        s.StepSeconds(1.1f);
        Assert.True(mesh.IsInState("s_Wait"));
        Assert.Equal(0, mesh.iCurrentAnim);
    }

    [Fact]
    public void AnUnTriggerInWaitPlaysThePreviousSequenceBackwards()
    {
        (DeviceScene s, SAnimatedMesh mesh, EventProbe mid, Sender sw) = Prop();
        Fire(s, sw, untrigger: true);
        Assert.True(mesh.IsInState("s_AnimateBackward"));
        Assert.True(mesh.AnimFrame > 0.9f);
        s.StepSeconds(2.1f);
        Assert.True(mesh.IsInState("s_Wait"));
        Assert.Equal(0f, mesh.AnimFrame);
        Assert.Single(mid.Calls);
    }

    [Fact]
    public void SoundNotifiesPlayAtTheProp()
    {
        DeviceScene s = DeviceScene.Create();
        s.Animations.Add(MeshRef, new MeshAnimSequence("A", 11, 10f,
            [new MeshAnimNotify(0f, new UAnimNotify_Sound { Sound = new AssetRef("start"), Volume = 4f, Radius = 200 })]));
        Sender sw = s.Add(new Sender());
        SAnimatedMesh mesh = s.Add(new SAnimatedMesh { Mesh = MeshRef }, V.At(5f, 0f, 0f), tag: "prop");
        mesh.Animations.Add("A");
        s.Begin();
        Fire(s, sw);
        SimEvent e = Assert.Single(s.Sounds.Of(SimEventKind.PlaySound, "start"));
        Assert.Equal(4f, e.Volume);
        Assert.Equal(200f, e.Radius);
        Assert.Equal(mesh.ActorId, e.ActorId);
    }

    [Fact]
    public void AnUnknownSequenceLeavesTheChannelAlone()
    {
        (DeviceScene s, SAnimatedMesh mesh, _, Sender sw) = Prop(m => m.Animations[0] = "Missing");
        Assert.Null(mesh.AnimSequence);
        Fire(s, sw);
        Assert.False(mesh.IsAnimating);
        Assert.True(mesh.IsInState("s_AnimateForward"));
    }

    [Fact]
    public void AttachmentsHangOnTheirBones()
    {
        DeviceScene s = DeviceScene.Create();
        Sender flare = s.Add(new Sender());
        SAnimatedMesh mesh = s.Add(new SAnimatedMesh { Mesh = MeshRef });
        mesh.ActorsToAttach.Add(new FAttachActor { BoneName = "Dummy02", ActorName = flare });
        s.Begin();
        Assert.Same(mesh, flare.Base);
        Assert.Equal(new Name("Dummy02"), flare.AttachmentBone);
        Assert.Contains(flare, (IEnumerable<Actor>)mesh.Attached);
    }
}
