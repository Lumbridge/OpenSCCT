using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

public sealed partial class OnlineServer
{
    private readonly HashSet<int> _demoSubscribers = new();
    private readonly HashSet<int> _trainingDemoSubscribers = new();
    private readonly HashSet<int> _roundDemoRecipients = new();
    private readonly Dictionary<int, Queue<PersonalDemoDelivery>> _personalDemos = new();
    private readonly Dictionary<int, DemoSend> _demoSends = new();
    private readonly HashSet<string> _temporaryDemos = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _personalDemoFolder = Path.Combine(Path.GetTempPath(), "OpenSCCT", "demos", Guid.NewGuid().ToString("N"));

    private sealed record PersonalDemoFile(string Path, long Length, string Hash);
    private sealed record PersonalDemo(string Id, PersonalDemoFile Replay, PersonalDemoFile? Training);
    private sealed class PersonalDemoDelivery(PersonalDemo demo, bool includeTraining)
    {
        public PersonalDemo Demo { get; } = demo;
        public bool IncludeTraining { get; } = includeTraining;
        public bool ReplaySaved { get; set; }
    }
    private sealed class DemoSend : IDisposable
    {
        public required PersonalDemoDelivery Delivery { get; init; }
        public PersonalDemo Demo => Delivery.Demo;
        public bool Training => Delivery.ReplaySaved;
        public PersonalDemoFile Source => Training ? Demo.Training! : Demo.Replay;
        public FileStream? File { get; set; }
        public long Acknowledged { get; set; } = -1;
        public long Sent { get; set; }
        public long LastProgress { get; set; }
        public long LastAcknowledgedProgress { get; set; }
        public long HighestAcknowledged { get; set; } = -1;
        public void Dispose() => File?.Dispose();
    }

    /// <summary>A connected recipient is accepting a queued demo; used for bounded hub match shutdown grace.</summary>
    public bool HasProgressingPersonalDemos => _players.Any(player => player.Slot?.Connection is { bJoined: true } &&
        _personalDemos.TryGetValue(player.Id, out var queue) && queue.Count > 0 &&
        (!_demoSends.TryGetValue(player.Id, out var transfer) || _now - transfer.LastAcknowledgedProgress < 30_000_000));

    private void SendDemoWelcome(SessionPlayer player)
    {
        // Keep the v1 field for older clients; v2 clients explicitly opt into the companion file.
        Send(player, SessionProtocol.Line("democap", 1, 2));
        if (_demoSends.Remove(player.Id, out var transfer)) transfer.Dispose();
    }

    private bool HandleDemoRequest(SessionPlayer player, string line)
    {
        if (!line.StartsWith(SessionProtocol.Prefix + "demo", StringComparison.Ordinal) ||
            !SessionProtocol.TryParse(line, out string command, out var fields)) return false;
        if (command == "demorecord")
        {
            bool enabled = SessionProtocol.Int(fields, 0) == 1;
            if (enabled)
            {
                _demoSubscribers.Add(player.Id);
                if (SessionProtocol.Int(fields, 1) >= 2) _trainingDemoSubscribers.Add(player.Id);
                else _trainingDemoSubscribers.Remove(player.Id);
                // An existing full recording can be shared even when the player opted in during a round.
                if (Host.Recorder is { IsFinished: false }) _roundDemoRecipients.Add(player.Id);
                Send(player, SessionProtocol.Line("demostatus", Host.Recorder is { IsFinished: false } ? "recording" :
                    _personalDemos.TryGetValue(player.Id, out var queued) && queued.Count > 0 ? "pending" : "next"));
            }
            else
            {
                ForgetDemoPlayer(player.Id);
                Send(player, SessionProtocol.Line("demostatus", "off"));
            }
        }
        else if (command is "demoack" or "demotrainingack" && _demoSends.TryGetValue(player.Id, out var transfer) &&
            (command == "demotrainingack") == transfer.Training &&
            SessionProtocol.Text(fields, 0) == transfer.Demo.Id &&
            long.TryParse(SessionProtocol.Text(fields, 1), out long offset))
        {
            if (offset == -1 || offset == transfer.Source.Length)
            {
                CompletePersonalDemo(player.Id, cancelled: offset == -1);
            }
            else if (offset >= 0 && offset <= transfer.Source.Length &&
                (transfer.Acknowledged < 0 || offset >= transfer.Acknowledged && offset <= transfer.Sent))
            {
                if (offset > transfer.HighestAcknowledged)
                {
                    transfer.HighestAcknowledged = offset;
                    transfer.LastAcknowledgedProgress = _now;
                }
                if (transfer.Acknowledged < 0) transfer.Sent = offset;
                transfer.Acknowledged = offset;
                transfer.LastProgress = _now;
            }
        }
        return true;
    }

