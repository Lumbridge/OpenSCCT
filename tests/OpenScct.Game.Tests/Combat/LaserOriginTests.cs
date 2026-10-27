using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// Where the spy pistol's laser leaves the hand (spec/equipment/spy-shocker.md rules 14.4 and 14a). The drawn beam
/// (game/Scripts/Fx/SpyLaserView.cs) and the simulation's line take the same point of a hand frame: 30 along X,
/// 4 against Y, 8 along Z (8 against Z in the other hand). Playtest (Ryan, 2026-10-25): the drawn beam started at the
/// hand bone itself, 35 uu behind the pistol's muzzle; the play-lasers smoke checks the drawn side.
/// </summary>
public sealed class LaserOriginTests
{
    [Fact]
    public void TheLaserLeavesTheHandThirtyForwardFourLeftEightUp()
    {
        FCoords hand = FCoords.UnitCoords;
        hand.Origin = new FVector(100f, 200f, 300f);
        Assert.Equal(new FVector(130f, 196f, 308f), SWeaponAttaque.LaserStartOn(hand, otherHand: false));
        Assert.Equal(new FVector(130f, 196f, 292f), SWeaponAttaque.LaserStartOn(hand, otherHand: true));
    }

    [Fact]
    public void TheOffsetTurnsWithTheHand()
    {
        // A hand turned a quarter turn to the right: its X is world +Y, its Y world -X.
        FCoords hand = FCoords.FromRotator(new FRotator(0, 16384, 0));
        FVector start = SWeaponAttaque.LaserStartOn(hand, otherHand: false);
        Assert.True(FVector.Dist(start, new FVector(4f, 30f, 8f)) < 0.01f, $"start {start}");
    }

    [Fact]
    public void TheSimulationsLineStartsAtTheSamePointOfThePistolFrame()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponDummy = pistol;
        spy.WeaponLoaded = 2;
        scene.Level.Info.LocalPlayerController = pc;
        pc.Rotation = new FRotator(-2000, 9000, 0);
        pistol.bLaserOn = true;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.PlaceLaser(spy.Location);

        FCoords frame = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
        Assert.Equal(SWeaponAttaque.LaserStartOn(frame, otherHand: false), pistol.LaserBeam!.Location);
    }

    [Fact]
    public void AnotherMachinesSpyAimsHisPistolWithHisReplicatedView()
    {
        // On a client, another player's spy has no controller: his view arrives packed in iYawPitchFromView (pitch in
        // the high word), which also aims his drawn arms. His dot's line used to start from the pawn's rotation, level.
        var scene = new CombatScene(ENetMode.NM_Client);
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponDummy = pistol;
        spy.WeaponLoaded = 2;
        spy.Controller = null;
        spy.Rotation = new FRotator(0, 9000, 0);
        const int pitch = -3000;
        spy.iYawPitchFromView = (pitch << 16) | 9000;
        _ = pc;

        FCoords frame = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
        FCoords want = CombatNatives.SpyPistolCoords(spy, new FRotator(pitch, 9000, 0));
        Assert.True(FVector.Dist(frame.Origin, want.Origin) < 0.01f, $"pistol at {frame.Origin}, want {want.Origin}");
        Assert.InRange((short)frame.XAxis.Rotation().Pitch, (int)(CombatNatives.SpyPistolPitchShare * pitch) - 10, (int)(CombatNatives.SpyPistolPitchShare * pitch) + 30);
    }
}
