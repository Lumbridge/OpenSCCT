using System.Collections.Generic;

namespace OpenScctGame.Chat;

public sealed record QuickChatCategory(string Name, IReadOnlyList<string> Messages);

/// <summary>A short category/phrase sequence; only a completed selection produces a team message.</summary>
public sealed class QuickChatState
{
    public const double TimeoutSeconds = 8;
    public const double CooldownSeconds = 1.5;
    public static IReadOnlyList<QuickChatCategory> Categories { get; } = new QuickChatCategory[]
    {
        new("Attack", new[] { "Attack the objective!", "I'm moving in.", "Cover me!", "Follow me!" }),
        new("Defend", new[] { "Defend the objective!", "Hold this position.", "Watch our flank!", "Fall back!" }),
        new("Information", new[] { "Enemy spotted!", "Enemy missing!", "Need help!", "On my way!" }),
        new("Responses", new[] { "Yes!", "No!", "Thanks!", "Sorry!" }),
    };

    public bool IsOpen { get; private set; }
    public int? Category { get; private set; }
    private double _expires, _nextSend;

    public bool Open(double now)
    {
        if (IsOpen || now < _nextSend) return false;
        IsOpen = true;
        Category = null;
        _expires = now + TimeoutSeconds;
        return true;
    }

    public string? Choose(int index, double now)
    {
        if (!IsOpen || Expire(now) || index is < 0 or >= 4) return null;
        if (Category is not { } category)
        {
            Category = index;
            _expires = now + TimeoutSeconds;
            return null;
        }
        string message = Categories[category].Messages[index];
        _nextSend = now + CooldownSeconds;
        Close();
        return message;
    }

    public void Back()
    {
        if (Category is not null) Category = null;
        else Close();
    }

    public bool Expire(double now)
    {
        if (!IsOpen || now < _expires) return false;
        Close();
        return true;
    }

    public void Close() { IsOpen = false; Category = null; }
}
