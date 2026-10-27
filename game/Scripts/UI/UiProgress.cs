using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>A thin progress bar for the menus: a dim track and a filled part in the accent colour.</summary>
public partial class UiProgress : Control
{
    private float _value;

    public UiProgress()
    {
        CustomMinimumSize = new Vector2(0f, 4f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>How full, 0 to 1.</summary>
    public float Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0f, 1f);
            QueueRedraw();
        }
    }

    /// <summary>The filled part's colour.</summary>
    public Color Fill { get; set; } = UiArt.Accent;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(Fill, 0.14f));
        if (_value > 0f)
        {
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _value, Size.Y)), Fill);
        }
    }
}
