using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>Three fluid columns, then Play above two columns, then a single Play-first stack.</summary>
public partial class DashboardColumns : Container
{
    private const float Gap = 18f;
    public Control Profile { get; init; } = null!;
    public Control Play { get; init; } = null!;
    public Control News { get; init; } = null!;

    private int Columns(float width)
    {
        float profile = Profile.GetCombinedMinimumSize().X;
        float play = Play.GetCombinedMinimumSize().X;
        float news = News.GetCombinedMinimumSize().X;
        return width >= profile + play + news + Gap * 2 ? 3 : width >= profile + news + Gap ? 2 : 1;
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
            float extra = Math.Max(0f, Size.X - p.X - c.X - n.X - Gap * 2);
            float left = p.X + extra * 0.25f, middle = c.X + extra * 0.45f;
            FitChildInRect(Profile, new Rect2(0f, 0f, left, p.Y));
            FitChildInRect(Play, new Rect2(left + Gap, 0f, middle, c.Y));
            FitChildInRect(News, new Rect2(left + middle + Gap * 2, 0f, Size.X - left - middle - Gap * 2, n.Y));
        }
        else
        {
            FitChildInRect(Play, new Rect2(0f, 0f, Size.X, c.Y));
            if (columns == 2)
            {
                float left = p.X + Math.Max(0f, Size.X - p.X - n.X - Gap) * 0.5f;
                FitChildInRect(Profile, new Rect2(0f, c.Y + Gap, left, p.Y));
                FitChildInRect(News, new Rect2(left + Gap, c.Y + Gap, Size.X - left - Gap, n.Y));
            }
            else
            {
                FitChildInRect(Profile, new Rect2(0f, c.Y + Gap, Size.X, p.Y));
                FitChildInRect(News, new Rect2(0f, c.Y + p.Y + Gap * 2, Size.X, n.Y));
            }
        }
        UpdateMinimumSize();
    }
}
