using System;
using System.Collections.Generic;
using OpenScct.Game.GameFlow;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

public sealed partial class OnlineServer
{
    private readonly List<MatchTimelineEvent> _history = new();
    private bool _historyActive;
    private bool _historyTruncated;

    private void RecordHistory(string kind, string text)
    {
        if (!_historyActive) return;
        if (_history.Count >= 1023 && kind != "end") { _historyTruncated = true; return; }
        if (_history.Count >= 1024) return;
        _history.Add(new MatchTimelineEvent((int)Math.Clamp((_now - _matchStart) / Second, 0, 86400), kind,
            text.Length > 256 ? text[..256] : text));
    }

    private sealed class HistorySink(OnlineServer server) : IMatchHistorySink
    {
        public void Record(string kind, string text) => server.RecordHistory(kind, text);
    }
}
