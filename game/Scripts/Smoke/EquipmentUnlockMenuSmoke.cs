using System;
using System.Linq;
using Godot;
using OpenScct.Game.Equipment;
using OpenScct.Server.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Render the real gear menu with hidden rifles, level locks and unlocked gear at small and wide sizes.</summary>
public sealed class EquipmentUnlockMenuSmoke : SmokeTest
{
    public const string TestName = "equipment-unlocks";
    public override string Name => TestName;
    private GearMenu? _page;
    private HostScreen? _host;
    private bool _hostPrepared;
    private GearMenuModel? _model;
    private int _case, _frames;
    private static readonly (bool Spy, bool Rifles, int Level)[] Cases =
        [(false, false, 30), (false, true, 1), (false, true, 30), (true, false, 1), (true, false, 5), (true, false, 10)];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_case >= Cases.Length * 2) return CheckHost(context);
        int state = _case % Cases.Length;
        var size = _case < Cases.Length ? new Vector2I(1920, 1080) : new Vector2I(800, 600);
        if (_page is null)
        {
            if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetSize(size);
            context.Tree.Root.Size = size;
            context.Tree.Root.ContentScaleSize = size;
            var c = Cases[state];
            _model = GearMenuModel.ForLobby(c.Spy ? 0 : 1, new LobbySetup { Rifles = c.Rifles }, new LoadoutSettings(), c.Level)!;
            _page = new GearMenu(_model);
            context.Tree.Root.AddChild(_page);
            _page._Process(3);
            return SmokeResult.Running;
        }
        if (++_frames < 12) return SmokeResult.Running;
        _frames = 0;
        var options = _model!.Spy ? _model.Options : _model.Weapons;
        if (_page.FindChild("EquipmentCatalogue", true, false) is not GridContainer grid || grid.GetChildCount() != options.Count)
            return SmokeResult.Fail("Wrong gear option count");
        for (int i = 0; i < options.Count; i++)
        {
            if (grid.GetChild(i) is not GearTile tile || tile.Disabled == _model.IsAllowed(options[i]))
                return SmokeResult.Fail("Wrong lock state for " + options[i]);
            if (tile.Disabled && _model.RequiredLevel(options[i]) > 0)
            {
                string text = "REQUIRES LEVEL " + _model.RequiredLevel(options[i]);
                if (!tile.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(l => l.Text == text))
                    return SmokeResult.Fail("Missing level requirement on " + options[i]);
                if (tile.Modulate.R > .6f) return SmokeResult.Fail("Locked gear is not greyed out");
            }
            if (MercRifles.Find(options[i]) is not null && (!tile.HasIcon || tile.Atlas is null))
                return SmokeResult.Fail("Missing rifle side render");
        }
        if (!_model.RiflesEnabled && options.Any(MercPrimarySelection.RequiresRifleOption))
            return SmokeResult.Fail("Disabled rifles remain visible");
        MenuPage.Shot(_page, $"equipment-unlocks-{state}-{size.X}x{size.Y}");
        if (_page.FindChild("OK", true, false) is not Button apply ||
            apply.GetGlobalRect().End.X > size.X + 1 || apply.GetGlobalRect().End.Y > size.Y + 1)
            return SmokeResult.Fail("Apply button falls outside " + size + ": " + _page.FindChild("OK", true, false));
        _page.QueueFree();
        _page = null;
        _case++;
        return SmokeResult.Running;
    }

    private SmokeResult CheckHost(SmokeContext context)
    {
        var size = _case == Cases.Length * 2 ? new Vector2I(1920, 1080) : new Vector2I(800, 600);
        if (_host is null)
        {
            if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetSize(size);
            context.Tree.Root.Size = size;
            context.Tree.Root.ContentScaleSize = size;
            _host = new HostScreen();
            context.Tree.Root.AddChild(_host);
            _host._Process(3);
            _host.SettingsTabs!.CurrentTab = 1;
            return SmokeResult.Running;
        }
        if (++_frames < 12) return SmokeResult.Running;
        _frames = 0;
        if (_host.FindChild("RiflesCheck", true, false) is not CheckBox check || !check.IsVisibleInTree())
            return SmokeResult.Fail("Additional rifles host checkbox is missing");
        if (!_hostPrepared)
        {
            if (_case == Cases.Length * 2 && (_host.Model!.Setup.Rifles || check.ButtonPressed))
                return SmokeResult.Fail("Rifles default to enabled");
            check.ButtonPressed = true;
            for (Node? parent = check.GetParent(); parent is not null; parent = parent.GetParent())
                if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(check);
            _hostPrepared = true;
            return SmokeResult.Running;
        }
        if (!_host.Model!.Setup.Rifles) return SmokeResult.Fail("Rifle checkbox does not update host rules");
        MenuPage.Shot(_host, $"rifle-host-option-{size.X}x{size.Y}");
        check.ButtonPressed = false;
        _host.QueueFree();
        _host = null;
        _hostPrepared = false;
        return ++_case == Cases.Length * 2 + 2
            ? SmokeResult.Pass("12 gear states and 2 host forms: rifle visibility, level locks, grey cards, labels and small/wide layouts")
            : SmokeResult.Running;
    }
}
