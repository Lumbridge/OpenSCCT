using Godot;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>The appearance sync status and explicit conflict choices, shared by the Account and Armoury pages.</summary>
public partial class AppearanceSyncPanel : VBoxContainer
{
    private AccountService? _service;
    private Label? _status;
    private HFlowContainer? _choices;

    public override void _Ready()
    {
        _service = AccountService.Get(GetTree());
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim };
        AddChild(_status);
        _choices = new HFlowContainer();
        _choices.AddThemeConstantOverride("h_separation", 8);
        _choices.AddThemeConstantOverride("v_separation", 8);
        _choices.AddChild(UiStyle.MenuButton("Reload account appearance", _service.ReloadAppearance, "ReloadAppearance"));
        _choices.AddChild(UiStyle.MenuButton("Reapply my choices", _service.ReapplyAppearance, "ReapplyAppearance"));
        foreach (Node child in _choices.GetChildren())
        {
            if (child is not Button button) continue;
            button.CustomMinimumSize = new Vector2(0f, 36f);
            button.AddThemeFontSizeOverride("font_size", 14);
        }
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
