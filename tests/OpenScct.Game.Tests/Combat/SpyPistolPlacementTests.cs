using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The drawn spy pistol's placement, which the shocker's shot and laser start from (spec/equipment/spy-shocker.md rule
/// 14a): the original's pistol actor at B R Hand, read in the sandbox (local/spyhand/capture.py) for a crouched and a
/// standing spy aiming at several pitches. Playtest: the eye-height port lowered the crouched spy's estimated pistol
/// (eye plus 60 uu along the view) from 93 to 40 uu above his centre, so the shots of play-shocker hit a low wall. The
/// original holds the pistol 38 uu above the centre crouched and 46 standing, 16 and 15 uu right of the view line,
/// pitched to 0.85 of the view.
/// </summary>
public sealed class SpyPistolPlacementTests
{
    private static (CombatScene Scene, SPlayerController PC, SPawnAttaque Spy, SStickyShocker Pistol) Spy(FVector location, bool crouched)
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, location);
        var spy = (SPawnAttaque)pawn;
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponDummy = pistol;
        spy.WeaponLoaded = 2;
        spy.bIsCrouched = crouched;
        spy.Location = location;
        scene.Level.Info.LocalPlayerController = pc;
        return (scene, pc, spy, pistol);
    }

    // (crouched, Location Z, view pitch, view yaw, pistol location, pistol pitch, laser start) from the capture.
    public static TheoryData<bool, float, int, int, float, float, float, int, float, float, float> Captured => new()
    {
        { true, -1092.495f, -144, 16383, -508.241f, -2034.218f, -1054.186f, -122, -504.217f, -2004.130f, -1046.553f },
        { true, -1092.495f, -1565, 16383, -508.248f, -2033.140f, -1056.492f, -1329, -504.235f, -2002.371f, -1052.386f },
        { true, -1092.495f, -3527, 16383, -508.240f, -2032.079f, -1059.884f, -2993, -504.216f, -2001.049f, -1060.718f },
        { true, -1092.495f, -1330, 10557, -506.458f, -2024.489f, -1056.102f, -1130, -486.784f, -2000.601f, -1051.408f },
        { false, -1054.995f, -138, 16383, -506.703f, -2035.177f, -1008.726f, -87, -502.683f, -2005.116f, -1000.996f },
        { false, -1054.995f, -4639, 16383, -506.710f, -2031.481f, -1015.577f, -3912, -502.700f, -2000.645f, -1019.141f },
        { false, -1054.995f, 2572, 17078, -506.237f, -2039.482f, -1005.411f, 2215, -504.066f, -2011.637f, -991.284f },
    };

    [Theory]
    [MemberData(nameof(Captured))]
    public void ThePistolAndTheLaserStartAreWhereTheOriginalHoldsThem(bool crouched, float z, int pitch, int yaw,
        float px, float py, float pz, int pistolPitch, float lx, float ly, float lz)
    {
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, SStickyShocker pistol) = Spy(new FVector(-492f, -2032f, z), crouched);
        pc.Rotation = new FRotator(pitch, yaw, 0);

        FCoords hand = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
        Assert.True(FVector.Dist(hand.Origin, new FVector(px, py, pz)) < 1f, $"pistol at {hand.Origin}, original ({px}, {py}, {pz})");
        Assert.InRange((short)hand.XAxis.Rotation().Pitch - pistolPitch, -30, 30);

        // The laser's start (rule 14: the hand + 30 X - 4 Y + 8 Z), as the beam is placed for the local player.
        pistol.bLaserOn = true;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.PlaceLaser(spy.Location);
        Assert.NotNull(pistol.LaserBeam);
        FVector start = pistol.LaserBeam!.Location;
        Assert.True(FVector.Dist(start, new FVector(lx, ly, lz)) < 1.5f, $"laser start at {start}, original ({lx}, {ly}, {lz})");
        _ = scene;
    }

    [Fact]
    public void TheCrouchedSpysShotStartsAtTheHandNotAtHisEye()
    {
        // The eye height a keyed crouch gives the spy (40) or the start's (55.5) does not move the pistol.
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, SStickyShocker pistol) = Spy(new FVector(0f, 0f, 50f), crouched: true);
        pc.Rotation = new FRotator(0, 0, 0);
        spy.EyeHeight = 40f;
        FVector low = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone).Origin;
        spy.EyeHeight = 55.5f;
        FVector high = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone).Origin;
        Assert.Equal(low, high);
        Assert.InRange(low.Z - spy.Location.Z, 38f, 39f);
        _ = scene;
    }
}
