using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>A centered main menu between compact side panels, then a Play-first stack on smaller windows.</summary>
public partial class DashboardColumns : Container
{
    private const float Gap = 18f;
    private const float MainWidth = 600f;
    public Control Profile { get; init; } = null!;
    public Control Play { get; init; } = null!;
    public Control News { get; init; } = null!;

    private int Columns(float width)
    {
        float profile = Profile.GetCombinedMinimumSize().X;
        float play = Play.GetCombinedMinimumSize().X;
        float news = News.GetCombinedMinimumSize().X;
        float sides = Math.Max(profile, news) * 2;
        return width >= sides + play + Gap * 2 ? 3 : width >= sides + Gap ? 2 : 1;
    }

    public override Vector2 _GetMinimumSize()
    {
        if (Profile is null || Play is null || News is null) return Vector2.Zero;
        Vector2 p = Profile.GetCombinedMinimumSize(), c = Play.GetCombinedMinimumSize(), n = News.GetCombinedMinimumSize();
        float height = Columns(Size.X) switch
        {
            3 => Math.Max(c.Y, Math.Max(p.Y, n.Y)),
            2 => c.Y + Gap + Math.Max(p.Y, n.Y),
            _ => c.Y + p.Y + n.Y + Gap * 2,
        };
        return new Vector2(Math.Max(c.X, Math.Max(p.X, n.X)), height);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren || Profile is null || Play is null || News is null) return;
        Vector2 p = Profile.GetCombinedMinimumSize(), c = Play.GetCombinedMinimumSize(), n = News.GetCombinedMinimumSize();
        int columns = Columns(Size.X);
        if (columns == 3)
        {
            float side = Math.Max(p.X, n.X);
            float middle = Math.Min(Math.Max(MainWidth, c.X), Size.X - side * 2 - Gap * 2);
            float start = (Size.X - middle - side * 2 - Gap * 2) * 0.5f;
            FitChildInRect(Profile, new Rect2(start, 0f, side, p.Y));
            FitChildInRect(Play, new Rect2(start + side + Gap, 0f, middle, c.Y));
            FitChildInRect(News, new Rect2(start + side + middle + Gap * 2, 0f, side, n.Y));
        }
        else
        {
            float middle = Math.Min(Math.Max(MainWidth, c.X), Size.X);
            FitChildInRect(Play, new Rect2((Size.X - middle) * 0.5f, 0f, middle, c.Y));
            if (columns == 2)
            {
                float side = Math.Max(p.X, n.X);
                float start = (Size.X - side * 2 - Gap) * 0.5f;
                FitChildInRect(Profile, new Rect2(start, c.Y + Gap, side, p.Y));
                FitChildInRect(News, new Rect2(start + side + Gap, c.Y + Gap, side, n.Y));
            }
            else
            {
                float side = Math.Min(Math.Max(p.X, n.X), Size.X);
                float start = (Size.X - side) * 0.5f;
                FitChildInRect(Profile, new Rect2(start, c.Y + Gap, side, p.Y));
                FitChildInRect(News, new Rect2(start, c.Y + p.Y + Gap * 2, side, n.Y));
            }
        }
        UpdateMinimumSize();
    }
}
