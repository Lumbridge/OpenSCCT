using System;
using System.Linq;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Options;
using OpenScctGame.UI;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>The twelve-choice picker and all four independent Armoury sections with the actual content.</summary>
public sealed class SidearmMenuSmoke : SmokeTest
{
    public const string TestName = "sidearm-menus";
    public override string Name => TestName;
    private MenuPage? _page;
    private int _phase, _frames, _pistol;

    public override SmokeResult Step(SmokeContext context)
    {
        if (++_frames < 8) return SmokeResult.Running;
        _frames = 0;
        switch (_phase)
        {
            case 0:
                var team = new STeamDefenseur();
                var model = new GearMenuModel(false, 4, team.ClassInventory.Select(c => c.Path!).ToArray(), 255,
                    team.ClassWeapon.Select(c => c.Path!).ToArray(), new LoadoutSettings(), false);
                _page = new GearMenu(model);
                context.Tree.Root.AddChild(_page);
                _page._Process(3);
                _phase++;
                break;
            case 1:
                ((BaseButton)_page!.FindChild("Slot1", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                _phase++;
                break;
            case 2:
                for (int i = 1; i <= 12; i++)
                    if (_page!.FindChild("Option" + i, true, false) is null)
                        return SmokeResult.Fail("picker option " + i + " is missing");
                MenuPage.Shot(_page!, "sidearm-picker-locked");
                _page!.QueueFree();
                _page = new ArmouryScreen();
                context.Tree.Root.AddChild(_page);
                _page._Process(3);
                _phase++;
                break;
            case 3:
                ((Button)_page!.FindChild("SideMerc", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                ((Button)_page.FindChild("ViewGunAr", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                _phase=6;
                break;
            case 6:
                MenuPage.Shot(_page!, "armoury-primary-reference");
                _phase=4;
                break;
            case 4:
                var slot = (CosmeticSlot)(6 + _pistol);
                if (_page!.FindChild("Slot" + slot, true, false) is not PanelContainer panel ||
                    panel.FindChild("Items", true, false) is not GridContainer { } grid || grid.GetChildCount() != 9)
                    return SmokeResult.Fail("Armoury section " + slot + " must show default plus eight finishes");
                ((Button)_page.FindChild("View" + slot, true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                _phase++;
                break;
            case 5:
                foreach (var child in _page!.FindChild("Preview", true, false).FindChildren("*", nameof(Camera3D), true, false))
                    if (child is Camera3D camera && (camera.CullMask & RenderLayers.ShadowPass) != 0)
                        return SmokeResult.Fail("Armoury preview uses the depth-only camera marker");
                MenuPage.Shot(_page!, "armoury-" + (CosmeticSlot)(6 + _pistol));
                if (++_pistol == 4)
                {
                    _page!.QueueFree();
                    return SmokeResult.Pass("Twelve gadget choices and four Armoury sections, each with nine finishes.");
                }
                _phase=4;
                break;
        }
        return SmokeResult.Running;
    }
}
