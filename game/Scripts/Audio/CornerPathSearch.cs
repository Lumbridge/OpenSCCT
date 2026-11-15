using System;
using OpenScct.Core;

namespace OpenScctGame.Audio;

/// <summary>
/// A bounded local edge search when a wall blocks two points in one acoustic
/// zone. Every segment of a candidate must be clear; no path is inferred from
/// zone membership. Incremental work and the result are cached per emitter.
/// </summary>
public sealed class CornerPathSearch
{
    private static readonly float[] Radii=[64,128,256,512,1024];
    private FVector _near,_far,_nearNormal,_farNormal,_tangent,_first,_last;
    private int _candidate,_segment;
    private AcousticPath _path;
    public FVector Listener { get; private set; }
    public FVector Source { get; private set; }
    public bool Initialized { get; private set; }
    public bool Complete { get; private set; }
    public int Queries { get; private set; }

    public void Reset() { Initialized=false; Complete=false; _path=default; }

    public void Begin(FVector listener,FVector source,FVector near,FVector far,FVector nearNormal,FVector farNormal)
    {
        Listener=listener; Source=source; _near=near; _far=far;
        _nearNormal=nearNormal.SafeNormal(); _farNormal=farNormal.SafeNormal();
        if (_nearNormal.SizeSquared()<0.5f) _nearNormal=(listener-source).SafeNormal();
        if (_farNormal.SizeSquared()<0.5f) _farNormal=-_nearNormal;
        _tangent=(_nearNormal ^ new FVector(0,0,1)).SafeNormal();
        if (_tangent.SizeSquared()<0.5f) _tangent=(_nearNormal ^ new FVector(1,0,0)).SafeNormal();
        _candidate=_segment=Queries=0; _path=default;
        Initialized=true; Complete=_tangent.SizeSquared()<0.5f;
    }

    public int Step(int budget,Func<FVector,FVector,bool> clear)
    {
        int used=0;
        while (!Complete && used<budget)
        {
            if (_segment==0)
            {
                float radius=Radii[_candidate/2]*((_candidate&1)==0?1:-1);
                _first=_near+_nearNormal*8+_tangent*radius;
                _last=_far+_farNormal*8+_tangent*radius;
            }
            FVector a=_segment==0?Listener:_segment==1?_first:_last;
            FVector b=_segment==0?_first:_segment==1?_last:Source;
            used++; Queries++;
            if (!clear(a,b)) { Next(); continue; }
            if (++_segment<3) continue;
            float distance=FVector.Dist(Listener,_first)+FVector.Dist(_first,_last);
            float total=distance+FVector.Dist(_last,Source);
            if (total<=6000 && (!_path.Valid || total<_path.Distance+FVector.Dist(_path.LastOpening,Source)))
                _path=new(true,distance,0.85f,0.8f,1,_first,_last);
            Next();
        }
        return used;
    }

    private void Next()
    {
        _segment=0; _candidate++;
        Complete=_candidate>=Radii.Length*2 || ((_candidate&1)==0 && _path.Valid);
    }

    public AcousticPath PathFrom(FVector listener) => !_path.Valid ? default : _path with {
        Distance=FVector.Dist(listener,_path.FirstOpening)+FVector.Dist(_path.FirstOpening,_path.LastOpening) };
}
