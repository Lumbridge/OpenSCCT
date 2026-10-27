using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The modern look's page corner bracket (in place of GUIContent MN_cadre03): a long L with a short inner L and a
/// small square, drawn with lines in the accent colour. Unflipped it is the top right corner, as the original texture.
/// </summary>
public partial class UiCorner : Control
{
    public UiCorner()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public UiCorner(bool flipH, bool flipV)
        : this()
    {
        FlipH = flipH;
        FlipV = flipV;
    }

    public bool FlipH { get; set; }

    public bool FlipV { get; set; }

    public override void _Draw()
    {
        Vector2 s = Size;
        Color line = UiArt.Accent with { A = 0.75f };
        Color soft = UiArt.Accent with { A = 0.32f };
        // Drawn as the top right corner, then mirrored.
        Rect(new Rect2(0f, 0f, s.X, 2f), line);
        Rect(new Rect2(s.X - 2f, 0f, 2f, s.Y), line);
        Rect(new Rect2(s.X * 0.45f, 6f, s.X * 0.55f - 6f, 1f), soft);
        Rect(new Rect2(s.X - 7f, 6f, 1f, s.Y * 0.55f - 6f), soft);
        Rect(new Rect2(s.X - 14f, 11f, 3f, 3f), line);
        Rect(new Rect2(0f, 0f, 6f, 4f), line);
        Rect(new Rect2(s.X - 4f, s.Y - 6f, 4f, 6f), line);
    }

    private void Rect(Rect2 r, Color colour)
    {
        Vector2 s = Size;
        float x = FlipH ? s.X - r.End.X : r.Position.X;
        float y = FlipV ? s.Y - r.End.Y : r.Position.Y;
        DrawRect(new Rect2(x, y, r.Size), colour);
    }
}
