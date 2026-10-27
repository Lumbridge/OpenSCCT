using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using OpenScct.Game.Tests.Movement;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// What the merc sees and shows during his berserk spin and his rifle butt strike
/// (spec/combat/melee-elbow-charge-berserk.md rules 27 to 34).
/// </summary>
public sealed class MercMeleeViewTests
{
    private static readonly Name s_Berserk360 = new("s_Berserk360");
    private static readonly Name s_PlayerWalkingSt2H = new("s_PlayerWalkingSt2H");
    private static readonly Name s_ElbowShocking = new("s_ElbowShocking");
    private static readonly Name s_Selected = new("s_Selected");
    private static readonly Name AtakStAlTu = new("AtakStAlTu");
    private static readonly Name AtakStAlFd2 = new("AtakStAlFd2");
    private static readonly Name FirstAtakCrFd2 = new("FirstAtakCrFd2");
    private static readonly Name BHead = new("B Head");
    private static readonly PlayerInputFrame NoInput = new();
    private static readonly PlayerInputFrame UseKey = new() { bUsing = 1 };

    private static (PawnRig Rig, SWeaponDefense Rifle) MercWithRifle()
    {
        // The converted characters give the clips their real lengths (AtakStAlTu 1.333 s) when present.
        PawnRigOptions options = (MovesetTestKit.ContentOptions() ?? new PawnRigOptions()) with { Hooks = new CameraMovementHooks() };
        PawnRig rig = Rig(PawnTeam.Merc, options: options);
        rig.Controller.fLastChargingTime = -100f;
        rig.Controller.fLastBerserkTime = -100f;
        var rifle = rig.Level.SpawnActor(new SWeaponDefense(), rig.Pawn.Location, rig.Pawn.Rotation)!;
        rifle.Instigator = rig.Pawn;
        rifle.SetOwner(rig.Pawn);
        rig.Pawn.MyWeapon = rifle;
        rifle.GotoState(s_Selected);
        rifle.bDrawWeapon = true;
        rig.Hold(NoInput, 10);
        return (rig, rifle);
    }

    [Fact]
    public void ABerserkPutsTheMercCameraOnHisHead()
    {
        (PawnRig rig, SWeaponDefense rifle) = MercWithRifle();
        SPlayerController pc = rig.Controller;
        float start = rig.Level.TimeSeconds;
        rig.Step(UseKey);
        Assert.True(pc.IsInState(s_Berserk360), pc.GetStateName().ToString());
        Assert.True(pc.bBehindView);
        Assert.True(pc.bFirstPersonWithPawn);
        SPlayerCam cam = PlayerCamera.Of(pc)!;
        Assert.True(cam.m_bAttachToBone);
        Assert.Equal(BHead, cam.m_parentBone);
        Assert.Equal(new FVector(13f, 0f, 9f), cam.m_vRelativeLocation);
        Assert.False(rifle.bDrawWeapon);
        Assert.Equal(AtakStAlTu, rig.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);

        for (int i = 0; i < 300 && pc.IsInState(s_Berserk360); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(pc.IsInState(s_PlayerWalkingSt2H), pc.GetStateName().ToString());
        if (MovesetTestKit.ContentOptions() is not null)
        {
            // Rule 34: 1.465 s (the clip, its 0.1 s tween and one tick).
            Assert.InRange(rig.Level.TimeSeconds - start, 1.4f, 1.55f);
        }

        Assert.False(pc.bBehindView);
        Assert.False(pc.bFirstPersonWithPawn);
        Assert.False(cam.m_bAttachToBone);
        Assert.True(rifle.bDrawWeapon);
    }

    [Fact]
    public void AMercWhiffPlaysTheRifleButtOnTheViewModel()
    {
        (PawnRig rig, SWeaponDefense rifle) = MercWithRifle();
        SPlayerController pc = rig.Controller;
        rig.Step(UseKey);
        for (int i = 0; i < 300 && pc.IsInState(s_Berserk360); i++)
        {
            rig.Step(NoInput);
        }

        // Within 3 s of the spin: no second berserk, the whiff.
        rig.Step(UseKey);
        Assert.Equal(AtakStAlFd2, rig.Pawn.PawnAnim[19].Sequence);
        Assert.Equal(1f, rig.Pawn.PawnAnim[19].BlendAlpha);
        Assert.True(rifle.IsInState(s_ElbowShocking), rifle.GetStateName().ToString());
        Assert.Equal(FirstAtakCrFd2, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL));
        Assert.True(pc.bNoReticule);

        float start = rig.Level.TimeSeconds;
        for (int i = 0; i < 300 && rifle.IsInState(s_ElbowShocking); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rifle.IsInState(s_Selected), rifle.GetStateName().ToString());
        Assert.InRange(rig.Level.TimeSeconds - start, 1.0f, 1.15f);
        Assert.Equal(ViewModelClips.FirstWaitStFd2, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
    }
}
