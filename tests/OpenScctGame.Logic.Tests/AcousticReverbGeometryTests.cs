using System;
using OpenScct.Core;
using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class AcousticReverbGeometryTests
{
    private static ReverbGeometry Room(FVector half,int surface=5,bool open=false)
    {
        var probe=new ReverbGeometryProbe(); probe.Begin(default,[],[]);
        while (!probe.NeedsSweep)
        {
            FVector d=probe.NextDirection;
            if (open && d.Y>0.4f) { probe.Add(new(false,ReverbGeometryProbe.Range,default)); continue; }
            float[] dir=[d.X,d.Y,d.Z],extent=[half.X,half.Y,half.Z];
            float nearest=float.PositiveInfinity; int axis=0;
            for (int i=0;i<3;i++)
                if (Math.Abs(dir[i])>0.001f && extent[i]/Math.Abs(dir[i])<nearest) { nearest=extent[i]/Math.Abs(dir[i]); axis=i; }
            FVector normal=axis==0?new(-Math.Sign(d.X),0,0):axis==1?new(0,-Math.Sign(d.Y),0):new(0,0,-Math.Sign(d.Z));
            probe.Add(new(true,nearest,normal,surface));
        }
        return probe.Geometry;
    }

    [Fact]
    public void LargerMeasuredRoomsHaveLongerDecayAndLaterReflections()
    {
        ReverbGeometry small=Room(new(160,200,140)),large=Room(new(640,800,560));
        Assert.True(small.Valid && large.Valid);
        Assert.InRange(large.Volume/small.Volume,63.9f,64.1f);
        Assert.True(large.Decay>small.Decay*2);
        Assert.True(large.EarlyDelay>small.EarlyDelay*3.9f);
        Assert.True(large.Size>small.Size*3.9f);
    }

    [Fact]
    public void AnOpeningLetsEnergyEscapeAndClosingItRestoresTheRoom()
    {
        ReverbGeometry closed=Room(new(240,320,180)),open=Room(new(240,320,180),open:true);
        Assert.True(open.Openness>closed.Openness+0.1f);
        Assert.True(open.Decay<closed.Decay*0.8f);
        Assert.True(open.WetGain<closed.WetGain);
        Assert.Equal(closed,Room(new(240,320,180)));
    }

    [Fact]
    public void SoftSurfacesShortenTheMeasuredDecay()
    {
        ReverbGeometry hard=Room(new(300,400,200)),soft=Room(new(300,400,200),surface:1);
        Assert.True(soft.Decay<hard.Decay*0.5f);
        Assert.True(soft.Decay*soft.HFDecayRatio<hard.Decay*hard.HFDecayRatio);
    }

    [Fact]
    public void PartialSweepsDoNotPublishMixedRoomMeasurementsAndSolidAnchorsAreRejected()
    {
        var probe=new ReverbGeometryProbe(); probe.Begin(default,[],[]);
        for (int i=0;i<25;i++) Assert.False(probe.Add(new(false,ReverbGeometryProbe.Range,default)));
        Assert.False(probe.Geometry.Valid);
        Assert.True(probe.Add(new(false,ReverbGeometryProbe.Range,default)));
        Assert.Equal(1,probe.Geometry.Openness);
        Assert.True(probe.Geometry.WetGain<0.01f);
        ReverbGeometry safe=probe.Geometry;
        probe.Begin(default,[],[]);
        for (int i=0;i<26;i++) probe.Add(new(true,0,new(0,0,1)));
        Assert.Equal(safe,probe.Geometry);
    }

    [Fact]
    public void DerivedPacketsRemainValidAndApproachTheirTargetWithoutAnInstantJump()
    {
        var authored=EaxEnvironment.NullZoneEffect;
        var target=authored.WithGeometry(Room(new(700,900,500)));
        Assert.True(target.IsOriginalValid);
        EaxEnvironment unchanged=authored.Approach(target,0);
        Assert.Equal(authored.ToEaxArray(),unchanged.ToEaxArray());
        EaxEnvironment next=authored.Approach(target,0.1f);
        Assert.InRange(next.DecayTime,Math.Min(authored.DecayTime,target.DecayTime),Math.Max(authored.DecayTime,target.DecayTime));
        Assert.NotEqual(target.DecayTime,next.DecayTime);
        Assert.All(next.ToEaxArray(),value=>Assert.True(float.IsFinite(value)));
        foreach (int index in new[] {0,3,4,5,9,14,27}) Assert.Equal(MathF.Truncate(next.ToEaxArray()[index]),next.ToEaxArray()[index]);
    }

    [Fact]
    public void PortalTargetsIncludeSmallOpeningsInTheGeometrySweep()
    {
        var scene=new AcousticScene([new(1,2,new(0,1,0),
            [new(40,300,-80),new(120,300,-80),new(120,300,80),new(40,300,80)])],new System.Collections.Generic.Dictionary<string,int>());
        Span<FVector> points=stackalloc FVector[6]; Span<float> weights=stackalloc float[6];
        Assert.Equal(1,scene.ReverbApertures(1,default,points,weights));
        Assert.True(weights[0]>0);
        var probe=new ReverbGeometryProbe(); probe.Begin(default,points[..1],weights[..1]);
        for (int i=0;i<26;i++) probe.Add(new(true,300,-probe.NextDirection,5));
        Assert.False(probe.Geometry.Valid);
        probe.Add(new(false,ReverbGeometryProbe.Range,default));
        Assert.True(probe.Geometry.Openness>0);
        Assert.Equal(27,probe.Rays);
    }

    [Fact]
    public void AnOpeningIntoAnotherEnclosedRoomCanIncreaseTheMeasuredVolumeWithoutBecomingOutdoors()
    {
        ReverbGeometry Measure(float passageDistance)
        {
            var probe=new ReverbGeometryProbe();
            probe.Begin(default,[new(0,300,0)],[0.08f]);
            for (int i=0;i<26;i++) probe.Add(new(true,300,-probe.NextDirection,5));
            probe.Add(new(true,passageDistance,new(0,-1,0),5));
            return probe.Geometry;
        }
        ReverbGeometry closed=Measure(300),joined=Measure(1600);
        Assert.Equal(0,joined.Openness);
        Assert.True(joined.Volume>closed.Volume*2);
        Assert.True(joined.Decay>closed.Decay);
    }

    [Fact]
    public void DuplicatePortalPolygonsDoNotCountAsMultipleOpenings()
    {
        var portal=new AcousticPortal(1,2,new(1,0,0),[new(0,-50,-50),new(0,50,-50),new(0,50,50),new(0,-50,50)]);
        var scene=new AcousticScene([portal,portal],new System.Collections.Generic.Dictionary<string,int>());
        Span<FVector> points=stackalloc FVector[6]; Span<float> weights=stackalloc float[6];
        Assert.Equal(1,scene.ReverbApertures(1,new(100,0,0),points,weights));
    }
}
