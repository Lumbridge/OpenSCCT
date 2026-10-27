using System;

namespace OpenScctGame.Hud.Gadgets;

/// <summary>
/// The gadget views of the HUD (spec/equipment/gadget-hud.md, docs/GADGETS.md): the sticky cam and camera network
/// pages that replace the player page, the sticky cam and mine menus, the gas mask overlay and the heart sensor cone.
/// The spy and merc binocular pages are the HUD's own (HudBinocularPages). A HUD gets them with
/// <code>
/// GadgetHudViews views = GadgetHudViews.Attach(hud);
/// </code>
/// </summary>
public sealed class GadgetHudViews
{
    private GadgetHudViews()
    {
    }

    public StickyCamPage? StickyCam { get; private init; }

    public CamNetworkPage? CamNetwork { get; private init; }

    public StickyCamMenuDriver StickyCamMenu { get; } = new();

    public MineMenuDriver MineMenu { get; } = new();

    public GasMaskDriver GasMask { get; } = new();

    public HeartSensorDriver Heart { get; } = new();

    /// <summary>Adds the views to <paramref name="hud"/> (pages the UI content lacks are left out).</summary>
    public static GadgetHudViews Attach(HudLayer hud)
    {
        ArgumentNullException.ThrowIfNull(hud);
        HudAssets? assets = hud.Assets;
        var views = new GadgetHudViews
        {
            StickyCam = assets is not null && assets.Hud.Pages.ContainsKey(StickyCamPage.PageName) ? new StickyCamPage(assets, hud.Options) : null,
            CamNetwork = assets is not null && assets.Hud.Pages.ContainsKey(CamNetworkPage.PageName) ? new CamNetworkPage(assets, hud.Options) : null,
        };

        if (views.StickyCam is { } sticky)
        {
            hud.AddPageOverride(sticky);
        }

        if (views.CamNetwork is { } cams)
        {
            hud.AddPageOverride(cams);
        }

        hud.AddDriver(views.StickyCamMenu);
        hud.AddDriver(views.MineMenu);
        hud.AddDriver(views.GasMask);
        hud.AddDriver(views.Heart);
        return views;
    }
}
