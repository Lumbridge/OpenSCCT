using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// Where the Shocker shoots (spy-shocker.md rules 9 and 11): along the pistol, which the armed spy points where his
/// camera looks (UpdateCamRotation turns the view; the spy never sends a desired rotation).
/// </summary>
public sealed class ShockerAimTests
{
    private sealed class RecordingHitZones : IHitZones
    {
        public FVector Start;
        public FVector End;
        public int Traces;

        public BoneTraceResult TraceBone(Actor Shooter, FVector TraceEnd, FVector TraceStart, bool bRealFire)
        {
            Traces++;
            Start = TraceStart;
            End = TraceEnd;
            return new BoneTraceResult { Actor = Shooter.XLevel.Info, HitLocation = TraceEnd };
        }
    }

    [Fact]
    public void TheShotGoesWhereTheArmedSpyLooks()
    {
        var scene = new CombatScene();
        var zones = new RecordingHitZones();
        scene.Level.SetService<IHitZones>(zones);
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponLoaded = 2;
        scene.Run(0.1f);

        // The camera looks up and to the left; the desired rotation the merc uses is never moved for a spy.
        pc.MyDesiredRotation = default;
        pc.Rotation = new FRotator(2000, -8000, 0);
        pistol.FireWeapon();

        Assert.Equal(1, zones.Traces);
        FVector dir = (zones.End - zones.Start).SafeNormal();
        FVector look = pc.Rotation.Vector();
        Assert.True((dir | look) > 0.999f, $"shot {dir} look {look}");
    }
}
