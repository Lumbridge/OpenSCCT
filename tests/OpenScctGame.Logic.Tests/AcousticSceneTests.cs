using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class AcousticSceneTests
{
    private static AcousticPortal Door(float x, int a, int b) => new(a, b, new FVector(1, 0, 0),
        [new(x,-50,-100), new(x,50,-100), new(x,50,100), new(x,-50,100)]);

    [Fact]
    public void OutsideAmbienceReachesAnInteriorAndAnotherRoomThroughRealOpenings()
    {
        var scene = new AcousticScene([Door(0, 1, 2), Door(300, 2, 3)], new Dictionary<string,int> { ["outside"] = 1 });
        scene.Update(3, new FVector(500, 0, 0), 0, (_, _) => PortalTransmission.Open);
        AcousticPath path = scene.PathTo(1);
        Assert.True(path.Valid);
        Assert.Equal(2, path.Hops);
        Assert.Equal(300, path.FirstOpening.X);
        Assert.Equal(0, path.LastOpening.X);
        Assert.Equal(500, path.Distance, 3);
        Assert.True(AcousticScene.AmbientGain(path) > 0);
        Assert.True(path.HF < 1);
    }

    [Fact]
    public void ClosingADoorReducesAndMufflesThePathRatherThanChangingGameplayHearing()
    {
        var scene = new AcousticScene([Door(0, 1, 2)], new Dictionary<string,int>());
        scene.Update(2, new FVector(100,0,0), 0, (_,_) => PortalTransmission.Open);
        AcousticPath open = scene.PathTo(1);
        scene.Update(2, new FVector(100,0,0), 1, (_,_) => new(0.08f, 0.02f));
        AcousticPath closed = scene.PathTo(1);
        Assert.True(closed.Valid);
        Assert.True(closed.Gain < open.Gain * 0.1);
        Assert.True(closed.HF < open.HF * 0.1);
        Assert.True(closed.Gain > 0);
    }

    [Fact]
    public void PortalDirectionUsesThePolygonAndDuplicatePolygonsDoNotAmplifyAudio()
    {
        AcousticPortal door = Door(0,1,2);
        Assert.Equal(new FVector(0,50,100), door.ClosestPoint(new FVector(100,200,200)));
        var scene = new AcousticScene([door, door], new Dictionary<string,int>());
        scene.Update(2, new FVector(100,0,0), 0, (_,_) => PortalTransmission.Open);
        Assert.Equal(1, scene.PathTo(1).Hops);
        Assert.Equal(1, scene.PathTo(1).Gain);
        Assert.False(scene.PathTo(99).Valid);
    }

    [Fact]
    public void PortalProbesAreBoundedEvenAtUnlimitedFrameRates()
    {
        var portals = new AcousticPortal[100];
        Array.Fill(portals, Door(0,1,2));
        var scene = new AcousticScene(portals, new Dictionary<string,int>());
        for (int i = 0; i < 1000; i++) scene.Update(2, new FVector(100,0,0), i * 0.001, (_,_) => PortalTransmission.Open);
        Assert.InRange(scene.PortalQueries, 200, 244);
    }

    [Fact]
    public void AmbientBoundariesFadeContinuouslyAndCanReverseBeforeFinishing()
    {
        Assert.Equal(0, AmbientTransitions.RangeGain(1000,1000));
        Assert.Equal(1, AmbientTransitions.RangeGain(700,1000));
        Assert.InRange(AmbientTransitions.RangeGain(900,1000), 0.49f, 0.51f);
        Assert.InRange(AmbientTransitions.CutGain(0.05f,0.05f), 0.49f, 0.51f);
        float volume = AmbientTransitions.Approach(1,0,0.016f);
        Assert.InRange(volume,0.95f,1);
        float returned = AmbientTransitions.Approach(volume,1,0.016f);
        Assert.True(returned > volume && returned < 1);
        Assert.Equal(volume, AmbientTransitions.Approach(volume,0,0));
    }

    [Fact]
    public void ReflectedRoomFieldContinuesAcrossTheOpeningAndAttenuatesAlongItsPath()
    {
        var scene=new AcousticScene([Door(0,1,2)],new Dictionary<string,int>());
        scene.Update(1,new(-1,0,0),0,(_,_)=>PortalTransmission.Open);
        Assert.Equal(1,AcousticScene.ReflectedGain(scene.PathTo(1)));
        scene.Update(2,new(0.01f,0,0),1,(_,_)=>PortalTransmission.Open);
        Assert.InRange(AcousticScene.ReflectedGain(scene.PathTo(1)),0.999f,1);
        scene.Update(2,new(650,0,0),2,(_,_)=>PortalTransmission.Open);
        Assert.Equal(0.5f,AcousticScene.ReflectedGain(scene.PathTo(1)),3);
        scene.Update(2,new(650,0,0),3,(_,_)=>new(0.08f,0.02f));
        Assert.Equal(0.04f,AcousticScene.ReflectedGain(scene.PathTo(1)),3);
        Assert.Equal(0,AcousticScene.ReflectedGain(default));
    }

    [Fact]
    public void UnexaminedOpeningsDoNotLeakSoundOnInitialLoad()
    {
        var portals = new AcousticPortal[100];
        Array.Fill(portals, Door(0,10,11));
        portals[99] = Door(0,1,2);
        var scene = new AcousticScene(portals,new Dictionary<string,int>());
        scene.Update(2,new(100,0,0),0,(_,_)=>PortalTransmission.Open);
        Assert.False(scene.PathTo(1).Valid);
        for (int i=1;i<=30;i++) scene.Update(2,new(100,0,0),i*0.02,(_,_)=>PortalTransmission.Open);
        Assert.True(scene.PathTo(1).Valid);
    }
}
