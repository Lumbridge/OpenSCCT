using System;
using OpenScct.Core;
using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class AcousticCornerPathTests
{
    private static CornerPathSearch Search()
    {
        var search=new CornerPathSearch();
        search.Begin(new(200,0,0),new(-200,0,0),new(16,0,0),new(-16,0,0),new(1,0,0),new(-1,0,0));
        return search;
    }

    [Fact]
    public void ARouteIsPublishedOnlyAfterEverySegmentWasCheckedWithinTheBudget()
    {
        CornerPathSearch search=Search();
        Assert.Equal(2,search.Step(2,(_,_)=>true));
        Assert.False(search.PathFrom(search.Listener).Valid);
        Assert.Equal(1,search.Step(1,(_,_)=>true));
        Assert.True(search.PathFrom(search.Listener).Valid);
        Assert.Equal(3,search.Queries);
        search.Reset();
        Assert.False(search.Initialized);
        Assert.False(search.PathFrom(default).Valid);
    }

    [Fact]
    public void SameZoneSoundsCanTravelAroundAnActualWallEdge()
    {
        CornerPathSearch search=Search();
        bool Clear(FVector a,FVector b)=>!HitsWall(a,b,100);
        Assert.False(Clear(search.Listener,search.Source));
        for (int frame=0;frame<30 && !search.Complete;frame++) Assert.InRange(search.Step(1,Clear),0,1);
        AcousticPath path=search.PathFrom(search.Listener);
        Assert.True(search.Complete && path.Valid);
        Assert.True(Math.Abs(path.FirstOpening.Y)>100);
        Assert.True(Clear(search.Listener,path.FirstOpening));
        Assert.True(Clear(path.FirstOpening,path.LastOpening));
        Assert.True(Clear(path.LastOpening,search.Source));
        Assert.True(path.Distance+FVector.Dist(path.LastOpening,search.Source)>400);
        Assert.InRange(search.Queries,1,30);
    }

    [Fact]
    public void EnclosedWallsDoNotBecomeInventedOpeningsAndTheSearchTerminates()
    {
        CornerPathSearch search=Search();
        for (int frame=0;frame<40 && !search.Complete;frame++) search.Step(1,(a,b)=>!HitsWall(a,b,100000));
        Assert.True(search.Complete);
        Assert.False(search.PathFrom(search.Listener).Valid);
        Assert.InRange(search.Queries,1,30);
        Assert.Equal(0,search.Step(12,(_,_)=>true));
    }

    private static bool HitsWall(FVector a,FVector b,float extent)
    {
        float lo=0,hi=1;
        float[] start=[a.X,a.Y,a.Z],delta=[b.X-a.X,b.Y-a.Y,b.Z-a.Z],half=[16,extent,100000];
        for (int axis=0;axis<3;axis++)
        {
            if (Math.Abs(delta[axis])<0.00001f) { if (Math.Abs(start[axis])>half[axis]) return false; continue; }
            float x=(-half[axis]-start[axis])/delta[axis],y=(half[axis]-start[axis])/delta[axis];
            lo=Math.Max(lo,Math.Min(x,y)); hi=Math.Min(hi,Math.Max(x,y));
            if (lo>hi) return false;
        }
        return true;
    }
}
