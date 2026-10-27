using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/infirmary-ammo.md and spec/devices/zone-tips.md.</summary>
public sealed class StationAndTipTests
{
    [Fact]
    public void TheMedkitHealsFullyClearsPoisonAndCountsAUseOnlyWhenHurt()
    {
        DeviceScene s = DeviceScene.Create();
        SInfirmary kit = s.Add(new SInfirmary());
        s.Begin();
        TestSpy spy = s.Player<TestSpy>(default);
        var pc = (SPlayerController)spy.Controller!;
        pc.PlayerReplicationInfo = s.Spawn(new PlayerReplicationInfo());
        pc.PlayerReplicationInfo.Stats = new USPlayerStats();
        s.Step();

        spy.Health = 40;
        spy.bPoisonned = true;
        spy.NbPointsRefillable = 7;
        kit.Use(pc);
        Assert.Equal(100, spy.Health);
        Assert.False(spy.bPoisonned);
        Assert.Equal(0, spy.NbPointsRefillable);
        Assert.Equal(1, pc.PlayerReplicationInfo.Stats.MedkitsUsed);
        Assert.Contains(s.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound && e.What == new Name(kit.HealSound.Path) && e.Radius == 100f);

        // Not twice in the same instant; at full health it heals but counts no use.
        spy.Health = 50;
        kit.Use(pc);
        Assert.Equal(50, spy.Health);
        s.Step();
        spy.Health = 100;
        kit.Use(pc);
        Assert.Equal(1, pc.PlayerReplicationInfo.Stats.MedkitsUsed);
    }

    [Fact]
    public void OnlyALivingUserHealsThroughTheMedkitsInteraction()
    {
        DeviceScene s = DeviceScene.Create();
        SInfirmary kit = s.Add(new SInfirmary());
        s.Begin();
        TestMerc merc = s.Player<TestMerc>(default);
        var pc = (SPlayerController)merc.Controller!;
        Assert.IsType<SInfirmaryInteraction>(kit.InteractionObj);
        var entry = s.Spawn(new MedkitEntry());
        entry.SetOwner(kit);
        s.Step();
        merc.Health = 0;
        entry.UseBy(pc);
        Assert.Equal(0, merc.Health);
        merc.Health = 10;
        entry.UseBy(pc);
        Assert.Equal(100, merc.Health);

        // The ammo crate's entry has no health test.
        SAmmoStock crate = s.Spawn(new AmmoEntryHost());
        var ammo = s.Spawn(new AmmoEntry());
        ammo.SetOwner(crate);
        merc.MyWeapon = new SWeapon();
        merc.Health = 0;
        ammo.UseBy(pc);
        Assert.Single(s.Hooks.Reloaded);
    }

    [Fact]
    public void TheAmmoCrateRefillsAMercsWeaponAndABombGadget()
    {
        DeviceScene s = DeviceScene.Create();
        SAmmoStock crate = s.Add(new SAmmoStock());
        s.Begin();
        Assert.False(crate.AcceptStickableGPO());
        Assert.True(crate.bEMFIsOn);
        Assert.Equal(0.6f, crate.HeatIntensity);
        TestMerc merc = s.Player<TestMerc>(default);
        TestSpy spy = s.Player<TestSpy>(default);
        var weapon = new SWeapon();
        merc.MyWeapon = weapon;
        var spyWeapon = new SWeapon();
        spy.MyWeapon = spyWeapon;

        crate.Use((SPlayerController)spy.Controller!);
        Assert.Empty(s.Hooks.Reloaded);

        crate.Use((SPlayerController)merc.Controller!);
        Assert.Equal([weapon], s.Hooks.Reloaded);

        var bomb = new SGadBomb { Charge = 0 };
        s.Hooks.Gadgets[spy] = bomb;
        crate.Use((SPlayerController)spy.Controller!);
        Assert.Equal([weapon, bomb, spyWeapon], s.Hooks.Reloaded);
        Assert.Equal(new SGadBomb().Charge, bomb.Charge);
    }

