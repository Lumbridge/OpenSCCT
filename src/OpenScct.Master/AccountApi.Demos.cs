using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

// Round demos (docs/ONLINE.md, "Demos"): trusted servers get signed S3 upload links for the rounds they reported,
// signed-in players list the ready demos and get signed download links. No file passes through the master.
internal sealed partial class AccountApi
{
    /// <summary>Download links one account may ask for per hour.</summary>
    public const int DownloadsPerHour = 30;

    private const string DemosOff = "Demos are not set up on this master.";

    private readonly DemoStorage? _demos;
    private readonly Dictionary<long, Queue<long>> _downloads = new();

    private void MapDemos(WebApplication app)
    {
        app.MapPost(MasterApi.DemoUploadPath, (HttpContext context, DemoUploadRequest request) => DemoUpload(context, request));
        app.MapPost(MasterApi.DemosPath + "/{id:long}/done", (HttpContext context, long id) => DemoDone(context, id));
        app.MapGet(MasterApi.DemosPath, (HttpContext context) => WithAccount(context, _ => DemoList(context)));
        app.MapPost(MasterApi.DemosPath + "/{id:long}/download", (HttpContext context, long id) => WithAccount(context, a => DemoDownload(a, id)));
    }

    private static void MapDemosDisabled(WebApplication app)
    {
        IResult Off() => Error(StatusCodes.Status503ServiceUnavailable, DemosOff);
        app.MapPost(MasterApi.DemoUploadPath, Off);
        app.MapPost(MasterApi.DemosPath + "/{id:long}/done", (long id) => Off());
        app.MapGet(MasterApi.DemosPath, Off);
        app.MapPost(MasterApi.DemosPath + "/{id:long}/download", (long id) => Off());
    }

    /// <summary>
    /// The trusted server of the request when it reported <paramref name="matchId"/> (the round is stored and its server
    /// id is registered now under the same key), else the refusal.
    /// </summary>
    private (MatchRow? Match, IResult? Refusal) TrustedMatch(HttpContext context, long matchId)
    {
        if (_options.TrustedServer(context.Request.Headers[MasterApi.ServerKeyHeader].ToString()) is not { } server)
        {
            return (null, Error(StatusCodes.Status403Forbidden, "Unknown server key."));
        }

        if (_store.Match(matchId) is not { } found)
        {
            return (null, Error(StatusCodes.Status404NotFound, "That round is not stored."));
        }

        bool owned;
        lock (_registry.Gate)
        {
            owned = _registry.Find(found.Match.ServerId) is { } record && string.Equals(record.TrustedName, server.Name, StringComparison.Ordinal);
        }

        return owned ? (found.Match, null) : (null, Error(StatusCodes.Status403Forbidden, "That round was reported by another server."));
    }

    private IResult DemoUpload(HttpContext context, DemoUploadRequest request)
    {
        if (_demos is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, DemosOff);
        }

        (MatchRow? match, IResult? refusal) = TrustedMatch(context, request.MatchId);
        if (match is null)
        {
            return refusal!;
        }

