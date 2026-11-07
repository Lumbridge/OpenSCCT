using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private Control CreateControllerTuningPage(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        string prefix = merc ? "controls_merc_pad" : "controls_spy_pad";
        LookSettings Look() => _edit.LookOf(merc, true);
        var reset = new MenuButton { Name = "ResetController", Text = "Reset…", CustomMinimumSize = new Vector2(90, 32) };
        reset.GetPopup().AddItem("Reset controller for this role", 0);
        reset.GetPopup().IdPressed += _ => Confirm("Reset controller",
            "Restore this role's controller bindings, sensitivity and movement settings? Changes take effect when you Apply or Save & Close.",
            () => ResetLook(side, true), "Reset controller");

        var settings = Grid("ControllerRoleSettings");
        Section(settings, "Sensitivity", "Controller turning speed for this role. Base speed and deadzones are configured in Controller (Shared).");
        if (!merc) Slider(settings, "Free look sensitivity", prefix + ".free_look", .05f, 10f, SensStep,
            Look().FreeLook, v => Look().FreeLook = v, SensFormat, typed: true);
        Slider(settings, merc ? "Look sensitivity" : "Gun look sensitivity", prefix + ".look", .05f, 10f, SensStep,
            Look().Look, v => Look().Look = v, SensFormat, typed: true);
        Slider(settings, "Vertical sensitivity", prefix + ".vertical", .05f, 10f, SensStep,
            Look().Vertical, v => Look().Vertical = v, SensFormat, typed: true);
        _controls[prefix + ".vertical"].TooltipText = "Multiplies vertical controller turning speed relative to horizontal speed for this role.";

        Section(settings, "Response", "Linear turns proportionally to stick input. Soft, Medium and Aggressive boost the outer 25% of each axis to 2×, 2.5× and 3× speed.");
        Choice(settings, merc ? "Stick response" : "Free look response", prefix + ".look_response", GameSettings.Responses,
            Look().LookResponse, v => Look().LookResponse = v, v => v.Replace("Curve", ""));
        if (!merc) Choice(settings, "Gun look response", prefix + ".gun_look_response", GameSettings.Responses,
            Look().GunLookResponse, v => Look().GunLookResponse = v, v => v.Replace("Curve", ""));

        Section(settings, "Movement", "Movement behavior for this role. Enable movement and set stick deadzones in Controller (Shared).");
        if (!merc) Slider(settings, "Run threshold", "controller.run_threshold", .1f, 1f, .01f,
            _edit.Pad.RunThreshold, v => _edit.Pad.RunThreshold = v, typed: true);
        Check(settings, "Eight-way movement", merc ? "controller.merc_move8" : "controller.spy_move8",
            merc ? _edit.Pad.MercMove8 : _edit.Pad.SpyMove8,
            v => { if (merc) _edit.Pad.MercMove8 = v; else _edit.Pad.SpyMove8 = v; });
        _controls[merc ? "controller.merc_move8" : "controller.spy_move8"].TooltipText = "Snap controller movement to eight directions rather than following the full stick angle.";

        Section(settings, "Scope & cameras", "Sensitivity multipliers for binoculars, the sniper scope and remote camera views.");
        Slider(settings, merc ? "Scope sensitivity" : "Binocular sensitivity", prefix + ".scope", .05f, 10f, SensStep,
            Look().Scope, v => Look().Scope = v, SensFormat, typed: true);
        if (merc) Slider(settings, "Scope vertical multiplier", prefix + ".scope_vertical", .05f, 10f, SensStep,
            Look().ScopeVertical, v => Look().ScopeVertical = v, SensFormat, typed: true);
        if (merc) _controls[prefix + ".scope_vertical"].TooltipText = "Vertical scope speed: 1 uses the original speed; 2.45 matches horizontal and vertical scope movement.";
        Slider(settings, "Remote camera sensitivity", prefix + ".cam", .05f, 10f, SensStep,
            Look().Cam, v => Look().Cam = v, SensFormat, typed: true);

        Section(settings, "Modifiers", "Temporarily change sensitivity while holding a controller input. Assign modifier inputs in the Controller category of Controller Bindings.");
        Check(settings, "Enable sensitivity modifier", prefix + ".clutch_one_enabled", Look().ClutchOneEnabled,
            v => Look().ClutchOneEnabled = v);
        Slider(settings, "Modifier strength", prefix + ".clutch_one", .1f, 10f, .05f,
            Look().ClutchOne > 0f ? Look().ClutchOne : 1f, v => Look().ClutchOne = v, typed: true);
        _controls[prefix + ".clutch_one"].TooltipText = "While held, 0.5× halves sensitivity, 1× keeps it unchanged and 2× doubles it. Release to restore normal speed.";
        if (merc)
        {
            Check(settings, "Enable second modifier", prefix + ".clutch_two_enabled", Look().ClutchTwoEnabled,
                v => Look().ClutchTwoEnabled = v);
            Slider(settings, "Second modifier strength", prefix + ".clutch_two", .1f, 10f, .05f,
                Look().ClutchTwo > 0f ? Look().ClutchTwo : 1f, v => Look().ClutchTwo = v, typed: true);
            _controls[prefix + ".clutch_two"].TooltipText = "Both Mercenary modifiers multiply together while held. Release the input to restore normal speed.";
        }

        Section(settings, "Button layout", "Swap the physical stick clicks and bumpers for this role. Existing logical button assignments follow the swapped controls.");
        Check(settings, "Swap left stick click and bumper", prefix + ".swap_ls_lb", Look().SwapLsLb,
            v => { Look().SwapLsLb = v; RebuildKeys(); });
        Check(settings, "Swap right stick click and bumper", prefix + ".swap_rs_rb", Look().SwapRsRb,
            v => { Look().SwapRsRb = v; RebuildKeys(); });

        return CreateSettingsEditor("Controller Movement & Aim", settings, "controller_tuning." + side, reset);
    }
}