    private string? PersonalRecordingFolder()
    {
        _roundDemoRecipients.Clear();
        foreach (int id in _demoSubscribers)
            if (_players.Any(player => player.Id == id)) _roundDemoRecipients.Add(id);
        return Settings.DemoFolder is { Length: > 0 } configured ? configured :
            _roundDemoRecipients.Count > 0 ? _personalDemoFolder : null;
    }

    private void PersonalRecordingStarted(string path)
    {
        if (Settings.DemoFolder is not { Length: > 0 }) _temporaryDemos.Add(path);
        foreach (SessionPlayer player in _players)
            if (_roundDemoRecipients.Contains(player.Id)) Send(player, SessionProtocol.Line("demostatus", "recording"));
    }

    private void PersonalRecordingFailed()
    {
        foreach (SessionPlayer player in _players)
            if (_roundDemoRecipients.Contains(player.Id)) Send(player, SessionProtocol.Line("demostatus", "failed"));
        _roundDemoRecipients.Clear();
    }

    private void QueuePersonalDemo(string path)
    {
        if (_roundDemoRecipients.Count == 0) { CleanTemporaryDemos(); return; }
        long length = new FileInfo(path).Length;
        if (length > OnlineClient.MaxPersonalDemoBytes)
        {
            foreach (SessionPlayer player in _players)
                if (_roundDemoRecipients.Contains(player.Id)) Send(player, SessionProtocol.Line("demostatus", "too-large"));
            _roundDemoRecipients.Clear();
            CleanTemporaryDemos();
            return;
        }
        var demo = new PersonalDemo(Guid.NewGuid().ToString("N"), DescribeDemoFile(path), DescribeTrainingFile(path));
        foreach (int id in _roundDemoRecipients)
        {
            if (!_demoSubscribers.Contains(id)) continue;
            if (!_personalDemos.TryGetValue(id, out var queue)) _personalDemos[id] = queue = new Queue<PersonalDemoDelivery>();
            // Bound disk retention for a client that never accepts its offers.
            if (queue.Count >= 2)
            {
                if (_demoSends.Remove(id, out var previous)) previous.Dispose();
                queue.Dequeue();
            }
            queue.Enqueue(new PersonalDemoDelivery(demo, demo.Training is not null && _trainingDemoSubscribers.Contains(id)));
        }
        _roundDemoRecipients.Clear();
        CleanTemporaryDemos();
    }

    private static PersonalDemoFile DescribeDemoFile(string path)
    {
        using var file = File.OpenRead(path);
        return new PersonalDemoFile(path, file.Length, Convert.ToHexString(SHA256.HashData(file)));
    }

