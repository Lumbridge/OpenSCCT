using System;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using PracticeRules = OpenScct.Game.Modes.PracticeRules;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class TestRangePracticeTests
{
    [Theory]
    [InlineData("TestRange", PlayRules.MapSettings)]
    [InlineData("testrange", PlayRules.PlayLevel)]
    public void LocalLaunchEnablesPracticeWithoutChangingTheCallersRules(string map, PlayRules rules)
    {
        var supplied = new PlaySettings { Map = map, Rules = rules, ResponsiveSmoke = true, Bots = 2 };
        PlaySettings launched = supplied.ForLocalLaunch();
        Assert.True(launched.UnlimitedGear);
        Assert.True(launched.OfflineRules);
        Assert.Equal(rules, launched.Rules);
        Assert.True(launched.ResponsiveSmoke);
        Assert.Equal(2, launched.Bots);
        // Online/demo sessions receive the original settings and never call the local-launch boundary.
        Assert.False(supplied.UnlimitedGear);
        Assert.False(supplied.OfflineRules);
    }

    [Theory]
    [InlineData("SteeE", false)]
    [InlineData("SteeE", true)]
    [InlineData("TestBox", false)]
    public void OtherMapsRetainTheirExplicitSettings(string map, bool unlimited)
    {
        var supplied = new PlaySettings { Map = map, UnlimitedGear = unlimited };
        Assert.Same(supplied, supplied.ForLocalLaunch());
    }

    [Theory]
    [InlineData(PlaySide.Spy, PlayRules.MapSettings)]
    [InlineData(PlaySide.Merc, PlayRules.PlayLevel)]
    public void TestRangeLaunchKeepsAmmoAndGrenadesOnBothMachines(PlaySide side, PlayRules rules)
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = "TestRange", Side = side, Rules = rules, HostMode = PlayHostMode.Inline,
            }.ForLocalLaunch(), clock);
        }
        catch (FileNotFoundException error) { Assert.Skip("TestRange content required: " + error.Message); return; }
        using (session)
        {
            for (int i = 0; i < 10 * Sim.Hz && session.LocalPawn is null; i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
            }
            Assert.NotNull(session.LocalPawn);
            Assert.True(session.Settings.UnlimitedGear);
            CheckResources(session.Host.World.Level);
            CheckResources(session.Client.World.Level);
        }
    }

    private static void CheckResources(Level level)
    {
        Assert.True(level.GetService<PracticeRules>()?.On);
        SGadSmokeGrenade smoke = level.Spawn<SGadSmokeGrenade>()!;
        smoke.Charge = 3;
        SWeaponDefense rifle = level.Spawn<SWeaponDefense>()!;
        rifle.ClipAmmo = 5; rifle.Ammo = 30;
        for (int i = 0; i < 12; i++)
        {
            smoke.DecCharge(); smoke.ServerDecCharge();
            rifle.FireWeapon();
        }
        Assert.Equal(3, smoke.Charge);
        Assert.Equal(5, rifle.ClipAmmo);
        Assert.Equal(30, rifle.Ammo);
    }
}
