using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class TutorialHudTests
{
    private sealed class Profile(bool offline) : IProfileOptions
    {
        public bool ObjectiveSounds => true;
        public byte SelectedMouse => 0;
        public bool OfflineSession => offline;
        public bool DisplayHelpInGame => true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfflineZoneEntryAndExitUpdateTheRealHud(bool helpOnly)
    {
        DeviceScene scene = DeviceScene.Create(ENetMode.NM_Client);
        SZoneTips tip = scene.Add(new SZoneTips { bStayPermanently = false, bShowOnlyWithIngameHelp = helpOnly });
        tip.sMessage_ATT[0] = "Follow";
        tip.sMessage_ATT[1] = "the arrows.";
        scene.Level.SetService<IProfileOptions>(new Profile(true));
        scene.Begin();
        CombatEquipment.Install(scene.Level);
        TestSpy pawn = scene.Player<TestSpy>(default);
        var pc = (SPlayerController)pawn.Controller!;
        scene.Level.Info.LocalPlayerController = pc;
        SHud hud = scene.Spawn(new SHud());
        hud.PlayerOwner = pc;
        pc.myHUD = hud;

        tip.BeginTouch(pawn);
        Assert.Equal("Follow the arrows.", hud.sTutoMap);
        tip.EndTouch(pawn);
        Assert.Equal(string.Empty, hud.sTutoMap);
    }

    [Theory]
    [InlineData(ENetMode.NM_Standalone, false, true, true)]
    [InlineData(ENetMode.NM_Client, true, true, true)]
    [InlineData(ENetMode.NM_ListenServer, true, true, true)]
    [InlineData(ENetMode.NM_Client, false, true, false)]
    [InlineData(ENetMode.NM_ListenServer, false, true, false)]
    [InlineData(ENetMode.NM_ListenServer, true, false, false)]
    public void MapHintsReachOnlyStandaloneOrTheOfflineLocalHud(
        ENetMode mode, bool offline, bool local, bool expected)
    {
        var world = new World(1, mode);
        Level level = world.Level;
        level.SetService<IProfileOptions>(new Profile(offline));
        SPlayerController pc = level.SpawnActor(new SPlayerController(), default, default)!;
        SHud hud = level.SpawnActor(new SHud(), default, default)!;
        hud.PlayerOwner = pc;
        pc.myHUD = hud;
        if (local)
        {
            level.Info.LocalPlayerController = pc;
        }

        hud.AddTutoMapMessage("Follow the arrows.");
        Assert.Equal(expected ? "Follow the arrows." : string.Empty, hud.sTutoMap);
        hud.AddTutoMapMessage(string.Empty);
        Assert.Equal(string.Empty, hud.sTutoMap);
    }
}
