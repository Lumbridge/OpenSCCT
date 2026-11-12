using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OpenScct.Net;

namespace OpenScct.Server.Online;

public sealed partial class OnlineClient
{
    internal const long MaxPersonalDemoBytes = DemoFile.MaxPersonalDemoBytes;
    public const int HubDemoWaitSeconds = 300;
    private bool _recordDemos;
    private bool _demoCapability;
    private bool _trainingCapability;
    private DemoDownload? _demoDownload;
    private string? _demoStatus;
    private string? _lastDemoId;
    private long _lastDemoLength;
    private string? _pendingTrainingId;
    private string? _lastTrainingId;
    private long _lastTrainingLength;

    private sealed class DemoDownload : IDisposable
    {
        public required string Id { get; init; }
        public required string Path { get; init; }
        public required string Hash { get; init; }
        public required long Length { get; init; }
        public required FileStream File { get; init; }
        public bool Training { get; init; }
        public bool IncludesTraining { get; init; }
        public string Acknowledge => Training ? "demotrainingack" : "demoack";
        public int Progress { get; set; } = -1;
        public void Dispose() => File.Dispose();
    }

    public bool RecordDemos => _recordDemos;
    /// <summary>A requested round is recording or its completed demo still needs to be saved.</summary>
    public bool PendingDemoSave { get; private set; }
    /// <summary>The current replay is saved and only its optional training companion is outstanding.</summary>
    public bool PendingTrainingSave => PendingDemoSave && _pendingTrainingId is not null;
    public string? LastDemoPath { get; private set; }
    public string DemoRecordingStatus => _demoStatus ?? (!_recordDemos ? "Match recording is off for you." :
        _demoCapability ? "Your upcoming matches will be saved to Demos on this computer." :
        State is OnlineClientState.Connecting or OnlineClientState.Travelling ? "Checking this server's demo support..." :
        "This server does not support personal demos. It needs an updated server build.");

    public void SetRecordDemos(bool enabled)
    {
        _recordDemos = enabled;
        _demoStatus = null;
        if (!enabled) { PendingDemoSave = false; _pendingTrainingId = null; DiscardDemoDownload(); }
        if (_demoCapability) SendLine(SessionProtocol.Line("demorecord", enabled, _trainingCapability ? 2 : 1));
        Changed?.Invoke();
    }

