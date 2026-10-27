using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using static OpenScct.Master.Admin.AdminHtml;

namespace OpenScct.Master.Admin;

/// <summary>
/// The admin portal (docs/ONLINE.md "Admin portal"): web pages under <c>/admin</c> for the master's admins
/// (<see cref="MasterOptions.Admins"/>), signed in with Discord through the game's own callback. Sessions live in the
/// account database; every POST carries the session's CSRF token; every change is audited as "admin:&lt;name&gt;".
/// </summary>
internal sealed partial class AdminPortal
{
    private const string SessionCookie = "svm_admin";
    private const string StateCookie = "svm_admin_state";

    private readonly MasterOptions _options;
    private readonly AccountStore _store;
    private readonly ServerRegistry _registry;
    private readonly AdminActions _actions;
    private readonly Func<HttpContext, IPAddress> _clientIp;
    private readonly Func<(long RelayForwarded, long Introductions)> _udpStats;
    private readonly DemoStorage? _demos;
    private long _nextCommandId = 1;

    public AdminPortal(MasterOptions options, AccountStore store, ServerRegistry registry, Func<HttpContext, IPAddress> clientIp,
        Func<(long RelayForwarded, long Introductions)> udpStats, DemoStorage? demos = null)
    {
        _demos = demos;
        _options = options;
        _store = store;
        _registry = registry;
        _actions = new AdminActions(store);
        _clientIp = clientIp;
        _udpStats = udpStats;
    }

