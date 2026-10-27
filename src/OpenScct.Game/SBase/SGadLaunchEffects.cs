using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SGadLaunchEffects: the 0.1 s launch flash of a throw (cosmetic).</summary>
public partial class SGadLaunchEffects
{
    [Uc("SBase.SGadLaunchEffects.PostBeginPlay", Sha1 = "d0a34fa8f9cc29eafb6f7399146f8487070d3878")]
    [PortOf("uc:SBase.SGadLaunchEffects:PostBeginPlay", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        float scale = K.LaunchFlashScaleBase + K.LaunchFlashScaleRandom * FRand();
        DrawScale3D = new FVector(scale, scale, scale);
        Rotation = new FRotator(Rotation.Pitch, Rotation.Yaw, SvmMath.Trunc(Rotation.Roll * FRand() * 16000f));
        SetTimer(K.LaunchFlashLife, false);
    }

    [Uc("SBase.SGadLaunchEffects.Timer", Sha1 = "842d47d9443f6db763a74955d860559be935f9af")]
    [PortOf("uc:SBase.SGadLaunchEffects:Timer", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void Timer() => Destroy();
}
