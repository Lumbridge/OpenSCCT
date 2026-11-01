using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace OpenScct.Master.Web;

/// <summary>Shared presentation primitives and static resources. This class performs no authorization.</summary>
internal static class WebHtml
{
    public const string Wordmark = "<span class=\"wordmark\"><span class=\"wordmark-open\">Open</span><span class=\"wordmark-scct\">SCCT</span></span>";

    public static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public static string U(string value) => Uri.EscapeDataString(value);
    /// <summary>Content-addressed URLs refresh cached resources even when a preview rebuild uses the same commit.</summary>
    public static string AssetUrl(string path) => AssetVersions.TryGetValue(path, out string? version) ? path + "?v=" + version : path;
    public static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    public static string Link(string url, string text, string? css = null) =>
        $"<a href=\"{H(url)}\"{(css is null ? "" : " class=\"" + H(css) + "\"")}>{H(text)}</a>";
    public static string Badge(string text, string css = "") => $"<span class=\"badge {H(css)}\">{H(text)}</span>";
    public static string Time(long time) => $"<time datetime=\"{DateTimeOffset.FromUnixTimeSeconds(time):O}\">{H(AccountStore.Format(time))}</time>";
    public static string Duration(int seconds) => seconds <= 0 ? "—" : seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m" : $"{seconds / 60}m {seconds % 60}s";

    public static void Secure(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "frame-ancestors 'none'; form-action 'self'; base-uri 'none'; object-src 'none'";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
    }

    public static IResult Page(HttpContext context, string title, string active, string body, string? name = null, string? csrf = null,
        string? notice = null, bool bad = false, int status = 200, bool isAdmin = false)
    {
        Secure(context);
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<meta name=\"color-scheme\" content=\"dark\"><title>").Append(H(title)).Append(" · OpenSCCT</title>")
            .Append($"<link rel=\"icon\" href=\"{H(AssetUrl("/web/assets/mark.svg"))}\" type=\"image/svg+xml\"><link rel=\"stylesheet\" href=\"{H(AssetUrl("/web/assets/site.css"))}\">")
            .Append($"<script src=\"{H(AssetUrl("/web/assets/site.js"))}\" defer></script>");
        if (active == "account") html.Append("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.Append("</head><body class=\"player-site\"><a class=\"skip-link\" href=\"#main\">Skip to content</a>")
            .Append($"<header class=\"site-header\"><div class=\"header-inner\"><a class=\"site-brand\" href=\"/\"><img src=\"{H(AssetUrl("/web/assets/mark.svg"))}\" alt=\"\" width=\"36\" height=\"36\"><span class=\"brand-copy\">{Wordmark}<small>SPIES VS MERCENARIES</small></span></a>")
            .Append("<button class=\"menu-toggle button secondary\" type=\"button\" aria-expanded=\"false\" aria-controls=\"primary-navigation\">Menu</button><nav id=\"primary-navigation\" class=\"primary-nav\" aria-label=\"Main navigation\">");
        foreach ((string url, string label, string key) in new[] { ("/", "Overview", "home"), ("/servers", "Servers", "servers"),
            ("/players", "Players", "players"), ("/leaderboards", "Leaderboards", "leaderboards"), ("/matches", "Matches", "matches"),
            ("/changelog", "Changelog", "changelog") })
            html.Append("<a href=\"").Append(url).Append('"').Append(active == key ? " aria-current=\"page\"" : "").Append('>').Append(label).Append("</a>");
        html.Append("</nav><div class=\"header-actions\">").Append(Link("/download", "Get the game", "button compact header-download"));
        html.Append(name is not null ? Link("/account", name, "account-link") : Link("/account", "Sign in", "account-link"));
        if (isAdmin && name is not null) html.Append(Link("/admin", "Admin", "button secondary compact header-admin"));
        if (name is not null && !string.IsNullOrEmpty(csrf))
            html.Append("<form class=\"header-signout\" method=\"post\" action=\"/account/logout\">").Append(Csrf(csrf))
                .Append("<button class=\"button secondary compact\" type=\"submit\" aria-label=\"Sign out of this browser\">Sign out</button></form>");
        html.Append("</div></div></header><main id=\"main\" class=\"site-main\">");
        if (!string.IsNullOrEmpty(notice)) html.Append("<div role=\"status\" class=\"notice ").Append(bad ? "bad" : "ok").Append("\">").Append(H(notice)).Append("</div>");
        html.Append(body).Append("</main><footer class=\"site-footer\"><div><span class=\"footer-brand\">").Append(Wordmark).Append("</span><p>Spies vs. Mercenaries on modern PCs.</p></div>")
            .Append("<nav aria-label=\"Footer\"><a href=\"/download\">Getting started</a><a href=\"/changelog\">Changelog</a><a href=\"https://github.com/Lumbridge/OpenSCCT\">Project</a>")
            .Append("<a href=\"https://github.com/Lumbridge/OpenSCCT/blob/main/docs/legal/PRIVACY.md\">Privacy</a><a href=\"https://github.com/Lumbridge/OpenSCCT/blob/main/docs/legal/TERMS.md\">Terms</a></nav>")
            .Append("<small>Community project. Not affiliated with Ubisoft.</small></footer></body></html>");
        return Results.Content(html.ToString(), "text/html; charset=utf-8", Encoding.UTF8, status);
    }

    public static string PageHeader(string eyebrow, string title, string description, string action = "") =>
        $"<div class=\"page-heading\"><div><p class=\"eyebrow\">{H(eyebrow)}</p><h1>{H(title)}</h1><p class=\"lead\">{H(description)}</p></div>{action}</div>";
    public static string Section(string title, string content, string extra = "") =>
        $"<section class=\"section\"><div class=\"section-heading\"><h2>{H(title)}</h2>{extra}</div>{content}</section>";
    public static string Empty(string title, string description) => $"<div class=\"empty-state\"><span class=\"empty-mark\" aria-hidden=\"true\">○</span><h3>{H(title)}</h3><p>{H(description)}</p></div>";
    public static string Stat(string label, string value, string note = "") =>
        $"<div class=\"stat-card\"><span>{H(label)}</span><strong>{H(value)}</strong><small>{H(note)}</small></div>";
    public static string Table(string[] headers, IEnumerable<string[]> rows, string empty = "Nothing to show yet.")
    {
        var html = new StringBuilder("<div class=\"table-wrap\"><table><thead><tr>");
        foreach (string heading in headers) html.Append("<th scope=\"col\">").Append(H(heading)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        int count = 0;
        foreach (string[] row in rows)
        {
            html.Append("<tr>");
            foreach (string cell in row) html.Append("<td>").Append(cell).Append("</td>");
            html.Append("</tr>");
            count++;
        }
        if (count == 0) html.Append("<tr><td class=\"table-empty\" colspan=\"").Append(headers.Length).Append("\">").Append(H(empty)).Append("</td></tr>");
        return html.Append("</tbody></table></div>").ToString();
    }
    public static string Pager(string url, int page, bool more)
    {
        if (page == 0 && !more) return string.Empty;
        string join = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return "<nav class=\"pagination\" aria-label=\"Pagination\">" + (page > 0 ? Link(url + join + "page=" + (page - 1).ToString(CultureInfo.InvariantCulture), "← Previous", "button secondary compact") : "<span></span>") +
            $"<span>Page {page + 1}</span>" + (more ? Link(url + join + "page=" + (page + 1).ToString(CultureInfo.InvariantCulture), "Next →", "button secondary compact") : "<span></span>") + "</nav>";
    }
    public static string Hidden(string name, string value) => $"<input type=\"hidden\" name=\"{H(name)}\" value=\"{H(value)}\">";
    public static string Csrf(string csrf) => Hidden("csrf", csrf);
    public static string LiveStatus => "<div class=\"live-status\" role=\"status\"><span class=\"status-dot\"></span><span data-live-status>Live · updates every 30 seconds</span></div>";

    private static readonly Dictionary<string, (byte[] Bytes, string Type)> Assets = LoadAssets();
    private static readonly Dictionary<string, string> AssetVersions = LoadAssetVersions();

    private static Dictionary<string, string> LoadAssetVersions()
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        Assembly assembly = typeof(WebHtml).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            int separator = name.IndexOf('/');
            if (separator < 0 || name[..separator] is not ("web" or "admin")) continue;
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            versions["/" + name[..separator] + "/assets/" + name[(separator + 1)..]] = Convert.ToHexStringLower(SHA256.HashData(stream))[..16];
        }
        return versions;
    }
    private static Dictionary<string, (byte[], string)> LoadAssets()
    {
        var assets = new Dictionary<string, (byte[], string)>(StringComparer.Ordinal);
        Assembly assembly = typeof(WebHtml).Assembly;
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("web/", StringComparison.Ordinal)))
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            string file = name[4..];
            string type = Path.GetExtension(file) switch { ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".svg" => "image/svg+xml", _ => "application/octet-stream" };
            assets[file] = (copy.ToArray(), type);
        }
        return assets;
    }
    public static void MapAssets(WebApplication app) => app.MapGet("/web/assets/{file}", (HttpContext c, string file) =>
    {
        if (!Assets.TryGetValue(file, out var asset)) return Results.NotFound();
        c.Response.Headers.CacheControl = "public, max-age=300";
        c.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Bytes(asset.Bytes, asset.Type);
    });
}
