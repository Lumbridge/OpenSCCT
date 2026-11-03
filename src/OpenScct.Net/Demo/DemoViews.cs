using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenScct.Net;

public sealed partial class DemoWriter
{
    private MemoryStream? _viewBytes;
    private BrotliStream? _viewCompression;
    private BinaryWriter? _views;
    private uint _lastViewTick;

    /// <summary>Records a bounded view sample without changing network packet contents or the protocol hash.</summary>
    public void WriteView(DemoViewFrame frame)
    {
        if (_finished || !frame.IsValid || Index.ViewFrames >= DemoReadLimits.Default.MaxViewFrames ||
            Index.ViewFrames > 0 && frame.Tick < _lastViewTick) return;
        if (_views is null)
        {
            _viewBytes = new MemoryStream();
            _viewCompression = new BrotliStream(_viewBytes, CompressionLevel.Fastest, leaveOpen: true);
            _views = new BinaryWriter(_viewCompression, Encoding.UTF8, leaveOpen: true);
        }
        frame.Write(_views);
        _lastViewTick = frame.Tick;
        Index.LastTick = Math.Max(Index.LastTick, frame.Tick);
        Index.ViewFrames++;
    }

    private void FinishViews()
    {
        if (_views is null) return;
        _views.Dispose();
        _views = null;
        _viewCompression!.Dispose();
        Index.ViewCompressedBytes = _viewBytes!.Length;
        _file.Write(_viewBytes.GetBuffer(), 0, (int)_viewBytes.Length);
    }

    private void DisposeViews()
    {
        _views?.Dispose();
        _viewCompression?.Dispose();
        _viewBytes?.Dispose();
    }
}

public sealed partial class DemoRecording
{
    private readonly Dictionary<int, List<DemoViewFrame>> _views = new();

    internal void ReadViews(BinaryReader reader, int maxPlayers)
    {
        uint previousTick = 0;
        for (int i = 0; i < Index.ViewFrames; i++)
        {
            DemoViewFrame frame = DemoViewFrame.Read(reader);
            if (!frame.IsValid || frame.Tick < previousTick || frame.Tick > Index.LastTick)
                throw new InvalidDataException("The demo contains an invalid recorded view.");
            previousTick = frame.Tick;
            if (!_views.TryGetValue(frame.PlayerId, out var player))
            {
                if (_views.Count >= maxPlayers) throw new InvalidDataException("The demo has too many recorded-view players.");
                _views.Add(frame.PlayerId, player = new());
            }
            player.Add(frame);
        }
    }

    /// <summary>Interpolated recorded POV, with no blending across respawns, camera changes or long gaps.</summary>
    public bool TryView(int playerId, uint tick, out DemoViewFrame view)
    {
        view = default;
        if (!_views.TryGetValue(playerId, out var frames)) return false;
        int lo = 0, hi = frames.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (frames[mid].Tick <= tick) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return false;
        view = frames[lo - 1];
        if (tick - view.Tick > Header.TickRate / 2) return false;
        if (lo < frames.Count)
        {
            DemoViewFrame next = frames[lo];
            if (next.Tick > view.Tick && next.Tick - view.Tick <= Header.TickRate / 4 &&
                next.PawnId == view.PawnId && next.SpyFirstPerson == view.SpyFirstPerson &&
                next.FirstPersonBody == view.FirstPersonBody && next.ViewSource == view.ViewSource &&
                (next.Location - view.Location).Size() < 800f)
                view = DemoViewFrame.Blend(view, next, tick);
        }
        return true;
    }
}
