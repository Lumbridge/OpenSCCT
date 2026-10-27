using System;
using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// P_InterATT, the spy's interaction list drawn over the HUD page (Bridge spy page notes, section 6): created when
/// two or more menu entries are usable, so its opening animation starts again each time; the rows are the usable
/// entries' descriptions; while Use is held the selected row gets the arrow and the highlight.
/// </summary>
public sealed class InteractionPage
{
    public const string PageName = "P_InterATT";
    public const string ListWidget = "ListChoix";

    private readonly HudAssets _assets;
    private MagmaNode? _root;

    public InteractionPage(HudAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    public MagmaRenderer Renderer { get; private set; } = new();

    public MagmaDrawList DrawList { get; } = new();

    public bool IsOpen => _root is not null;

    public MagmaNode? Root => _root;

    public void Update(HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(s);
        bool show = s.Valid && !s.HideHud && s.ShowInteractions;
        if (!show)
        {
            _root = null;
            return;
        }

        if (_root is null)
        {
            MagmaPackage package = _assets.Hud;
            if (!package.Pages.TryGetValue(PageName, out int area))
            {
                return;
            }

            // A new page each time the list appears (the native page is created fresh), with its own row state.
            _root = new MagmaNode(package, area);
            Renderer = new MagmaRenderer { TextureSize = _assets.TextureSize };
        }

        MagmaListRows rows = Renderer.ListRows(_root, ListWidget);
        rows.Rows.Clear();
        rows.Rows.AddRange(s.Interactions);
        rows.Selected = s.SelectedInteraction;
        rows.Focus = s.InteractionFocus;
        if (rows.HiddenWidgets.Count == 0)
        {
            rows.HiddenWidgets.Add("IconStickyCamA");
        }

        _root.Tick(dt);
    }

    public void Draw(Vector2 screen)
    {
        DrawList.Clear();
        if (_root is not null)
        {
            Renderer.DrawPage(_root, screen, DrawList);
        }
    }
}