    private PersonalDemoFile? DescribeTrainingFile(string demoPath)
    {
        try
        {
            string path = TrainingDataFile.PathForDemo(demoPath);
            if (!File.Exists(path)) return null;
            long length = new FileInfo(path).Length;
            if (length is <= 0 or > TrainingDataFile.MaxCompressedBytes) return null;
            return DescribeDemoFile(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log("training data unavailable for personal demo: " + error.Message);
            return null;
        }
    }

    private void OfferPersonalDemo(SessionPlayer player, DemoSend transfer)
    {
        if (transfer.Training)
            Send(player, SessionProtocol.Line("demotrainingoffer", transfer.Demo.Id, transfer.Source.Length, transfer.Source.Hash));
        else
            Send(player, SessionProtocol.Line("demooffer", transfer.Demo.Id, transfer.Source.Length, transfer.Source.Hash, transfer.Delivery.IncludeTraining));
    }

    private void TickPersonalDemos()
    {
        // Do not compete with replication during play or map loading. A reconnect resumes from the client's offset.
        if (_travel is not null || Phase is not (SessionPhase.Lobby or SessionPhase.Results)) return;
        foreach (SessionPlayer player in _players)
        {
            if (player.Slot?.Connection is not { bJoined: true } ||
                !_personalDemos.TryGetValue(player.Id, out var queue) || queue.Count == 0) continue;
            try
            {
                if (!_demoSends.TryGetValue(player.Id, out var transfer))
                {
                    transfer = new DemoSend { Delivery = queue.Peek(), LastProgress = _now, LastAcknowledgedProgress = _now };
                    _demoSends[player.Id] = transfer;
                    OfferPersonalDemo(player, transfer);
                    continue;
                }
                if (_now - transfer.LastProgress > 20_000_000)
                {
                    // Re-offer rather than queueing unbounded reliable messages to a stalled client.
                    transfer.Acknowledged = -1;
                    transfer.LastProgress = _now;
                    OfferPersonalDemo(player, transfer);
                }
                if (transfer.Acknowledged < 0) continue;
                transfer.File ??= File.OpenRead(transfer.Source.Path);
                for (int chunk = 0; chunk < 4 && transfer.Sent < transfer.Source.Length && transfer.Sent - transfer.Acknowledged < 2400; chunk++)
                {
                    byte[] bytes = new byte[(int)Math.Min(300, transfer.Source.Length - transfer.Sent)];
                    transfer.File.Position = transfer.Sent;
                    transfer.File.ReadExactly(bytes);
                    Send(player, SessionProtocol.Line(transfer.Training ? "demotrainingdata" : "demodata", transfer.Demo.Id, transfer.Sent, Convert.ToBase64String(bytes)));
                    transfer.Sent += bytes.Length;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Send(player, SessionProtocol.Line("demostatus", queue.Peek().ReplaySaved ? "training-failed" : "failed"));
                CompletePersonalDemo(player.Id, cancelled: true);
                Log("personal demo transfer failed: " + error.Message);
            }
        }
    }

    private void CompletePersonalDemo(int playerId, bool cancelled = false)
    {
        if (_demoSends.Remove(playerId, out var transfer)) transfer.Dispose();
        if (_personalDemos.TryGetValue(playerId, out var queue) && queue.Count > 0)
        {
            PersonalDemoDelivery delivery = queue.Peek();
            if (!cancelled && !delivery.ReplaySaved && delivery.IncludeTraining) delivery.ReplaySaved = true;
            else queue.Dequeue();
        }
        CleanTemporaryDemos();
    }

    private void ForgetDemoPlayer(int playerId)
    {
        _demoSubscribers.Remove(playerId);
        _trainingDemoSubscribers.Remove(playerId);
        _roundDemoRecipients.Remove(playerId);
        if (_demoSends.Remove(playerId, out var transfer)) transfer.Dispose();
        _personalDemos.Remove(playerId);
        CleanTemporaryDemos();
    }

    private void CleanTemporaryDemos()
    {
        foreach (string path in _temporaryDemos.ToArray())
        {
            if (Host.Recorder is { IsFinished: false } active && active.Writer.Path == path ||
                _personalDemos.Values.Any(queue => queue.Any(delivery => delivery.Demo.Replay.Path == path))) continue;
            try { File.Delete(path); File.Delete(TrainingDataFile.PathForDemo(path)); _temporaryDemos.Remove(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private void DisposePersonalDemos()
    {
        foreach (DemoSend transfer in _demoSends.Values) transfer.Dispose();
        _demoSends.Clear();
        _personalDemos.Clear();
        CleanTemporaryDemos();
        try { if (Directory.Exists(_personalDemoFolder)) Directory.Delete(_personalDemoFolder); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
