using Godot;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>The appearance sync status and explicit conflict choices, shared by the Account and Armoury pages.</summary>
public partial class AppearanceSyncPanel : VBoxContainer
{
    private AccountService? _service;
    private Label? _status;
    private HBoxContainer? _choices;

    public override void _Ready()
    {
        _service = AccountService.Get(GetTree());
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim };
        AddChild(_status);
        _choices = new HBoxContainer();
        _choices.AddChild(UiStyle.MenuButton("Reload account appearance", _service.ReloadAppearance, "ReloadAppearance"));
        _choices.AddChild(UiStyle.MenuButton("Reapply my choices", _service.ReapplyAppearance, "ReapplyAppearance"));
        AddChild(_choices);
        _service.AppearanceChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_service is not null) _service.AppearanceChanged -= Refresh;
    }

    private void Refresh()
    {
        if (_service is null || _status is null || _choices is null) return;
        Visible = AccountSettings.Current.SignedIn && _service.AppearanceManaged;
        _status.Text = _service.AppearanceStatus;
        _choices.Visible = _service.AppearanceConflict;
    }
}
