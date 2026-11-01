using System;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

/// <summary>The public player site and its independent, account-scoped browser session.</summary>
internal sealed partial class PlayerPortal(MasterOptions options, AccountStore store, ServerRegistry registry, Func<HttpContext, IPAddress> clientIp,
    ReleaseChangelog releases)
{
    private const string SessionCookie = "svm_player";
    private const string StateCookie = "svm_player_state";
    private const int PageSize = 24;
    private readonly MasterOptions _options = options;
    private readonly AccountStore _store = store;
    private readonly ServerRegistry _registry = registry;
    private readonly Func<HttpContext, IPAddress> _clientIp = clientIp;
    private readonly ReleaseChangelog _releases = releases;
    private sealed record Viewer(AccountRow Account, PlayerWebSessionRow Session);

    public void Map(WebApplication app)
    {
        Get(app, "/", Home);
        Get(app, "/download", Download);
        app.MapGet("/changelog", (Func<HttpContext, Task<IResult>>)Changelog);
        Get(app, "/servers", Servers);
        Get(app, "/players", Players);
        Get(app, "/players/{id:long}", c => Profile(c, Id(c)));
        Get(app, "/leaderboards", Leaderboards);
        Get(app, "/matches", Matches);
        Get(app, "/matches/{id:long}", c => Match(c, Id(c)));
        Get(app, "/account", c => WithViewer(c, v => Account(c, v)));
        Get(app, "/account/appearance", c => WithViewer(c, v => Appearance(c, v)));
        Get(app, "/account/security", c => WithViewer(c, v => Security(c, v)));
        Get(app, "/account/privacy", c => WithViewer(c, v => Privacy(c, v)));
        Get(app, "/auth/player/login", Login);
        Post(app, "/account/name", (c, v, f) => ChangeName(c, v, f));
        Post(app, "/account/privacy", (c, v, f) => ChangePrivacy(c, v, f));
        Post(app, "/account/prestige", (c, v, f) => Prestige(c, v, f));
        Post(app, "/account/appearance", (c, v, f) => ChangeAppearance(c, v, f));
        Post(app, "/account/security", (c, v, f) => RevokeSession(c, v, f));
        Post(app, "/account/logout", (c, v, f) => Logout(c));
    }

    private static void Get(WebApplication app, string pattern, Func<HttpContext, IResult> handler) =>
        app.MapGet(pattern, (HttpContext c) => handler(c));

    private void Post(WebApplication app, string pattern, Func<HttpContext, Viewer, IFormCollection, IResult> handler) =>
        app.MapPost(pattern, (RequestDelegate)(async c =>
        {
            Secure(c);
            if (Current(c) is not { } viewer)
            {
                await SignIn(c, "Your website session ended. Sign in to continue.", 401).ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            if (!c.Request.HasFormContentType || c.Request.ContentLength > 32 * 1024)
            {
                c.Response.StatusCode = 400;
                return;
            }
            IFormCollection form = await c.Request.ReadFormAsync(c.RequestAborted).ConfigureAwait(false);
            string sent = form["csrf"].ToString();
            if (sent.Length != viewer.Session.Csrf.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(viewer.Session.Csrf)))
            {
                await Show(c, "Form expired", "account", PageHeader("ACCOUNT", "This form has expired", "Reload the page and try again."), status: 403)
                    .ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            using (AccountStore.ActingAs("player:" + viewer.Account.Id.ToString(CultureInfo.InvariantCulture)))
                await handler(c, viewer, form).ExecuteAsync(c).ConfigureAwait(false);
        }));

    private Viewer? Current(HttpContext c)
    {
        if (c.Items.TryGetValue("player.viewer", out object? cached)) return cached as Viewer;
        Viewer? viewer = null;
        if (c.Request.Cookies[SessionCookie] is { Length: > 0 and < 100 } token && _store.PlayerWebSession(token) is { } session &&
            _store.FindById(session.AccountId) is { } account) viewer = new(account, session);
        c.Items["player.viewer"] = viewer;
        return viewer;
    }

    private IResult WithViewer(HttpContext c, Func<Viewer, IResult> body) => Current(c) is { } v ? body(v) : SignIn(c);

    private IResult Show(HttpContext c, string title, string active, string body, string? notice = null, bool bad = false, int status = 200)
    {
        Viewer? viewer = Current(c);
        return Page(c, title, active, body, viewer?.Account.Name ?? (viewer is null ? null : "My account"), viewer?.Session.Csrf, notice, bad, status,
            isAdmin: viewer is not null && _options.IsAdmin(viewer.Account.DiscordId));
    }

    private IResult NotFound(HttpContext c, string title = "Player unavailable") =>
        Show(c, title, string.Empty, PageHeader("NOT FOUND", title, "This page is unavailable or its owner has made it private.") +
            Link("/", "Back to the player hub", "button secondary"), status: 404);

    private IResult SignIn(HttpContext c, string? problem = null, int status = 200) =>
        Show(c, "Sign in", "account", $"<section class=\"panel signed-out\"><img src=\"{H(AssetUrl("/web/assets/mark.svg"))}\" alt=\"\" width=\"58\" height=\"58\">" +
            "<p class=\"eyebrow\">YOUR OPENSCCT ACCOUNT</p><h1>Sign in</h1><p class=\"lead\">View your stats and manage your account. " +
            "Use the same Discord account as in the game.</p>" + Link("/auth/player/login", "Continue with Discord →", "button wide") +
            "<small>We use your Discord ID to recognize your account. We never ask for your password, email, or friends list.</small></section>", problem, problem is not null, status);

    private IResult Login(HttpContext c)
    {
        Secure(c);
        string state = _store.CreatePlayerOAuthState();
        c.Response.Cookies.Append(StateCookie, state, Cookie(c, "/", SameSiteMode.Lax, TimeSpan.FromMinutes(10)));
        return Results.Redirect(DiscordAuth.AuthorizeUrl(_options, "player." + state));
    }

    public async Task<IResult> CallbackAsync(HttpContext c)
    {
        Secure(c);
        string state = c.Request.Query["state"].ToString();
        string nonce = state.StartsWith("player.", StringComparison.Ordinal) ? state[7..] : string.Empty;
        string expected = c.Request.Cookies[StateCookie] ?? string.Empty;
        c.Response.Cookies.Delete(StateCookie, Cookie(c, "/", SameSiteMode.Lax, null));
        if (nonce.Length != 64 || expected.Length != nonce.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expected)) || !_store.ConsumePlayerOAuthState(nonce))
            return SignIn(c, "This sign-in expired or was already used. Please start again.", 400);
        string code = c.Request.Query["code"].ToString();
        if (c.Request.Query["error"].Count > 0 || code.Length is 0 or > 256)
            return SignIn(c, "Discord sign-in was not completed. You can try again when you are ready.", 400);
        string discordId;
        try { discordId = await DiscordAuth.IdentifyAsync(_options, code, c.RequestAborted).ConfigureAwait(false); }
        catch (DiscordAuthException) { return SignIn(c, "Discord could not complete the sign-in. Please try again in a moment.", 502); }
        AccountRow account = _store.GetOrCreate(discordId);
        string token = _store.CreatePlayerWebSession(account.Id);
        c.Response.Cookies.Append(SessionCookie, token, Cookie(c, "/", SameSiteMode.Strict, TimeSpan.FromSeconds(AccountStore.PlayerWebMax)));
        // A same-site document hop is required for the Strict session cookie after Discord's redirect.
        return Results.Content("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            $"<meta http-equiv=\"refresh\" content=\"0;url=/account\"><title>Signed in · OpenSCCT</title><link rel=\"stylesheet\" href=\"{H(AssetUrl("/web/assets/site.css"))}\"></head>" +
            "<body><main class=\"site-main\"><h1>Signed in.</h1><a class=\"button\" href=\"/account\">Continue to your account</a></main></body></html>", "text/html; charset=utf-8");
    }

    private CookieOptions Cookie(HttpContext c, string path, SameSiteMode sameSite, TimeSpan? maxAge) => new()
    {
        HttpOnly = true, Secure = _options.PublicBaseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || c.Request.IsHttps,
        SameSite = sameSite, Path = path, MaxAge = maxAge,
    };

    private IResult Logout(HttpContext c)
    {
        if (c.Request.Cookies[SessionCookie] is { } token) _store.EndPlayerWebSession(token);
        c.Response.Cookies.Delete(SessionCookie, Cookie(c, "/", SameSiteMode.Strict, null));
        return Results.Redirect("/");
    }

    private static long Id(HttpContext c) => long.TryParse(c.Request.RouteValues["id"]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : -1;
    private static int PageOf(HttpContext c) => int.TryParse(c.Request.Query["page"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? Math.Clamp(p, 0, 10_000) : 0;
    private static string Field(IFormCollection form, string key, int max = 100) => Limit(form[key].ToString(), max);
    private static string Query(HttpContext c, string key, int max = 64) => Limit(c.Request.Query[key].ToString(), max);
    private static string Limit(string value, int max)
    {
        string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length > max ? clean[..max] : clean;
    }
    private static string Form(string action, string csrf, string content, string? confirm = null, string css = "form-stack") =>
        $"<form method=\"post\" action=\"{H(action)}\" class=\"{H(css)}\"{(confirm is null ? "" : " data-confirm=\"" + H(confirm) + "\"")}>{Csrf(csrf)}{content}</form>";
}
