using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Play.Demo;
using OnlineDemo = OpenScct.Net.Discovery.DemoEntry;

namespace OpenScctGame.UI;

public partial class DemosScreen
{
    private Task<string>? _download;
    private bool _keepDownload;
    private string _downloadMap = string.Empty;
    private long _downloadBytes, _downloadSize;

    private void StartDownload(OnlineDemo demo, bool keep)
    {
        if (_download is not null) return;
        try
        {
            string safeMap = string.Concat(demo.Map.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string name = $"{demo.Id}-{safeMap}{OpenScct.Net.DemoFile.Extension}";
            string saved = Path.Combine(DemoLibrary.SavedFolder, name);
            if (File.Exists(saved))
            {
                if (keep) ShowStatus(demo.Map + " is already saved in your library.");
                else DemoLibrary.Watch(GetTree(), saved);
                return;
            }
            string path = Path.Combine(DemoLibrary.CacheFolder, name);
            _keepDownload = keep;
            _downloadMap = demo.Map;
            Interlocked.Exchange(ref _downloadBytes, 0);
            Interlocked.Exchange(ref _downloadSize, demo.Size);
            ShowStatus("Preparing " + demo.Map + "…");
            _progress.Visible = true;
            _progress.Value = 0;
            CancellationToken token = _lifetime.Token;
            _download = AccountService.Call(client => Download(client, demo, path, token));
            UpdateDownloadButtons();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _progress.Visible = false;
            ShowError("Could not download the demo: " + e.Message);
        }
    }

    private void ProcessDownload()
    {
        if (_download is not { } download) return;
        if (!download.IsCompleted)
        {
            long received = Interlocked.Read(ref _downloadBytes);
            long total = Interlocked.Read(ref _downloadSize);
            _progress.Value = total > 0 ? Math.Min(100d, received * 100d / total) : 0d;
            _status.Text = received == 0 ? "Preparing " + _downloadMap + "…"
                : total > 0 && received >= total ? "Verifying " + _downloadMap + "…"
                : $"Downloading {_downloadMap} · {Bytes(received)}" + (total > 0 ? " / " + Bytes(total) : string.Empty);
            return;
        }
        _download = null;
        _progress.Visible = false;
        UpdateDownloadButtons();
        if (!download.IsCompletedSuccessfully)
        {
            ShowError("Could not download the demo: " + AccountService.ErrorOf(download, "the download failed."));
            return;
        }
        if (!_keepDownload)
        {
            ShowStatus(string.Empty);
            DemoLibrary.Watch(GetTree(), download.Result);
            return;
        }
        try
        {
            DemoLibrary.Keep(download.Result);
            ReadLocal();
            Render();
            ShowStatus("Saved " + _downloadMap + " to your library. It will stay on this computer.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ReadLocal();
            Render();
            ShowError("Downloaded, but could not keep the demo: " + e.Message);
        }
    }

    private void UpdateDownloadButtons()
    {
        foreach (Button button in _downloadButtons)
            button.Disabled = _download is not null || !button.GetMeta("compatible").AsBool();
    }

    /// <summary>Reuse verified downloads, otherwise fetch and check a temporary file before exposing it to playback.</summary>
    private async Task<string> Download(MasterClient client, OnlineDemo demo, string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        token = timeout.Token;
        if (File.Exists(path) && (demo.Size <= 0 || new FileInfo(path).Length == demo.Size) &&
            string.Equals(await HashOf(path, token).ConfigureAwait(false), demo.Sha256, StringComparison.OrdinalIgnoreCase))
            return path;
        DemoDownloadResponse link = await client.DemoDownloadAsync(demo.Id, token).ConfigureAwait(false);
        long expectedSize = link.Size > 0 ? link.Size : demo.Size;
        Interlocked.Exchange(ref _downloadSize, expectedSize);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using HttpResponseMessage response = await http.GetAsync(link.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (FileStream file = File.Create(part))
            {
                byte[] buffer = new byte[65536];
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    Interlocked.Add(ref _downloadBytes, count);
                }
            }
            if (expectedSize > 0 && new FileInfo(part).Length != expectedSize)
                throw new InvalidDataException("the file did not match its expected size.");
            string expected = link.Sha256.Length > 0 ? link.Sha256 : demo.Sha256;
            if (!string.Equals(await HashOf(part, token).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the file did not match its checksum.");
            token.ThrowIfCancellationRequested();
            File.Move(part, path, overwrite: true);
            return path;
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PushWarning("SVM_DEMO temporary download could not be removed: " + e.Message); }
        }
    }

    private static async Task<string> HashOf(string path, CancellationToken token)
    {
        await using FileStream file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }
}
