using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Views;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

public sealed class ObservedWeaponViewModelTests
{
    [Fact]
    public void SpectatorRifleUsesRecordedDummyWithoutPossessingOrEquippingPawn()
    {
        var pawn = Merc(new SWeaponDefenseDummy());
        var observed = new ObservedWeaponViewModel();
        SWeaponDefense weapon = Assert.IsType<SWeaponDefense>(observed.Update(pawn, Sim.DeltaTime));
        Assert.Equal("SWeaponFirst.1DEF_01", weapon.Mesh.Path);
        Assert.Equal(ViewModelClips.FirstWaitStFd2, weapon.ViewAnim.Sequence(0));
        Assert.Null(pawn.MyWeapon);
        Assert.Null(pawn.Controller);
        Assert.Equal(0, weapon.ActorId);

        pawn.Velocity = new FVector(200f, 0f, 0f);
        pawn.WeaponDummy!.MuzzleCount++;
        Assert.Same(weapon, observed.Update(pawn, Sim.DeltaTime));
        Assert.Equal(ViewModelClips.FirstWalkStFd2, weapon.ViewAnim.Sequence(0));
        Assert.Equal(weapon.AFirstShooSt, weapon.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
        Assert.True(weapon.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        float left = weapon.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL);
        observed.Update(pawn, 0f);
        Assert.Equal(left, weapon.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL));
        observed.Update(pawn, 2f);
        Assert.Equal(0f, weapon.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Fact]
    public void ObservedSelectionHandlesEveryMercWeaponAndSwitchingWhilePaused()
    {
        var observed = new ObservedWeaponViewModel();
        var pawn = Merc(new SWeaponDefenseDummy_Uzi());
        Assert.IsType<SWeaponDefense_Uzi>(observed.Update(pawn, 0f));
        pawn.WeaponDummy = new SWeaponDefenseDummy_Shotgun();
        Assert.IsType<SWeaponDefense_Shotgun>(observed.Update(pawn, 0f));
        foreach (SidearmDefinition definition in Sidearms.All)
        {
            pawn.SidearmWeapon = new MercSidearm { Dummy = new MercSidearmDummy { Kind = (int)definition.Kind } };
            pawn.NetworkAttachments = new(1, true);
            SWeaponDefense weapon = Assert.IsType<MercSidearm>(observed.Update(pawn, 0f));
            Assert.Equal(definition.Asset + "_first", weapon.Mesh.Path);
            Assert.NotSame(pawn.SidearmWeapon, weapon);
        }
        Assert.Null(observed.Update(null, 0f));
        Assert.Null(observed.Update(new SPawnAttaque(), 0f));
    }

    [Fact]
    public void ReloadTracksRecordedBodyProgressAndStopsWhenCancelled()
    {
        var pawn = Merc(new SWeaponDefenseDummy_Shotgun());
        var observed = new ObservedWeaponViewModel();
        pawn.PawnAnim.Play(SBasePawn.WEAPONCHANNEL, new PawnClip(new Name("ReloStSpFd2S"), 50, 15f), 1f, 0f, false);
        pawn.PawnAnim.SetBlendParams(SBasePawn.WEAPONCHANNEL, 1f);
        pawn.PawnAnim.SetFrame(SBasePawn.WEAPONCHANNEL, 0.49f);
        var action = pawn.PawnAnim[SBasePawn.WEAPONCHANNEL];
        SWeaponDefense weapon = observed.Update(pawn, 0f)!;
        float remaining = weapon.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL);
        Assert.Equal(weapon.AFirstReloSt, weapon.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
        Assert.Equal(ViewModelClips.DataOf(weapon.AFirstReloSt).Seconds(1f) / 2f, remaining, precision: 4);
        observed.Update(pawn, 0f);
        Assert.Equal(remaining, weapon.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL));
        // Even an interrupted/slow body reload keeps its sampled position after the nominal weapon duration.
        observed.Update(pawn, 10f);
        Assert.Equal(remaining, weapon.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL));
        pawn.PawnAnim.SetBlendParams(SBasePawn.WEAPONCHANNEL, 0f);
        observed.Update(pawn, 0f);
        Assert.Equal(0f, weapon.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Fact]
    public void HolsteredDeadAndHiddenPawnsDoNotLeaveAWeaponInTheCamera()
    {
        var observed = new ObservedWeaponViewModel();
        var pawn = Merc(new SWeaponDefenseDummy());
        Assert.NotNull(observed.Update(pawn, 0f));
        pawn.NetworkAttachments = new(0, false);
        Assert.Null(observed.Update(pawn, 0f));
        pawn.NetworkAttachments = new(0, true);
        pawn.Health = 0;
        Assert.Null(observed.Update(pawn, 0f));
        pawn.Health = 100;
        pawn.bHidden = true;
        Assert.Null(observed.Update(pawn, 0f));
    }

    private static SPAWNDEFENSE Merc(SWeaponDefenseDummy dummy) => new()
    {
        Role = ENetRole.ROLE_SimulatedProxy, Health = 100, WeaponLoaded = 2,
        WeaponDummy = dummy, NetworkAttachments = new NetworkPawnAttachments(0, true),
    };
}
