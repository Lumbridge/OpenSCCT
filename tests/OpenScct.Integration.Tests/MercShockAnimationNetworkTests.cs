using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercShockAnimationNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(NetProfile.Stock, false)]
    [InlineData(NetProfile.Stock, true)]
    [InlineData(NetProfile.Enhanced, false)]
    [InlineData(NetProfile.Enhanced, true)]
    public void OwnersShockLoopClearsAfterRecoveryOrChargeInterruption(NetProfile profile, bool charging)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 973, SimLinkProfile.Ping200);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
            Profile = profile,
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }

        Tick(900);
        var client = match.Bots[2];
        var owner = Assert.IsType<SPlayerController>(client.Controller);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        var rifle = Assert.IsType<SWeaponDefense>(pawn.MyWeapon);
        var authority = match.ServerController(client)!;
        if (charging) owner.eventChangeState(new Name("s_PlayerCharging"));
        authority.TimeShock = -100f;
        authority.Checkshock(2, true);

        // The reliable server shock must first reach the owner's actual first-person weapon.
        for (int i = 0; i < Sim.Hz && !rifle.IsInState("s_Dmg_Electric"); i++) Tick(1);
        Assert.True(rifle.IsInState("s_Dmg_Electric"));
        Assert.Equal(ViewModelClips.FirstElecStFd2, rifle.ViewAnim.Sequence(SWeaponDefense.PAINCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
        Assert.Equal(1f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));

        // A charge's existing timer can replace the electric state before the shock expires.
        // Otherwise the ordinary freeze timer exits it. Both exits must remove the overlay.
        for (int i = 0; i < 6 * Sim.Hz && rifle.IsInState("s_Dmg_Electric"); i++) Tick(1);
        Assert.False(rifle.IsInState("s_Dmg_Electric"));
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));

        Tick(10 * Sim.Hz);
        Assert.False(pawn.bStickyShocking);
        Assert.Equal(0, owner.iDontUpdatePadInput);
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
        Assert.Equal(EClientState.Joined, client.Session.State);
    }
}