    private bool SecureCookies => _options.PublicBaseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase);

    public void Map(WebApplication app)
    {
        Route(app, "GET", "/admin/assets/{file}", c => Asset(c, Arg(c, "file")));
        Route(app, "GET", "/admin/login", Login);
        Route(app, "POST", "/admin/logout", c => Post(c, (_, _) => Task.FromResult(Logout(c))));

        Route(app, "GET", "/admin", c => Get(c, Dashboard));
        Route(app, "GET", "/admin/servers", c => Get(c, Servers));
        Route(app, "GET", "/admin/servers/{id}", c => Get(c, (cx, a) => Server(cx, a, Arg(c, "id"), null, false)));
        Route(app, "POST", "/admin/servers/blocks", c => Post(c, (a, f) => Task.FromResult(BlockAction(c, a, f))));
        Route(app, "POST", "/admin/servers/{id}", c => Post(c, (a, f) => Task.FromResult(ServerAction(c, a, Arg(c, "id"), f))));
        Route(app, "GET", "/admin/players", c => Get(c, Players));
        Route(app, "GET", "/admin/players/{id:long}", c => Get(c, (cx, a) => Player(cx, a, Id(c), null, false)));
        Route(app, "POST", "/admin/players/{id:long}", c => Post(c, (a, f) => Task.FromResult(PlayerAction(c, a, Id(c), f))));
        Route(app, "GET", "/admin/bans", c => Get(c, (cx, a) => Bans(cx, a, null, false)));
        Route(app, "POST", "/admin/bans", c => Post(c, (a, f) => Task.FromResult(BanAction(c, a, f))));
        Route(app, "GET", "/admin/matches", c => Get(c, Matches));
        Route(app, "GET", "/admin/matches/{id:long}", c => Get(c, (cx, a) => Match(cx, a, Id(c))));
        Route(app, "GET", "/admin/matches/{id:long}/demo", c => Get(c, (cx, a) => MatchDemo(cx, a, Id(c))));
        Route(app, "GET", "/admin/history", c => Get(c, History));
        Route(app, "GET", "/admin/settings", c => Get(c, (cx, a) => Settings(cx, a, null, false)));
        Route(app, "POST", "/admin/settings", c => Post(c, (a, f) => Task.FromResult(SettingsAction(c, a, f))));
        Route(app, "GET", "/admin/audit", c => Get(c, Audit));
    }

    private static void Route(WebApplication app, string method, string pattern, Func<HttpContext, IResult> handler) =>
        Route(app, method, pattern, c => Task.FromResult(handler(c)));

    private static void Route(WebApplication app, string method, string pattern, Func<HttpContext, Task<IResult>> handler) =>
        app.MapMethods(pattern, [method], (RequestDelegate)(async context =>
        {
            IResult result = await handler(context).ConfigureAwait(false);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        }));

    private static string Arg(HttpContext context, string name) => context.Request.RouteValues[name]?.ToString() ?? string.Empty;

    private static long Id(HttpContext context) =>
        long.TryParse(Arg(context, "id"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : -1;

    // ---- Sign-in -----------------------------------------------------------------------------------------------

    /// <summary>Starts the Discord sign-in: a random state in a short cookie, then Discord's authorize page.</summary>
    private IResult Login(HttpContext context)
    {
        string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        context.Response.Cookies.Append(StateCookie, nonce, new CookieOptions
        {
            HttpOnly = true, Secure = SecureCookies || context.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/",
            MaxAge = TimeSpan.FromMinutes(10),
        });
        Secure(context);
        return Results.Redirect(DiscordAuth.AuthorizeUrl(_options, "admin." + nonce));
    }

    /// <summary>Discord's answer for an admin sign-in (called from the shared callback).</summary>
    public async Task<IResult> CallbackAsync(HttpContext context)
    {
        string state = context.Request.Query["state"].ToString();
        string code = context.Request.Query["code"].ToString();
        string expected = context.Request.Cookies[StateCookie] ?? string.Empty;
        context.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/" });
        string nonce = state.Length > 6 ? state[6..] : string.Empty;
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expected)))
        {
            return SignInPage(context, "This sign-in expired or was started elsewhere. Try again.");
        }

        if (code.Length == 0 || code.Length > 256)
        {
            return SignInPage(context, "Discord did not finish the sign-in.");
        }

        string discordId;
        try
        {
            discordId = await DiscordAuth.IdentifyAsync(_options, code, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DiscordAuthException e)
        {
            return SignInPage(context, e.Message);
        }

        if (!_options.IsAdmin(discordId))
        {
            _store.Audit("admin.refused", "discord " + discordId + " from " + _clientIp(context));
            return SignInPage(context, "This Discord account is not an admin of this master.");
        }

        string name = _store.FindByDiscord(discordId)?.Name ?? discordId;
        string token = _store.CreateAdminSession(discordId, name, _clientIp(context).ToString(), context.Request.Headers.UserAgent.ToString());
        context.Response.Cookies.Append(SessionCookie, token, new CookieOptions
        {
            HttpOnly = true, Secure = SecureCookies || context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/admin",
            MaxAge = TimeSpan.FromSeconds(AccountStore.AdminSessionMax),
        });
        using (AccountStore.ActingAs("admin:" + name))
        {
            _store.Audit("admin.signin", "from " + _clientIp(context));
        }

        // A same-site hop: the strict session cookie is not sent on the redirect chain that started at Discord.
        Secure(context);
        return Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0;url=/admin\">" +
            "<title>Signed in</title><link rel=\"stylesheet\" href=\"/admin/assets/admin.css\"></head><body><main id=\"main\">" +
            "<p><a href=\"/admin\">Continue to the admin portal</a></p></main></body></html>",
            "text/html; charset=utf-8", Encoding.UTF8);
    }

    private static IResult SignInPage(HttpContext context, string? problem = null) =>
        Page(context, "Admin sign-in", string.Empty, null,
            "<p>The OpenSCCT master's admin portal. Admins sign in with the Discord account the master lists.</p>" +
            "<p><a class=\"button discord\" href=\"/admin/login\">Sign in with Discord</a></p>",
            problem, noticeBad: problem is not null, status: problem is null ? 200 : 403);

    private IResult Logout(HttpContext context)
    {
        if (context.Request.Cookies[SessionCookie] is { Length: > 0 } token)
        {
            _store.EndAdminSession(token);
        }

        context.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/admin" });
        return Results.Redirect("/admin");
    }

    /// <summary>The signed-in admin of the request, or null (no cookie, session over, or no longer an admin).</summary>
    private AdminSessionRow? Admin(HttpContext context)
    {
        if (context.Request.Cookies[SessionCookie] is not { Length: > 0 and < 100 } token || _store.AdminSession(token) is not { } session)
        {
            return null;
        }

        if (!_options.IsAdmin(session.DiscordId))
        {
            _store.EndAdminSession(token);
            return null;
        }

        context.Items[CsrfField] = session.Csrf;
        return session;
    }

    private IResult Get(HttpContext context, Func<HttpContext, AdminSessionRow, IResult> page) =>
        Admin(context) is { } admin ? page(context, admin) : SignInPage(context);

    private async Task<IResult> Post(HttpContext context, Func<AdminSessionRow, IFormCollection, Task<IResult>> action)
    {
        if (Admin(context) is not { } admin)
        {
            return SignInPage(context, "Your admin session ended; sign in again.");
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        string sent = form[CsrfField].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(admin.Csrf)))
        {
            return Page(context, "Refused", string.Empty, admin.Name, "<p>The form was stale. Go back, reload the page and try again.</p>",
                status: 403);
        }

        using (AccountStore.ActingAs("admin:" + admin.Name))
        {
            return await action(admin, form).ConfigureAwait(false);
        }
    }

    // ---- Assets ------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, (byte[] Bytes, string Type)> s_assets = LoadAssets();

    private static Dictionary<string, (byte[], string)> LoadAssets()
    {
        var assets = new Dictionary<string, (byte[], string)>(StringComparer.Ordinal);
        Assembly assembly = typeof(AdminPortal).Assembly;
        foreach (string file in new[] { "admin.css", "admin.js" })
        {
            using Stream? stream = assembly.GetManifestResourceStream("admin/" + file);
            if (stream is null)
            {
                continue;
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            assets[file] = (copy.ToArray(), file.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8");
        }

        return assets;
    }

    private static IResult Asset(HttpContext context, string file)
    {
        if (!s_assets.TryGetValue(file, out (byte[] Bytes, string Type) asset))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "public, max-age=300";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Bytes(asset.Bytes, asset.Type);
    }

    // ---- Form helpers ------------------------------------------------------------------------------------------

    private static string Field(IFormCollection form, string name, int max = 200)
    {
        string value = form[name].ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length > max ? value[..max] : value;
    }
}
