using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Update;

public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond);

/// <summary>Identifies the file a partial download belongs to, so a partial of another upload is never resumed.</summary>
public sealed record DownloadIdentity(long AssetId, long Size, DateTimeOffset? UpdatedAt);

/// <summary>
/// Downloads to <c>&lt;target&gt;.partial</c> next to a small identity file, resumes with an HTTP Range request
/// when the partial belongs to the same asset, starts over when the server ignores the range, and only moves the
/// file to its final name once it has its full size. Checking the hash is the caller's job.
/// </summary>
public sealed class ResumableDownloader
{
    public const int MaxAttempts = 4;

    private readonly HttpClient _http;

    public ResumableDownloader(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    public static string PartialPath(string target) => target + ".partial";

    public static string IdentityPath(string target) => target + ".partial.json";

    public async Task DownloadAsync(
        Func<HttpRequestMessage> createRequest,
        string target,
        DownloadIdentity identity,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(identity);

        string partial = PartialPath(target);
        string idFile = IdentityPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);

        if (File.Exists(target) && (identity.Size <= 0 || new FileInfo(target).Length == identity.Size))
        {
            progress?.Report(new DownloadProgress(identity.Size, identity.Size, 0));
            return;
        }

        if (!SameIdentity(idFile, identity))
        {
            DeleteIfExists(partial);
        }

        AtomicFile.WriteAllText(idFile, JsonSerializer.Serialize(identity));

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (await TryOnceAsync(createRequest, partial, identity.Size, progress, cancel).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancel.IsCancellationRequested && IsTransient(ex))
            {
                // Keep the partial and resume from where it stopped.
            }

            if (attempt >= MaxAttempts)
            {
                throw new IOException($"Download did not complete after {MaxAttempts} attempts.");
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancel).ConfigureAwait(false);
        }

        File.Move(partial, target, overwrite: true);
        DeleteIfExists(idFile);
    }

    /// <summary>One request. True when the partial now holds the whole file.</summary>
    private async Task<bool> TryOnceAsync(Func<HttpRequestMessage> createRequest, string partial, long size, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (size > 0 && have > size)
        {
            DeleteIfExists(partial);
            have = 0;
        }

        if (size > 0 && have == size)
        {
            return true;
        }

        using HttpRequestMessage request = createRequest();
        if (have > 0)
        {
            request.Headers.Range = new RangeHeaderValue(have, null);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            DeleteIfExists(partial);
            return false;
        }

        response.EnsureSuccessStatusCode();

        bool append = response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange is { From: { } from } && from == have;
        if (!append)
        {
            // The server sent the whole file (or a range we did not ask for): start over.
            have = 0;
        }

        long total = size > 0 ? size : have + (response.Content.Headers.ContentLength ?? 0);
        await using (FileStream file = new(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        await using (Stream body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
        {
            byte[] buffer = new byte[1 << 16];
            var clock = Stopwatch.StartNew();
            long startedAt = have;
            long lastReport = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                have += read;
                if (progress is not null && clock.ElapsedMilliseconds - lastReport >= 100)
                {
                    lastReport = clock.ElapsedMilliseconds;
                    double rate = (have - startedAt) / Math.Max(0.001, clock.Elapsed.TotalSeconds);
                    progress.Report(new DownloadProgress(have, total, rate));
                }
            }

            await file.FlushAsync(cancel).ConfigureAwait(false);
        }

        progress?.Report(new DownloadProgress(have, total, 0));
        if (size > 0 && have != size)
        {
            throw new IOException($"Download stopped at {have} of {size} bytes.");
        }

        return true;
    }

    private static bool IsTransient(Exception ex) =>
        ex is IOException or HttpRequestException or TaskCanceledException;

    private static bool SameIdentity(string idFile, DownloadIdentity identity)
    {
        try
        {
            return File.Exists(idFile) && JsonSerializer.Deserialize<DownloadIdentity>(File.ReadAllText(idFile)) == identity;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
