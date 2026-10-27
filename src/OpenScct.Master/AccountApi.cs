using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// The account side of the master (docs/ONLINE.md, "Accounts"): Discord sign-in from the game, the account calls,
/// join tickets, ban kicks for heartbeats and round results from trusted servers.
/// </summary>
internal sealed class AccountApi
{
    private const string SignInAgain = "Sign in again.";

    private readonly MasterOptions _options;
    private readonly AccountStore _store;
    private readonly TicketSigner _signer;
    private readonly ServerRegistry _registry;
    private readonly Func<HttpContext, IPAddress> _clientIp;
    private readonly TimeProvider _time;

    public AccountApi(MasterOptions options, AccountStore store, TicketSigner signer, ServerRegistry registry,
        Func<HttpContext, IPAddress> clientIp, TimeProvider time)
    {
        _options = options;
        _store = store;
        _signer = signer;
        _registry = registry;
        _clientIp = clientIp;
        _time = time;
        Links = new PendingLinks(time);
    }

    public PendingLinks Links { get; }

    /// <summary>The admin portal's sign-in, for Discord answers whose state starts with "admin." (one callback URL for both).</summary>
    public Func<HttpContext, Task<IResult>>? AdminCallback { get; set; }

    public string TicketKey => _signer.PublicKey;

    public void Map(WebApplication app)
    {
        app.MapPost(MasterApi.LinkPath, StartLink);
        app.MapPost(MasterApi.LinkPath + "/{code}", (string code, LinkPollRequest request) => PollLink(code, request));
        app.MapGet(MasterApi.LinkPagePrefix + "{code}", (HttpContext context, string code) => LinkPage(context, code));
        app.MapGet(MasterApi.DiscordCallbackPath, (Delegate)((HttpContext context) => CallbackAsync(context)));
        app.MapGet(MasterApi.AccountPath, (HttpContext context) => WithAccount(context, a => Results.Json(Info(a), MasterApi.Json)));
        app.MapPost(MasterApi.AccountNamePath, (HttpContext context, NameRequest request) => WithAccount(context, a => ClaimName(a, request)));
        app.MapPost(MasterApi.SignOutPath, SignOut);
        app.MapGet(MasterApi.ProfilePath, (HttpContext context) => WithAccount(context, a =>
            _store.Profile(a.Id) is { } p ? Results.Json(p, MasterApi.Json) : Results.NoContent()));
        app.MapPut(MasterApi.ProfilePath, (HttpContext context, ProfileData profile) => WithAccount(context, a => PutProfile(a, profile)));
        app.MapPost(MasterApi.ResultsPath, (HttpContext context, ResultsReport report) => PostResults(context, report));
    }

    /// <summary>The same paths when sign-in is not set up: each answers 503.</summary>
    public static void MapDisabled(WebApplication app)
    {
        IResult Off() => Error(StatusCodes.Status503ServiceUnavailable, "Sign-in is not set up on this master.");
        app.MapPost(MasterApi.LinkPath, Off);
        app.MapPost(MasterApi.LinkPath + "/{code}", (string code) => Off());
        app.MapGet(MasterApi.LinkPagePrefix + "{code}", (string code) =>
            Page(StatusCodes.Status503ServiceUnavailable, "Sign-in is off", "Sign-in is not set up on this master."));
        app.MapGet(MasterApi.DiscordCallbackPath, () =>
            Page(StatusCodes.Status503ServiceUnavailable, "Sign-in is off", "Sign-in is not set up on this master."));
        app.MapGet(MasterApi.AccountPath, Off);
        app.MapPost(MasterApi.AccountNamePath, Off);
        app.MapPost(MasterApi.SignOutPath, Off);
        app.MapGet(MasterApi.ProfilePath, Off);
        app.MapPut(MasterApi.ProfilePath, Off);
        app.MapPost(MasterApi.ResultsPath, () => Error(StatusCodes.Status503ServiceUnavailable, "This master keeps no accounts or results."));
    }

    private static IResult Error(int status, string message) =>
        Results.Json(new MasterError { Error = message }, MasterApi.Json, statusCode: status);

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    // ---- Sign-in ---------------------------------------------------------------------------------------------

