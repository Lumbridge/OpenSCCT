using System;
using OpenScct.Core;

namespace OpenScctGame.Audio;

public readonly record struct AcousticRayHit(bool Hit,float Distance,FVector Normal,int Surface=0,int Occlusion=0);
public readonly record struct ReverbGeometry(bool Valid,float Size,float Volume,float Area,float Openness,
    float Decay,float HFDecayRatio,float LFDecayRatio,float EarlyDelay,float LateDelay,float Diffusion,float WetGain);

/// <summary>Bounded, incremental sampling of the actual visible acoustic volume. All distances enter in UE units.</summary>
public sealed class ReverbGeometryProbe
{
    public const float Range=8192;
    public const int BaseRays=26, MaxRays=32;
    private static readonly FVector[] Basis=MakeBasis();
    private readonly FVector[] _directions=new FVector[MaxRays];
    private readonly float[] _weights=new float[MaxRays];
    private readonly AcousticRayHit[] _hits=new AcousticRayHit[MaxRays];
    private int _count,_next;
    public FVector Origin { get; private set; }
    public ReverbGeometry Geometry { get; private set; }
    public long Rays { get; private set; }
    public long Sweeps { get; private set; }

    public void Reset() { _count=_next=0; Geometry=default; Rays=Sweeps=0; }

    public void Begin(FVector origin,ReadOnlySpan<FVector> apertures,ReadOnlySpan<float> weights)
    {
        Origin=origin; _next=0; _count=BaseRays;
        Basis.CopyTo(_directions,0);
        float total=0;
        for (int i=0;i<Math.Min(apertures.Length,MaxRays-BaseRays);i++)
        {
            FVector direction=(apertures[i]-origin).SafeNormal();
            float weight=Math.Clamp(weights[i],0,0.15f);
            if (direction.SizeSquared()<0.5f || weight<=0) continue;
            _directions[_count]=direction; _weights[_count++]=weight; total+=weight;
        }
        float scale=total>0.35f?0.35f/total:1;
        for (int i=BaseRays;i<_count;i++) _weights[i]*=scale;
        Array.Fill(_weights,(1-Math.Min(total,0.35f))/BaseRays,0,BaseRays);
    }

    public bool NeedsSweep=>_count==0 || _next==_count;
    public FVector NextDirection=>_directions[_next];
    public bool Add(AcousticRayHit hit)
    {
        if (_next>=_count) throw new InvalidOperationException("Begin a reverb geometry sweep before adding hits.");
        _hits[_next++]=hit; Rays++;
        if (_next!=_count) return false;
        Sweeps++;
        ReverbGeometry measured=Estimate(_hits.AsSpan(0,_count),_directions.AsSpan(0,_count),_weights.AsSpan(0,_count));
        if (measured.Valid) Geometry=measured;
        return true;
    }

    public static ReverbGeometry Estimate(ReadOnlySpan<AcousticRayHit> hits,ReadOnlySpan<FVector> directions,ReadOnlySpan<float> weights)
    {
        double volume=0,area=0,absMid=0,absHF=0,absLF=0,open=0,total=0;
        float nearest=float.PositiveInfinity,invalid=0;
        for (int i=0;i<hits.Length;i++)
        {
            AcousticRayHit hit=hits[i]; float w=weights[i]; total+=w;
            if (!hit.Hit) { open+=w; continue; }
            if (!float.IsFinite(hit.Distance) || hit.Distance<1) { invalid+=w; continue; }
            double r=Math.Clamp(hit.Distance,1,Range)*AudioK.DistanceFactor;
            double cosine=Math.Max(0.15,Math.Abs(directions[i]|hit.Normal.SafeNormal()));
            double surface=w*r*r/cosine;
            volume+=w*r*r*r/3; area+=surface;
            var absorption=Absorption(hit.Surface,hit.Occlusion);
            absMid+=surface*absorption.Mid; absHF+=surface*absorption.HF; absLF+=surface*absorption.LF;
            nearest=Math.Min(nearest,(float)r);
        }
        if (total<=0 || invalid>total*0.25) return default; // The anchor is in solid space; keep the previous safe response.
        float openness=(float)Math.Clamp(open/total,0,1);
        if (area<0.001) return new(true,1,0,0,1,0.12f,0.5f,1,0.001f,0.001f,0.2f,0.001f);
        float meanFreePath=(float)(4*volume/area);
        float RT(double absorption)
        {
            double survival=Math.Clamp((1-absorption/area)*(1-openness),0.001,0.999);
            return Math.Clamp((float)(13.81551*meanFreePath/(343*-Math.Log(survival))),0.12f,8);
        }
        float decay=RT(absMid),hf=RT(absHF),lf=RT(absLF);
        // Opposed walls distinguish a bounded room from a ground plane in open air.
        int pairs=0;
        for (int axis=0;axis<3 && hits.Length>=6;axis++)
            if (hits[axis*2].Hit && hits[axis*2+1].Hit) pairs++;
        float support=0.15f+0.85f*pairs/3f;
        float wet=Math.Clamp(MathF.Pow(1-openness,1.7f)*support,0.001f,1);
        float v=(float)(4*Math.PI*volume),s=(float)(4*Math.PI*area);
        return new(true,Math.Clamp(MathF.Cbrt(v),1,100),v,s,openness,decay,
            Math.Clamp(hf/decay,0.1f,2),Math.Clamp(lf/decay,0.1f,2),
            Math.Clamp(2*nearest/343,0.001f,0.25f),Math.Clamp(meanFreePath/343,0.001f,0.1f),
            Math.Clamp(0.25f+0.65f*(1-openness)*pairs/3f,0.1f,1),wet);
    }

    // Broad content categories provide estimates, not measured absorption spectra.
    /// <summary>Shared material energy absorption for room estimation and acoustic inspection tools.</summary>
    public static (float Mid,float HF,float LF) Absorption(int surface,int occlusion)=>
        occlusion==10 || surface is 1 or 13 ? (0.45f,0.7f,0.18f) :
        surface is 4 or 20 or 21 ? (0.35f,0.6f,0.18f) :
        occlusion==7 || surface is 7 or 8 ? (0.18f,0.28f,0.12f) :
        occlusion is 3 or 4 || surface==19 ? (0.08f,0.1f,0.18f) :
        surface is 14 or 16 or 17 ? (0.08f,0.12f,0.05f) : (0.12f,0.25f,0.08f);

    private static FVector[] MakeBasis()
    {
        var result=new FVector[BaseRays];
        result[0]=new(1,0,0); result[1]=new(-1,0,0); result[2]=new(0,1,0);
        result[3]=new(0,-1,0); result[4]=new(0,0,1); result[5]=new(0,0,-1);
        int at=6;
        for (int x=-1;x<=1;x++) for (int y=-1;y<=1;y++) for (int z=-1;z<=1;z++)
            if (Math.Abs(x)+Math.Abs(y)+Math.Abs(z)>1) result[at++]=new FVector(x,y,z).SafeNormal();
        return result;
    }
}
