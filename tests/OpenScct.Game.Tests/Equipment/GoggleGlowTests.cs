using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The goggle glow: the beam spawn, who sees it, its mode from the goggle actions and its skins (goggle-glow.md).</summary>
public sealed class GoggleGlowTests
{
    private static readonly Name HeadBone = new("B Head");

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Pawn, SGoggle Goggle) Player(bool spy, ENetMode mode = ENetMode.NM_Standalone)
    {
        var scene = new CombatScene(mode);
        (SPlayerController pc, SBasePawn pawn) = scene.Player(spy, new FVector(0f, 0f, 100f));

        // The local player as the login sets it up: its side, and not a remote player's server copy.
        pc.Role = ENetRole.ROLE_Authority;
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pc.SPawnType = pawn.SPawnType;
        SGoggle goggle = scene.Level.SpawnActor(new SGoggle(), pc.Location, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        scene.Level.Info.LocalPlayerController = pc;
        return (scene, pc, pawn, goggle);
    }

    /// <summary>Another player's pawn as a client channel spawns it: the class roles exchanged, remote owned.</summary>
    private static SBasePawn Remote(CombatScene scene, bool spy, FVector at)
    {
        SBasePawn pawn = spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh();
        (pawn.Role, pawn.RemoteRole) = (pawn.RemoteRole, pawn.Role);
        return scene.Level.SpawnActor(pawn, at, default, bRemoteOwned: true)!;
    }

    private static int Count<T>(CombatScene scene)
        where T : Actor
    {
        int n = 0;
        foreach (T a in scene.Level.AllActors<T>())
        {
            n += a.bDeleteMe ? 0 : 1;
        }

        return n;
    }

    /// <summary>The goggle clip's notify where the goggles reach the eyes (as the clip fires it).</summary>
    private static void GogglesReachTheEyes(SBasePawn pawn)
    {
        pawn.bGogglesUp = true;
        PawnVision.AnimEvent_GoggleSwitchDown(pawn);
        pawn.bGogglesUp = false;
    }

    /// <summary>The goggle clip's notify where the goggles leave the eyes.</summary>
    private static void GogglesLeaveTheEyes(SBasePawn pawn)
    {
        pawn.bGogglesUp = false;
        PawnVision.AnimEvent_GoggleSwitchUp(pawn);
        pawn.bGogglesUp = true;
    }

    [Fact]
    public void ASpyWearsTheGreenGlowOnItsHeadFromTheSpawn()
    {
        (_, _, SBasePawn pawn, _) = Player(spy: true);
        SGoggleBeam beam = Assert.IsType<SGoggleBeam_ATT>(pawn.GoggleBeam);
        Assert.False(beam.bHidden);
        Assert.Same(pawn, beam.Owner);
        Assert.Same(pawn, beam.Base);
        Assert.Equal(HeadBone, beam.AttachmentBone);
        Assert.Equal(new FRotator(0, 1500, 0), beam.RelativeRotation);
        Assert.Equal("SGoggle.ATT01_beams", beam.Mesh.Path);
        Assert.True(beam.Skin.IsNone, "the spy beam keeps its own green material until a skin change");
        Assert.Equal(0f, beam.HeatOpacity);
        Assert.Equal(0f, beam.EMFOpacity);
    }

    [Fact]
    public void AMercWearsTheOrangeGlowOnItsHeadFromTheSpawn()
    {
        (_, _, SBasePawn pawn, _) = Player(spy: false);
        SGoggleBeam beam = Assert.IsType<SGoggleBeam_DEF>(pawn.GoggleBeam);
        Assert.False(beam.bHidden);
        Assert.Equal(HeadBone, beam.AttachmentBone);
        Assert.Equal(new FRotator(0, 1250, 0), beam.RelativeRotation);
        Assert.Equal("SGoggle.DEF01_beams", beam.Mesh.Path);
        Assert.Equal(ENetRole.ROLE_None, beam.RemoteRole);
    }

    [Fact]
    public void ADedicatedServerHasNoMercGlowButKeepsTheSpyGlow()
    {
        (_, _, SBasePawn merc, _) = Player(spy: false, ENetMode.NM_DedicatedServer);
        Assert.Null(merc.GoggleBeam);
        (_, _, SBasePawn spy, _) = Player(spy: true, ENetMode.NM_DedicatedServer);
        Assert.NotNull(spy.GoggleBeam);
    }

    [Fact]
    public void AMercsMachineHasNoSpyGlow()
    {
        (CombatScene scene, _, _, _) = Player(spy: false, ENetMode.NM_Client);
        SBasePawn spy = Remote(scene, true, new FVector(500f, 0f, 100f));
        Assert.Null(spy.GoggleBeam);
        Assert.Equal(0, Count<SGoggleBeam_ATT>(scene));
    }

    [Fact]
    public void ASpysMachineShowsEveryGlow()
    {
        (CombatScene scene, _, SBasePawn own, _) = Player(spy: true, ENetMode.NM_Client);
        SBasePawn merc = Remote(scene, false, new FVector(500f, 0f, 100f));
        SBasePawn mate = Remote(scene, true, new FVector(-500f, 0f, 100f));
        Assert.NotNull(own.GoggleBeam);
        Assert.NotNull(merc.GoggleBeam);
        Assert.NotNull(mate.GoggleBeam);
    }

    [Fact]
    public void TheMercGlowTurnsRedInMotionTrackingWhenTheGogglesReachTheEyes()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: false);
        goggle.SwitchMode(1);
        SGoggleBeam beam = pawn.GoggleBeam!;
        Assert.Equal(K.BeamModeMotion, beam.CurrentMode);
        Assert.True(beam.Skin.IsNone, "the skin waits for the clip");
        GogglesReachTheEyes(pawn);
        Assert.Equal(K.GoggleSkinMotion, beam.Skin);
    }

    [Fact]
    public void TheMercGlowTurnsBlueInEmfAtOnceFromAnotherMode()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: false);
        goggle.SwitchMode(1);
        GogglesReachTheEyes(pawn);
        goggle.SwitchMode(2);
        Assert.Equal(K.BeamModeEmf, pawn.GoggleBeam!.CurrentMode);
        Assert.Equal(K.GoggleSkinEmf, pawn.GoggleBeam.Skin);
    }

    [Fact]
    public void TheMercGlowGoesBackToOrangeWhenTheGogglesLeaveTheEyes()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: false);
        goggle.SwitchMode(2);
        GogglesReachTheEyes(pawn);
        goggle.SwitchMode(2);
        Assert.Equal(K.BeamModeNormal, pawn.GoggleBeam!.CurrentMode);
        Assert.Equal(K.GoggleSkinEmf, pawn.GoggleBeam.Skin);
        GogglesLeaveTheEyes(pawn);
        Assert.Equal(K.GoggleSkinOrange, pawn.GoggleBeam.Skin);
    }

    [Fact]
    public void AResetWithoutTheClipPutsTheOrangeBackAtOnce()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: false);
        goggle.SwitchMode(1);
        GogglesReachTheEyes(pawn);
        goggle.ResetVisionMode(true);
        Assert.Equal(K.BeamModeNormal, pawn.GoggleBeam!.CurrentMode);
        Assert.Equal(K.GoggleSkinOrange, pawn.GoggleBeam.Skin);
    }

    [Fact]
    public void TheSpyGlowStaysGreenInEveryMode()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: true);
        goggle.SwitchMode(1);
        Assert.Equal(K.BeamModeNight, pawn.GoggleBeam!.CurrentMode);
        GogglesReachTheEyes(pawn);
        Assert.Equal(K.GoggleSkinGreen, pawn.GoggleBeam.Skin);
        goggle.SwitchMode(2);
        Assert.Equal(K.BeamModeThermal, pawn.GoggleBeam.CurrentMode);
        Assert.Equal(K.GoggleSkinGreen, pawn.GoggleBeam.Skin);
        goggle.SwitchMode(2);
        GogglesLeaveTheEyes(pawn);
        Assert.Equal(K.BeamModeNone, pawn.GoggleBeam.CurrentMode);
        Assert.Equal(K.GoggleSkinGreen, pawn.GoggleBeam.Skin);
    }

    [Fact]
    public void AnotherPlayersMercGlowFollowsTheActionStream()
    {
        // A merc that is not the local player: no goggles here, only the actions that arrive and the clip's notifies.
        (CombatScene scene, _, _, _) = Player(spy: true, ENetMode.NM_Client);
        SBasePawn merc = Remote(scene, false, new FVector(500f, 0f, 100f));
        merc.ExecuteAction(SBasePawn.SA_VISION_MOTION);
        merc.ExecuteAction(SBasePawn.SA_GOG_HEADMVT_ON);
        GogglesReachTheEyes(merc);
        Assert.Equal(K.GoggleSkinMotion, merc.GoggleBeam!.Skin);
        merc.ExecuteAction(SBasePawn.SA_VISION_EMF);
        merc.ExecuteAction(SBasePawn.SA_GOG_SWITCH_WITHOUTANIM);
        Assert.Equal(K.GoggleSkinEmf, merc.GoggleBeam.Skin);
        merc.ExecuteAction(SBasePawn.SA_GOG_HEADMVT_OFF);
        Assert.Equal(K.BeamModeNormal, merc.GoggleBeam.CurrentMode);
        GogglesLeaveTheEyes(merc);
        Assert.Equal(K.GoggleSkinOrange, merc.GoggleBeam.Skin);
    }

    [Fact]
    public void TheLowerActionAloneLeavesTheBeamMode()
    {
        (_, _, SBasePawn pawn, _) = Player(spy: false);
        pawn.ExecuteAction(SBasePawn.SA_GOG_HEADMVT_ON);
        Assert.Equal(0, pawn.GoggleBeam!.CurrentMode);
    }

    [Fact]
    public void ASpyInAVisionModeIsEmfVisibleThroughItsBeamMode()
    {
        (_, _, SBasePawn pawn, SGoggle goggle) = Player(spy: true);
        Assert.False(PawnVision.IsSpyEmfVisible(pawn));
        goggle.SwitchMode(1);
        Assert.True(PawnVision.IsSpyEmfVisible(pawn));
        Assert.True(pawn.bEMFIsOn);
    }

    [Fact]
    public void ADestroyedPawnTakesItsGlowWithIt()
    {
        (CombatScene scene, _, SBasePawn pawn, _) = Player(spy: false);
        SGoggleBeam beam = pawn.GoggleBeam!;
        pawn.Destroy();
        Assert.True(beam.bDeleteMe);
        Assert.Null(pawn.GoggleBeam);
        Assert.Equal(0, Count<SGoggleBeam>(scene));
    }

    [Fact]
    public void TheDebugOffsetsTurnTheBeam()
    {
        (_, _, SBasePawn pawn, _) = Player(spy: true);
        SGoggleBeam beam = pawn.GoggleBeam!;
        beam.Z(100f);
        beam.X(10f);
        Assert.Equal(new FRotator(10, 100, 0), beam.RelativeRotation);
    }
}
