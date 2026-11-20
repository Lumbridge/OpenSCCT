using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record DownloadFile(string Path, string Url, long Size);
    private sealed record MapDownload(string? Direct, bool Shared, DownloadFile[] Files);
    private sealed record DownloadIndex(DownloadFile[] Shared, Dictionary<string, MapDownload> Maps);

    private static readonly HttpClient DownloadClient = new();
    private static readonly SemaphoreSlim DownloadSlots = new(8);
    private static readonly DownloadIndex Downloads = ReadDownloads();

    private static DownloadIndex ReadDownloads()
    {
        using var stream = typeof(PlayerPortal).Assembly.GetManifestResourceStream("map-downloads.json");
        return JsonSerializer.Deserialize<DownloadIndex>(stream!) ?? throw new InvalidDataException("Missing map download index.");
    }

    private static IResult DownloadMap(HttpContext context)
    {
        var id = context.Request.RouteValues["id"]?.ToString() ?? "";
        if (!Downloads.Maps.TryGetValue(id, out var map)) return Results.NotFound();
        if (map.Direct is not null) return Results.Redirect(map.Direct);
        if (!DownloadSlots.Wait(0))
        {
            context.Response.Headers.RetryAfter = "15";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        context.Response.OnCompleted(() => { DownloadSlots.Release(); return Task.CompletedTask; });
        return Results.Stream(async output =>
        {
            // ZipArchive writes entry descriptors synchronously even after async payload copies.
            var bodyControl = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
            if (bodyControl is not null) bodyControl.AllowSynchronousIO = true;
            var cancel = context.RequestAborted;
            try
            {
                await using var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, true, null, cancel);
                foreach (var file in map.Shared ? map.Files.Concat(Downloads.Shared) : map.Files)
                {
                    using var response = await DownloadClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length && length != file.Size)
                        throw new InvalidDataException("The upstream map file size changed.");
                    var entry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression);
                    await using var target = await entry.OpenAsync(cancel);
                    await using var input = await response.Content.ReadAsStreamAsync(cancel);
                    await input.CopyToAsync(target, cancel);
                }
            }
            catch when (context.Response.HasStarted)
            {
                context.Abort();
                throw;
            }
        }, "application/zip", id + ".zip");
    }
}

