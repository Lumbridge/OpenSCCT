using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record BrowserProfileUpdate(long AccountId, ProfileData Profile, DateTimeOffset? ExpectedUpdated);

    private void MapBrowserGame(WebApplication app)
    {
        MapBrowserMultiplayer(app);
        string contentRoot = browserContentRoot ?? Environment.GetEnvironmentVariable("SVM_BROWSER_CONTENT_ROOT") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "browser-content"));
        contentRoot = Path.GetFullPath(contentRoot);
        app.MapMethods("/play/content/{file}", ["GET", "HEAD"], (HttpContext context, string file) =>
        {
            BrowserHeaders(context);
            bool index = file == "index.json";
            if (!index && !(file.Length == 68 && file.EndsWith(".zip", StringComparison.Ordinal) && file[..64].All(Uri.IsHexDigit)))
                return Results.NotFound();
            string path = Path.Combine(contentRoot, file);
            if (!File.Exists(path)) return Results.NotFound();
            if (!index) context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return Results.File(path, index ? "application/json" : "application/zip", enableRangeProcessing: !index);
        });
        string root = browserGameRoot ?? Environment.GetEnvironmentVariable("SVM_BROWSER_GAME_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "browser");
        if (File.Exists(Path.Combine(root, "index.html")))
        {
            root = Path.GetFullPath(root);
            var contentTypes = new FileExtensionContentTypeProvider();
            contentTypes.Mappings[".pck"] = "application/octet-stream";
            contentTypes.Mappings[".wasm"] = "application/wasm";
            var files = new PhysicalFileProvider(root);
            app.Use(async (context, next) =>
            {
                var original = context.Request.Path;
                if (original.StartsWithSegments("/play", out var asset) &&
                    contentTypes.TryGetContentType(asset.Value ?? "", out string? mime) &&
                    files.GetFileInfo((asset.Value ?? "") + ".gz").Exists)
                {
                    context.Response.Headers.Vary = "Accept-Encoding";
                    bool gzip = context.Request.GetTypedHeaders().AcceptEncoding?.Any(value =>
                        value.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase) && value.Quality.GetValueOrDefault(1) > 0) == true;
                    if (gzip)
                    {
                        context.Request.Path = new PathString(original.Value + ".gz");
                        context.Items["BrowserContentType"] = mime;
                        context.Response.Headers.ContentEncoding = "gzip";
                    }
                }
                try { await next(context); }
                finally { context.Request.Path = original; }
            });
            Get(app, "/play", context =>
            {
                if (!context.Request.Path.Value!.EndsWith('/')) return Results.Redirect("/play/");
                BrowserHeaders(context);
                return Results.File(Path.Combine(root, "index.html"), "text/html");
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/play",
                FileProvider = files,
                ContentTypeProvider = contentTypes,
                OnPrepareResponse = context =>
                {
                    BrowserHeaders(context.Context);
                    if (context.Context.Items["BrowserContentType"] is string mime) context.Context.Response.ContentType = mime;
                },
            });
        }
        app.MapGet("/play/profile", (HttpContext context) =>
        {
            WebHtml.Secure(context);
            return Current(context) is { } viewer
                ? Results.Json(new { signedIn = true, accountId = viewer.Account.Id, name = viewer.Account.Name, csrf = viewer.Session.Csrf,
                    profile = _store.BrowserProfile(viewer.Account.Id, SettingsOnly(context)) }, MasterApi.Json)
                : Results.Json(new { signedIn = false }, MasterApi.Json);
        });
        app.MapPost("/play/profile", (Func<HttpContext, Task<IResult>>)SaveBrowserProfile);
    }

    // Cookie authentication never gives JavaScript a desktop bearer token. Even reads require the CSRF secret
    // and the expected account, so an old tab cannot act as an account signed in later in another tab.
    internal AccountRow? AuthenticateGameApi(HttpContext context)
    {
        WebHtml.Secure(context);
        if (context.Request.Path == MasterApi.ProfilePath && !HttpMethods.IsGet(context.Request.Method)) return null;
        if (context.Request.Headers.Origin is { Count: > 0 } origin && origin != _options.PublicBaseUrl) return null;
        if (Current(context) is not { } viewer ||
            context.Request.Headers["X-OpenSCCT-Account"] != viewer.Account.Id.ToString(CultureInfo.InvariantCulture)) return null;
        string sent = context.Request.Headers["X-CSRF-Token"].ToString();
        return sent.Length == viewer.Session.Csrf.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(viewer.Session.Csrf)) ? viewer.Account : null;
    }

    internal IResult SignOutGameApi(HttpContext context)
    {
        if (AuthenticateGameApi(context) is null) return Results.Unauthorized();
        if (context.Request.Cookies[SessionCookie] is { } token) _store.EndPlayerWebSession(token);
        context.Response.Cookies.Delete(SessionCookie, Cookie(context, "/", SameSiteMode.Strict, null));
        return Results.Ok();
    }

    private static bool SettingsOnly(HttpContext context) => context.Request.Query["settingsOnly"] == "true";

    private static void BrowserHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; " +
            "img-src 'self' data: blob:; media-src 'self' blob:; worker-src 'self' blob:; connect-src 'self'; frame-ancestors 'self'; form-action 'self'; base-uri 'self'; object-src 'none'";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
    }

    private async Task<IResult> SaveBrowserProfile(HttpContext context)
    {
        WebHtml.Secure(context);
        if (Current(context) is not { } viewer) return Results.Unauthorized();
        string sent = context.Request.Headers["X-CSRF-Token"].ToString();
        if (sent.Length != viewer.Session.Csrf.Length || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(viewer.Session.Csrf)))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        // ProfileData.Data is JSON inside a JSON string, so every byte may expand to a six-byte escape.
        const long maxRequest = MasterApi.MaxProfileBytes * 6L + 8192;
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength > maxRequest)
            return Results.BadRequest();
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = maxRequest;
        BrowserProfileUpdate? update;
        try { update = await context.Request.ReadFromJsonAsync<BrowserProfileUpdate>(MasterApi.Json, context.RequestAborted); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(); }
        if (update?.Profile?.Data is not { } data || Encoding.UTF8.GetByteCount(data) > MasterApi.MaxProfileBytes)
            return Results.BadRequest();
        if (update.AccountId != viewer.Account.Id) return Results.Conflict(new { error = "The signed-in account changed. Reload before syncing." });
        return _store.TryPutBrowserProfile(viewer.Account.Id, update.Profile, update.ExpectedUpdated, out ProfileData? saved, SettingsOnly(context))
            ? Results.Json(saved, MasterApi.Json)
            : Results.Conflict(new { error = "Your account settings changed elsewhere. Reload before syncing again." });
    }
}