    private static (DeviceScene S, SZoneTips Tip) Tip(ENetMode mode, System.Action<SZoneTips>? setup = null)
    {
        DeviceScene s = DeviceScene.Create(mode);
        SZoneTips tip = s.Add(new SZoneTips());
        tip.sMessage_ATT[0] = "Hack into";
        tip.sMessage_ATT[1] = "the crane.";
        tip.sMessage_DEF[0] = "Guard";
        tip.sMessage_DEF[1] = "the crane.";
        setup?.Invoke(tip);
        s.Begin();
        return (s, tip);
    }

    private static TPawn WithHud<TPawn>(DeviceScene s)
        where TPawn : SBasePawn, new()
    {
        TPawn p = s.Player<TPawn>(default);
        ((PlayerController)p.Controller!).myHUD = s.Spawn(new SHud());
        return p;
    }

    [Fact]
    public void OfflineAZoneTipShowsItsSidesHintAndClearsItOnExit()
    {
        (DeviceScene s, SZoneTips tip) = Tip(ENetMode.NM_Standalone, t => t.bStayPermanently = false);
        Assert.Equal(300f, tip.CollisionRadius);
        TestSpy spy = WithHud<TestSpy>(s);
        TestMerc merc = WithHud<TestMerc>(s);
        tip.BeginTouch(spy);
        Assert.Same(spy.Controller, tip.TargetController);
        tip.EndTouch(spy);
        Assert.Null(tip.TargetController);
        tip.BeginTouch(merc);
        Assert.Equal(["Hack into the crane.", "Guard the crane."],
            s.Hooks.TutoMessages.ConvertAll(m => m.Message).FindAll(m => m.Length > 0));

        // A tip that does not stay clears its hint with an empty message.
        Assert.Contains(s.Hooks.TutoMessages, m => m.Message.Length == 0);
    }

    [Fact]
    public void HelpOnlyTipsNeedTheProfileOptionAndOnlineNothingShows()
    {
        (DeviceScene s, SZoneTips tip) = Tip(ENetMode.NM_Standalone, t => t.bShowOnlyWithIngameHelp = true);
        TestSpy spy = WithHud<TestSpy>(s);
        tip.BeginTouch(spy);
        Assert.Empty(s.Hooks.TutoMessages);
        tip.EndTouch(spy);
        s.Hooks.Profile = new USPlayerProfile { bDisplayHelpInGame = true };
        tip.BeginTouch(spy);
        Assert.Single(s.Hooks.TutoMessages);

        (DeviceScene online, SZoneTips netTip) = Tip(ENetMode.NM_ListenServer);
        TestSpy host = WithHud<TestSpy>(online);
        netTip.BeginTouch(host);
        Assert.Empty(online.Hooks.TutoMessages);
        Assert.Null(netTip.TargetController);
    }

    [Fact]
    public void APermanentTipStaysAfterLeaving()
    {
        (DeviceScene s, SZoneTips tip) = Tip(ENetMode.NM_Standalone);
        Assert.True(tip.bStayPermanently);
        TestSpy spy = WithHud<TestSpy>(s);
        tip.BeginTouch(spy);
        tip.EndTouch(spy);
        Assert.Single(s.Hooks.TutoMessages);
    }
}

/// <summary>The medkit's entry, with its InitInteract callable as the interaction system calls it.</summary>
internal sealed class MedkitEntry : SInfirmaryInteraction
{
    public void UseBy(SPlayerController user) => InitInteract(user, 0);
}

/// <summary>The ammo crate's entry, likewise.</summary>
internal sealed class AmmoEntry : SAmmoStockInteraction
{
    public void UseBy(SPlayerController user) => InitInteract(user, 0);
}

/// <summary>An ammo crate that can be spawned after the load.</summary>
internal sealed class AmmoEntryHost : SAmmoStock
{
    public AmmoEntryHost()
    {
        bNoDelete = false;
    }
}
