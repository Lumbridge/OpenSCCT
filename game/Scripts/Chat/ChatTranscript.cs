using System;
using System.Collections.Generic;

namespace OpenScctGame.Chat;

/// <summary>Presentation channels; Nearby is available only in the hub.</summary>
public enum ChatChannel { All, Team, Nearby, System }

/// <summary>A received line. Identity and channel remain separate from the literal player text.</summary>
public sealed record ChatEntry(long Id, ChatChannel Channel, string Text, string SenderName, int SenderId, int SenderTeam, double ReceivedAt)
{
    public float Opacity(double now) => (float)Math.Clamp(
        (ReceivedAt + ChatTranscript.VisibleSeconds + ChatTranscript.FadeSeconds - now) / ChatTranscript.FadeSeconds, 0d, 1d);
}

/// <summary>Connection-local history, independent of pawn lifetime and simulation clock resets.</summary>
public sealed class ChatTranscript
{
    public const int Capacity = 64;
    public const double VisibleSeconds = 8d;
    public const double FadeSeconds = 1d;
    private readonly List<ChatEntry> _entries = [];
    private readonly IReadOnlyList<ChatEntry> _readOnly;
    private long _nextId;

    public ChatTranscript() => _readOnly = _entries.AsReadOnly();

    public IReadOnlyList<ChatEntry> Entries => _readOnly;
    public event Action? Changed;

    public void Add(ChatChannel channel, string text, string senderName, int senderId, int senderTeam, double now)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(senderName);
        if (_entries.Count == Capacity) _entries.RemoveAt(0);
        _entries.Add(new ChatEntry(++_nextId, channel, text, senderName, senderId, senderTeam, now));
        Changed?.Invoke();
    }
}