    private IResult StartLink()
    {
        if (Links.Start() is not { } link)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "Too many sign-ins in progress; try again in a minute.");
        }

        return Results.Json(new LinkStartResponse
        {
            Code = link.Code, Url = _options.PublicBaseUrl + MasterApi.LinkPagePrefix + link.Code, PollSecret = link.PollSecret,
            ExpiresSeconds = PendingLinks.LifetimeSeconds,
        }, MasterApi.Json);
    }

    private IResult PollLink(string code, LinkPollRequest request)
    {
        if (Links.Find(code) is not { } link || !PendingLinks.SecretMatches(link, request.PollSecret))
        {
            return Error(StatusCodes.Status404NotFound, "Unknown sign-in; start again.");
        }

        switch (link.Status)
        {
            case LinkStatus.Done:
                Links.Remove(link);
                AccountRow? account = _store.FindById(link.AccountId);
                return Results.Json(new LinkPollResponse
                {
                    Status = LinkStatus.Done, SessionToken = link.SessionToken, Account = account is null ? null : Info(account),
                }, MasterApi.Json);
            case LinkStatus.Refused:
            case LinkStatus.Expired:
                Links.Remove(link);
                return Results.Json(new LinkPollResponse
                {
                    Status = link.Status, Error = link.Error ?? (link.Status == LinkStatus.Expired ? "The sign-in expired; start again." : null),
                }, MasterApi.Json);
            default:
                return Results.Json(new LinkPollResponse { Status = LinkStatus.Pending }, MasterApi.Json);
        }
    }

    private IResult LinkPage(HttpContext context, string code)
    {
        NoStore(context);
        if (Links.Find(code) is not { Status: LinkStatus.Pending } link)
        {
            return Page(StatusCodes.Status404NotFound, "Sign-in expired",
                "This sign-in link is unknown or has expired. Start the sign-in again from the game.");
        }

        // A confirm step before Discord: someone could send a player his own sign-in link, and Discord passes an
        // approved app straight through, so the player would sign the stranger's game into his account. The page
        // shows the code to compare with the game, and the button only works in the browser that saw this page (its
        // cookie carries the same one-time value as the button's link).
        string go = context.Request.Query["go"].ToString();
        if (go.Length > 0 && context.Request.Cookies[LinkCookie] is { Length: > 0 } cookie &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(go), Encoding.UTF8.GetBytes(cookie)))
        {
            context.Response.Cookies.Delete(LinkCookie, new CookieOptions { Path = MasterApi.LinkPagePrefix.TrimEnd('/') });
            return Results.Redirect(DiscordAuth.AuthorizeUrl(_options, link.Code));
        }

        string nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        context.Response.Cookies.Append(LinkCookie, nonce, new CookieOptions
        {
            HttpOnly = true, Secure = context.Request.IsHttps || _options.PublicBaseUrl.StartsWith("https:", StringComparison.Ordinal),
            SameSite = SameSiteMode.Lax, Path = MasterApi.LinkPagePrefix.TrimEnd('/'), MaxAge = TimeSpan.FromMinutes(10),
        });
        string href = MasterApi.LinkPagePrefix + Uri.EscapeDataString(link.Code) + "?go=" + nonce;
        return Page(StatusCodes.Status200OK, "Sign in to OpenSCCT",
            "Check that this code is the one your game shows. If you did not start this sign-in yourself, close this page.",
            $"""<p class="code">{WebUtility.HtmlEncode(link.Code)}</p><p><a class="button" href="{WebUtility.HtmlEncode(href)}">Continue with Discord</a></p>""");
    }

    private const string LinkCookie = "svm_link";

    private async Task<IResult> CallbackAsync(HttpContext context)
    {
        NoStore(context);
        string state = context.Request.Query["state"].ToString();
        if (state.StartsWith("admin.", StringComparison.Ordinal) && AdminCallback is { } admin)
        {
            return await admin(context).ConfigureAwait(false);
        }

        string code = context.Request.Query["code"].ToString();
        string error = context.Request.Query["error"].ToString();
        if (Links.Find(state) is not { } link || link.Status == LinkStatus.Expired)
        {
            return Page(StatusCodes.Status404NotFound, "Sign-in expired",
                "This sign-in is unknown or has expired. Start the sign-in again from the game.");
        }

        if (!Links.TryBegin(link))
        {
            return Page(StatusCodes.Status409Conflict, "Already used", "This sign-in has already been used. Go back to the game.");
        }

        if (error.Length > 0 || code.Length == 0 || code.Length > 256)
        {
            string why = error == "access_denied" ? "The sign-in was cancelled on Discord." : "Discord did not finish the sign-in.";
            Links.Refuse(link, why);
            return Page(StatusCodes.Status400BadRequest, "Not signed in", why + " You can start again from the game.");
        }

        string discordId;
        try
        {
            discordId = await DiscordAuth.IdentifyAsync(_options, code, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DiscordAuthException e)
        {
            Links.Refuse(link, e.Message);
            return Page(StatusCodes.Status502BadGateway, "Not signed in", e.Message);
        }
        catch (OperationCanceledException)
        {
            Links.Refuse(link, "The browser left before the sign-in finished.");
            throw;
        }

        AccountRow account = _store.GetOrCreate(discordId);
        if (_store.ActiveBan(account.Id, discordId, _clientIp(context)) is { } ban)
        {
            string why = "This account is banned: " + ban.Describe();
            Links.Refuse(link, why);
            return Page(StatusCodes.Status403Forbidden, "Banned", why);
        }

        string token = _store.CreateSession(account.Id);
        Links.Complete(link, token, account.Id);
        string greeting = account.Name is null ? "Signed in." : $"Signed in as {account.Name}.";
        return Page(StatusCodes.Status200OK, "Signed in", greeting + " You can go back to the game.");
    }

    // ---- Account calls ---------------------------------------------------------------------------------------

    /// <summary>The bearer token of the request, or null when it carries none.</summary>
    private static string? BearerToken(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string token = header[7..].Trim();
        return token.Length is > 0 and <= 128 ? token : string.Empty;
    }

    private AccountRow? Authenticate(HttpContext context) =>
        BearerToken(context) is { Length: > 0 } token ? _store.Authenticate(token) : null;

    private IResult WithAccount(HttpContext context, Func<AccountRow, IResult> body) =>
        Authenticate(context) is { } account ? body(account) : Error(StatusCodes.Status401Unauthorized, SignInAgain);

    private IResult SignOut(HttpContext context)
    {
        if (BearerToken(context) is not { Length: > 0 } token || !_store.DeleteSession(token))
        {
            return Error(StatusCodes.Status401Unauthorized, SignInAgain);
        }

        return Results.Ok();
    }

    /// <summary>The account as the game shows it.</summary>
    public AccountInfo Info(AccountRow account)
    {
        Dictionary<string, long> earned = _store.Earned(account.Id);
        AccountProgress progress = _store.Progress(account.Id);
        var achievements = new List<AchievementInfo>(Achievements.All.Count);
        foreach (Achievement a in Achievements.All)
        {
            bool done = earned.TryGetValue(a.Id, out long at);
            (int current, int target) = a.Goal(progress);
            achievements.Add(new AchievementInfo
            {
                Id = a.Id, Title = a.Title, Description = a.Description,
                Earned = done ? DateTimeOffset.FromUnixTimeSeconds(at) : null,
                Category = a.Category.ToString(), Current = done ? target : Math.Min(current, target), Target = target, Rewards = a.Rewards,
            });
        }

        long? allowed = account.Name is not null && account.NameChanged is { } changed && changed + AccountStore.RenameCooldown > Now
            ? changed + AccountStore.RenameCooldown
            : null;
        return new AccountInfo
        {
            Id = account.Id, Name = account.Name, Admin = _options.IsAdmin(account.DiscordId),
            NameChangeAllowed = allowed is { } t ? DateTimeOffset.FromUnixTimeSeconds(t) : null,
            Stats = _store.Stats(account.Id), Achievements = achievements, Unlocked = _store.Unlocked(account.Id),
        };
    }

    private IResult ClaimName(AccountRow account, NameRequest request)
    {
        string name = (request.Name ?? string.Empty).Trim();
        if (PlayerNames.ClaimProblem(name) is { } problem)
        {
            return Error(StatusCodes.Status400BadRequest, problem);
        }

        switch (_store.ClaimName(account.Id, name, ignoreCooldown: false, out long retryAt))
        {
            case NameClaim.Taken:
                return Error(StatusCodes.Status409Conflict, "That name is taken.");
            case NameClaim.TooSoon:
                return Error(StatusCodes.Status403Forbidden, $"You can change your name again on {AccountStore.Format(retryAt)}.");
            case NameClaim.NoAccount:
                return Error(StatusCodes.Status401Unauthorized, SignInAgain);
            default:
                return _store.FindById(account.Id) is { } updated
                    ? Results.Json(Info(updated), MasterApi.Json)
                    : Error(StatusCodes.Status401Unauthorized, SignInAgain);
        }
    }

    private IResult PutProfile(AccountRow account, ProfileData profile)
    {
        if (profile.Data is null)
        {
            return Error(StatusCodes.Status400BadRequest, "The settings are missing.");
        }

        if (Encoding.UTF8.GetByteCount(profile.Data) > MasterApi.MaxProfileBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge,
                $"The settings are too large to keep ({MasterApi.MaxProfileBytes / 1024} KB at most).");
        }

        return Results.Json(_store.PutProfile(account.Id, profile), MasterApi.Json);
    }

    // ---- Tickets, kicks, results -----------------------------------------------------------------------------

    /// <summary>
    /// The join ticket for the signed-in asker of <paramref name="serverId"/>: (null, null) when the join was sent
    /// signed out, else the ticket or why there is none.
    /// </summary>
    public (string? Ticket, string? Problem) Ticket(HttpContext context, string serverId)
    {
        if (BearerToken(context) is not { } token)
        {
            return (null, null);
        }

        if (token.Length == 0 || _store.Authenticate(token) is not { } account)
        {
            return (null, SignInAgain);
        }

        if (account.Name is null)
        {
            return (null, "Pick a name first (Account page).");
        }

        if (_store.ActiveBan(account.Id, account.DiscordId, _clientIp(context)) is { } ban)
        {
            return (null, "You are banned: " + ban.Describe());
        }

        var ticket = new PlayerTicket
        {
            AccountId = account.Id, Name = account.Name, ServerId = serverId, Expires = _time.GetUtcNow() + PlayerTicket.Lifetime,
            TicketId = PlayerTicket.NewTicketId(), Flags = _options.IsAdmin(account.DiscordId) ? PlayerTicketFlags.Admin : PlayerTicketFlags.None,
            Unlocks = CosmeticUnlocks.Of(_store.Unlocked(account.Id)).ToBytes(),
        };
        return (_signer.Sign(ticket), null);
    }

    /// <summary>The accounts of a heartbeat's player list that are banned now.</summary>
    public IReadOnlyList<long> Kicks(IReadOnlyList<long> accountIds) => _store.BannedAmong(accountIds);

    private IResult PostResults(HttpContext context, ResultsReport report)
    {
        if (_options.TrustedServer(context.Request.Headers[MasterApi.ServerKeyHeader].ToString()) is not { } server)
        {
            return Error(StatusCodes.Status403Forbidden, "Unknown server key.");
        }

        bool trusted;
        lock (_registry.Gate)
        {
            trusted = _registry.Find(report.ServerId) is { } record && string.Equals(record.TrustedName, server.Name, StringComparison.Ordinal);
        }

        if (!trusted)
        {
            return Error(StatusCodes.Status403Forbidden, "This server is not registered as trusted; send a heartbeat with its key first.");
        }

        if (report.Rows.Count > 64 || report.Map.Length > 64)
        {
            return Error(StatusCodes.Status400BadRequest, "The report is too large.");
        }

        bool counted = Countable(report);
        if (counted)
        {
            _store.ApplyResults(report, server.Name);
        }

        string serverName;
        lock (_registry.Gate)
        {
            serverName = _registry.Find(report.ServerId)?.Last.Name ?? server.Name;
        }

        _store.RecordMatch(report, serverName, counted);

        return Results.Ok();
    }

    /// <summary>A round with a winner and at least one signed-in player on each team.</summary>
    private static bool Countable(ResultsReport report)
    {
        if (report.WinningTeam is not (0 or 1))
        {
            return false;
        }

        bool spies = false, mercs = false;
        foreach (ResultsReportRow row in report.Rows)
        {
            if (row.Bot || row.AccountId is null)
            {
                continue;
            }

            spies |= row.Team == 0;
            mercs |= row.Team == 1;
        }

        return spies && mercs;
    }

    // ---- Pages -----------------------------------------------------------------------------------------------

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    /// <summary>A small plain page for the browser side of the sign-in.</summary>
    private static IResult Page(int status, string title, string message, string extraHtml = "")
    {
        string html = string.Create(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>OpenSCCT - {{WebUtility.HtmlEncode(title)}}</title>
            <style>
            body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; background: #0d1210;
                   color: #d8e4dc; font: 16px/1.5 system-ui, sans-serif; }
            main { max-width: 28rem; margin: 1rem; padding: 2rem; border: 1px solid #2f4a3a; background: #141c18; }
            h1 { margin: 0 0 .5rem; font-size: 1.3rem; color: #8fe0a8; letter-spacing: .05em; text-transform: uppercase; }
            p { margin: 0; }
            .code { margin: 1.25rem 0; font: 700 2rem/1 ui-monospace, monospace; letter-spacing: .15em; color: #fff; text-align: center; }
            .button { display: block; padding: .75rem 1rem; background: #5865f2; color: #fff; text-align: center; text-decoration: none;
                      font-weight: 600; border-radius: 4px; }
            </style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p>{{extraHtml}}</main></body></html>
            """);
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, status);
    }
}