        if (request.Size <= 0 || request.Size > _demos.Options.MaxBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, $"A demo may be up to {_demos.Options.MaxBytes / (1024 * 1024)} MB.");
        }

        string sha = (request.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
        {
            return Error(StatusCodes.Status400BadRequest, "Sha256 must be 64 hex digits.");
        }

        if ((request.GameVersion?.Length ?? 0) > 64 || (request.NetVersion?.Length ?? 0) > 64 || (request.ContentHash?.Length ?? 0) > 64)
        {
            return Error(StatusCodes.Status400BadRequest, "The versions are too long.");
        }

        if (_store.DemoOfMatch(match.Id) is { State: DemoState.Ready })
        {
            return Error(StatusCodes.Status409Conflict, "That round already has a demo.");
        }

        string key = _demos.NewKey(match.Id);
        DemoRow row = _store.CreateDemo(match.Id, key, request.Size, sha, Math.Clamp(request.DurationSeconds, 0, 86400),
            request.GameVersion ?? string.Empty, request.NetVersion ?? string.Empty, request.ContentHash ?? string.Empty, _demos.Options.KeepDays);
        (string url, DateTimeOffset expires) = _demos.UploadUrl(key, request.Size);
        return Results.Json(new DemoUploadResponse
        {
            DemoId = row.Id, UploadUrl = url, ContentType = DemoStorage.ContentType, UrlExpires = expires,
        }, MasterApi.Json);
    }

    private IResult DemoDone(HttpContext context, long id)
    {
        if (_demos is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, DemosOff);
        }

        if (_store.Demo(id) is not { } demo)
        {
            return Error(StatusCodes.Status404NotFound, "Unknown demo.");
        }

        (MatchRow? match, IResult? refusal) = TrustedMatch(context, demo.MatchId);
        if (match is null)
        {
            return refusal!;
        }

        if (demo.State == DemoState.Ready)
        {
            return Results.Ok();
        }

        return _store.MarkDemoReady(id)
            ? Results.Ok()
            : Error(StatusCodes.Status410Gone, "The demo's upload window has passed; ask for a new link.");
    }

    private IResult DemoList(HttpContext context)
    {
        if (_demos is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, DemosOff);
        }

        int page = int.TryParse(context.Request.Query["page"].ToString(), out int p) ? Math.Clamp(p, 0, 10_000) : 0;
        (List<(DemoRow Demo, MatchRow Match, List<MatchPlayerRow> Players)> rows, int total) = _store.ReadyDemos(page, MasterApi.DemoPageSize);
        return Results.Json(new DemoListResponse
        {
            Page = page, PageSize = MasterApi.DemoPageSize, Total = total,
            Demos = rows.Select(r => new DemoEntry
            {
                Id = r.Demo.Id, MatchId = r.Match.Id, Map = r.Match.Map, Mode = r.Match.Mode, ServerName = r.Match.ServerName,
                Time = DateTimeOffset.FromUnixTimeSeconds(r.Match.Time),
                DurationSeconds = r.Demo.DurationSeconds > 0 ? r.Demo.DurationSeconds : r.Match.DurationSeconds,
                WinningTeam = r.Match.Winner, Size = r.Demo.Size, Sha256 = r.Demo.Sha256, GameVersion = r.Demo.GameVersion,
                NetVersion = r.Demo.NetVersion, ContentHash = r.Demo.ContentHash, Expires = DateTimeOffset.FromUnixTimeSeconds(r.Demo.Expires),
                Players = r.Players.Select(x => new DemoPlayer
                {
                    Name = x.Name, Team = x.Team, Score = x.Score, Kills = x.Kills, Deaths = x.Deaths, Bot = x.Bot,
                }).ToList(),
            }).ToList(),
        }, MasterApi.Json);
    }

    private IResult DemoDownload(AccountRow account, long id)
    {
        if (_demos is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, DemosOff);
        }

        if (_store.Demo(id) is not { State: DemoState.Ready } demo || demo.Expires <= Now)
        {
            return Error(StatusCodes.Status404NotFound, "That demo is no longer kept.");
        }

        if (!TakeDownload(account.Id))
        {
            return Error(StatusCodes.Status429TooManyRequests, $"You can download {DownloadsPerHour} demos an hour; try again later.");
        }

        (string url, DateTimeOffset expires) = _demos.DownloadUrl(demo.Key);
        return Results.Json(new DemoDownloadResponse { Url = url, UrlExpires = expires, Size = demo.Size, Sha256 = demo.Sha256 }, MasterApi.Json);
    }

    /// <summary>Counts a download link against the account's hourly allowance; false when it is used up.</summary>
    private bool TakeDownload(long accountId)
    {
        long now = Now;
        lock (_downloads)
        {
            if (!_downloads.TryGetValue(accountId, out Queue<long>? times))
            {
                if (_downloads.Count > 10_000)
                {
                    foreach (long idle in _downloads.Where(d => d.Value.Count == 0 || d.Value.Last() <= now - 3600).Select(d => d.Key).ToList())
                    {
                        _downloads.Remove(idle);
                    }
                }

                times = new Queue<long>();
                _downloads[accountId] = times;
            }

            while (times.Count > 0 && times.Peek() <= now - 3600)
            {
                times.Dequeue();
            }

            if (times.Count >= DownloadsPerHour)
            {
                return false;
            }

            times.Enqueue(now);
            return true;
        }
    }
}