    private bool HandleDemoLine(string line)
    {
        if (!line.StartsWith(SessionProtocol.Prefix + "demo", StringComparison.Ordinal) ||
            !SessionProtocol.TryParse(line, out string command, out var fields)) return false;
        try
        {
            switch (command)
            {
                case "democap":
                    _demoCapability = SessionProtocol.Int(fields, 0) == 1;
                    _trainingCapability = Settings.EnableTrainingData && _demoCapability && SessionProtocol.Int(fields, 1) >= 2;
                    _demoStatus = null;
                    if (_demoCapability) SendLine(SessionProtocol.Line("demorecord", _recordDemos, _trainingCapability ? 2 : 1));
                    Changed?.Invoke();
                    break;
                case "demostatus":
                    string status = SessionProtocol.Text(fields, 0);
                    if (status is "recording" or "pending") PendingDemoSave = _recordDemos;
                    else if (status is "failed" or "training-failed" or "too-large" or "off" or "next")
                    {
                        PendingDemoSave = false;
                        _pendingTrainingId = null;
                        DiscardDemoDownload();
                    }
                    _demoStatus = status switch
                    {
                        "recording" => "This match is being recorded. Your demo will download when the round ends.",
                        "pending" => "Your match demo is ready to download. Stay connected until it finishes.",
                        "failed" => "The server could not record this match. Gameplay can continue.",
                        "training-failed" => "Demo saved, but its training data could not be downloaded.",
                        "too-large" => "This demo is too large to download in the lobby (64 MB limit).",
                        "next" => "Recording is enabled for your next match.",
                        _ => null,
                    };
                    Changed?.Invoke();
                    break;
                case "demooffer":
                case "demotrainingoffer":
                    bool training = command == "demotrainingoffer";
                    string acknowledge = training ? "demotrainingack" : "demoack";
                    string id = SessionProtocol.Text(fields, 0), hash = SessionProtocol.Text(fields, 2);
                    if (!_recordDemos || Settings.DemoFolder is not { Length: > 0 } folder ||
                        !Guid.TryParseExact(id, "N", out _) || !long.TryParse(SessionProtocol.Text(fields, 1), out long length) ||
                        length <= 0 || length > (training ? TrainingDataFile.MaxCompressedBytes : MaxPersonalDemoBytes) ||
                        hash.Length != 64 || !IsHex(hash) || (training && (!_trainingCapability || id != _lastDemoId || LastDemoPath is null)))
                    {
                        SendLine(SessionProtocol.Line(acknowledge, id, -1));
                        PendingDemoSave = false;
                        break;
                    }
                    bool includesTraining = !training && _trainingCapability && SessionProtocol.Int(fields, 3) == 1;
                    if (training ? id == _lastTrainingId && length == _lastTrainingLength : id == _lastDemoId && length == _lastDemoLength)
                    {
                        SendLine(SessionProtocol.Line(acknowledge, id, length));
                        _pendingTrainingId = !training && includesTraining && _lastTrainingId != id ? id : null;
                        PendingDemoSave = _pendingTrainingId is not null;
                        _demoStatus = PendingDemoSave ? "Demo saved. Waiting for its training data; stay connected until it finishes." :
                            training || _lastTrainingId == id ? "Demo and training data saved. Open Demos from the main menu to watch the replay." :
                            "Demo saved. Open Demos from the main menu to watch it.";
                        Changed?.Invoke();
                        break;
                    }
                    if (_demoDownload is not { } current || current.Id != id || current.Length != length || current.Hash != hash || current.Training != training)
                    {
                        DiscardDemoDownload();
                        Directory.CreateDirectory(folder);
                        string path = System.IO.Path.Combine(folder, "." + id + (training ? ".training.partial" : ".partial"));
                        _demoDownload = new DemoDownload { Id = id, Path = path, Hash = hash, Length = length,
                            File = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None),
                            Training = training, IncludesTraining = includesTraining };
                    }
                    PendingDemoSave = true;
                    if (training) _pendingTrainingId = id;
                    SendLine(SessionProtocol.Line(acknowledge, id, _demoDownload!.File.Position));
                    ReportDemoProgress();
                    break;
                case "demodata":
                case "demotrainingdata":
                    if (_demoDownload is not { } download || SessionProtocol.Text(fields, 0) != download.Id ||
                        (command == "demotrainingdata") != download.Training || !long.TryParse(SessionProtocol.Text(fields, 1), out long offset)) break;
                    if (offset < download.File.Position)
                    {
                        SendLine(SessionProtocol.Line(download.Acknowledge, download.Id, download.File.Position));
                        break;
                    }
                    byte[] bytes = Convert.FromBase64String(SessionProtocol.Text(fields, 2));
                    if (offset != download.File.Position || bytes.Length is <= 0 or > 300 || offset + bytes.Length > download.Length)
                        throw new InvalidDataException("Invalid demo chunk.");
                    download.File.Write(bytes);
                    if (download.File.Position == download.Length) CompleteDemoDownload();
                    else
                    {
                        SendLine(SessionProtocol.Line(download.Acknowledge, download.Id, download.File.Position));
                        ReportDemoProgress();
                    }
                    break;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            bool trainingFailed = _demoDownload?.Training == true || command.StartsWith("demotraining", StringComparison.Ordinal);
            if (_demoDownload is { } download) SendLine(SessionProtocol.Line(download.Acknowledge, download.Id, -1));
            else if (command is "demooffer" or "demotrainingoffer")
                SendLine(SessionProtocol.Line(trainingFailed ? "demotrainingack" : "demoack", SessionProtocol.Text(fields, 0), -1));
            DiscardDemoDownload();
            PendingDemoSave = false;
            _pendingTrainingId = null;
            _demoStatus = (trainingFailed ? "Demo saved, but training data could not be saved: " : "Demo could not be saved: ") + error.Message;
            Changed?.Invoke();
        }
        return true;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private void ReportDemoProgress()
    {
        if (_demoDownload is not { } download) return;
        int progress = (int)(download.File.Position * 100 / download.Length);
        if (progress == download.Progress) return;
        download.Progress = progress;
        _demoStatus = (download.Training ? "Demo saved. Saving training data... " : "Saving your match demo... ") + progress + "%. Stay connected until it finishes.";
        Changed?.Invoke();
    }

    private void CompleteDemoDownload()
    {
        DemoDownload download = _demoDownload!;
        download.File.Flush();
        download.File.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(download.File));
        download.Dispose();
        if (!string.Equals(hash, download.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Demo checksum differs.");
        string path;
        if (download.Training)
        {
            string replayPath = LastDemoPath ?? throw new InvalidDataException("The demo for this training data is unavailable.");
            TrainingDataFile.Validate(download.Path, DemoFile.ReadInfo(replayPath).Header);
            path = TrainingDataFile.PathForDemo(replayPath);
        }
        else
        {
            var info = DemoFile.ReadInfo(download.Path); // Require a complete header/index before publishing to the library.
            // Use the recorded side: the lobby may already have travelled or switched teams for a revenge round.
            int team = info.Index.Players.Where(player => player.Name == Settings.PlayerName)
                .Select(player => (int?)player.Team).FirstOrDefault() ?? SessionProtocol.SpectatorTeam;
            path = System.IO.Path.Combine(Settings.DemoFolder!, DemoFileName.Personal(
                DateTimeOffset.FromUnixTimeMilliseconds(info.Header.StartUnixMs), info.Header.Map, team, download.Id));
        }
        File.Move(download.Path, path, overwrite: false);
        if (download.Training)
        {
            _lastTrainingId = download.Id;
            _lastTrainingLength = download.Length;
            _pendingTrainingId = null;
        }
        else
        {
            _lastDemoId = download.Id;
            _lastDemoLength = download.Length;
            LastDemoPath = path;
            _pendingTrainingId = download.IncludesTraining ? download.Id : null;
        }
        _demoDownload = null;
        PendingDemoSave = _pendingTrainingId is not null;
        _demoStatus = PendingDemoSave ? "Demo saved. Waiting for its training data; stay connected until it finishes." :
            download.Training ? "Demo and training data saved. Open Demos from the main menu to watch the replay." :
            "Demo saved. Open Demos from the main menu to watch it.";
        SendLine(SessionProtocol.Line(download.Acknowledge, download.Id, download.Length));
        Changed?.Invoke();
    }

    private void DiscardDemoDownload()
    {
        if (_demoDownload is not { } download) return;
        _demoDownload = null;
        download.Dispose();
        try { File.Delete(download.Path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
