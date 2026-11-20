using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record BrowserGrant(string Address, long? Account, string Ip, DateTimeOffset Expires);
    private sealed record BrowserJoinRequest(string ServerId);
    private readonly ConcurrentDictionary<string, BrowserGrant> _browserGrants = new();
    private readonly ConcurrentDictionary<string, int> _browserConnections = new();
    private readonly SemaphoreSlim _browserSlots = new(64);
    private readonly MasterHost.RateLimiter _browserJoinLimit = new(30);

    private void MapBrowserMultiplayer(WebApplication app)
    {
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/play/servers", (HttpContext context) =>
        {
            Web.WebHtml.Secure(context);
            lock (_registry.Gate)
                return Results.Json(new { inviteOnly = _options.InviteKeys.Count > 0,
                    servers = _registry.Snapshot().Where(r => r.Trusted && r.Last.Listed && r.Last.Kind == ServerKind.Game)
                        .Select(r => new { id = r.Id, name = r.Last.Name, map = r.Last.Map, players = r.Last.Players,
                            password = r.Last.Password, requireAccounts = r.Last.RequireAccounts }).ToArray() }, MasterApi.Json);
        });
        app.MapPost("/play/join", (Func<HttpContext, Task<IResult>>)BrowserJoin);
        app.MapGet("/play/socket/{token}", (RequestDelegate)BrowserSocket);
    }

    private bool BrowserOrigin(HttpContext context) => context.Request.Headers.Origin == _options.PublicBaseUrl;

    private async Task<IResult> BrowserJoin(HttpContext context)
    {
        Web.WebHtml.Secure(context);
        _browserJoinLimit.Sweep();
        if (!_browserJoinLimit.Allow(_clientIp(context))) return Results.StatusCode(429);
        if (!BrowserOrigin(context) || !context.Request.HasJsonContentType() || context.Request.ContentLength is null or > 2048)
            return Results.BadRequest(new { error = "Reload the game and try again." });
        if (_options.InviteKeys.Count > 0 && !_options.InviteKeys.Contains(context.Request.Headers[MasterApi.KeyHeader].ToString(), StringComparer.Ordinal))
            return Results.Json(new { error = "This master needs its invite key." }, statusCode: 403);
        Viewer? viewer = Current(context);
        if (viewer is not null && context.Request.Headers["X-CSRF-Token"] != viewer.Session.Csrf)
            return Results.StatusCode(403);
        BrowserJoinRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<BrowserJoinRequest>(MasterApi.Json, context.RequestAborted); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(); }
        if (request?.ServerId is not { Length: > 0 and < 100 } id) return Results.BadRequest();
        string address, name;
        bool needsAccount;
        lock (_registry.Gate)
        {
            if (_registry.Find(id) is not { Trusted: true, Last.Listed: true } record || record.Last.Kind != ServerKind.Game)
                return Results.NotFound(new { error = "This server is not available for browser play." });
            address = LiteNetLibTransport.FormatEndPoint(new IPEndPoint(record.PublicIp, record.Last.GamePort));
            name = record.Last.Name;
            needsAccount = record.Last.RequireAccounts;
        }
        if (needsAccount && viewer is null) return Results.Json(new { error = "Sign in with Discord before joining this server." }, statusCode: 401);
        string? ticket = null;
        if (viewer is not null)
        {
            if (browserTickets is null) return Results.StatusCode(503);
            if (viewer.Account.Name is null) return Results.Json(new { error = "Choose your player name on the Account page first." }, statusCode: 400);
            if (_store.ActiveBan(viewer.Account.Id, viewer.Account.DiscordId, _clientIp(context)) is not null)
                return Results.Json(new { error = "Your account is banned from this master." }, statusCode: 403);
            ticket = browserTickets.Sign(new PlayerTicket
            {
                AccountId = viewer.Account.Id, Name = viewer.Account.Name, ServerId = id,
                Expires = DateTimeOffset.UtcNow + PlayerTicket.Lifetime, TicketId = PlayerTicket.NewTicketId(),
                Flags = _options.IsAdmin(viewer.Account.DiscordId) ? PlayerTicketFlags.Admin : PlayerTicketFlags.None,
                Unlocks = CosmeticUnlocks.Of(_store.Unlocked(viewer.Account.Id)).ToBytes(),
                Rank = _store.ProgressionOf(viewer.Account.Id).Rank.Pack(), Titles = TitleCatalog.ToBytes(_store.Titles(viewer.Account.Id)),
                WeaponKills = OpenScct.Core.Engine.WeaponCareerCounters.FromFeats(_store.Progress(viewer.Account.Id).Feats),
            });
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var pair in _browserGrants) if (pair.Value.Expires < now) _browserGrants.TryRemove(pair.Key, out _);
        if (_browserGrants.Count >= 256) return Results.StatusCode(429);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _browserGrants[token] = new(address, viewer?.Account.Id, _clientIp(context).ToString(), now.AddMinutes(1));
        return Results.Json(new { name, playerName = viewer?.Account.Name, ticket,
            socket = "/play/socket/" + token }, MasterApi.Json);
    }

    private async Task BrowserSocket(HttpContext context)
    {
        string ip = _clientIp(context).ToString();
        string token = context.Request.RouteValues["token"]?.ToString() ?? "";
        if (!context.WebSockets.IsWebSocketRequest || !BrowserOrigin(context) ||
            !_browserGrants.TryRemove(token, out BrowserGrant? grant) || grant.Expires < DateTimeOffset.UtcNow ||
            grant.Ip != ip || grant.Account != Current(context)?.Account.Id)
        { context.Response.StatusCode = 403; return; }
        int count = _browserConnections.AddOrUpdate(ip, 1, (_, old) => old + 1);
        bool slot = count <= 4 && await _browserSlots.WaitAsync(0, context.RequestAborted);
        try
        {
            if (!slot) { context.Response.StatusCode = 429; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await BrowserDatagramGateway.Run(socket, grant.Address, context.RequestAborted);
        }
        finally
        {
            if (slot) _browserSlots.Release();
            _browserConnections.AddOrUpdate(ip, 0, (_, old) => Math.Max(0, old - 1));
            ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, int>>)_browserConnections)
                .Remove(new(ip, 0));
        }
    }
}
