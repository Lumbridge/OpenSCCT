using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace OpenScct.Master.Admin;

/// <summary>
/// The admin portal's HTML: the page frame (nav, notice), tables, forms with their CSRF field, and small charts as
/// inline SVG. Every value from data goes through <see cref="H"/>; pages carry no inline script or style (CSP).
/// </summary>
internal static class AdminHtml
{
    public const string CsrfField = "csrf";

    private static readonly (string Path, string Label)[] s_nav =
    [
        ("/admin", "Dashboard"), ("/admin/servers", "Servers"), ("/admin/players", "Players"), ("/admin/bans", "Bans"),
        ("/admin/matches", "Matches"), ("/admin/commands", "Commands"), ("/admin/history", "History"), ("/admin/settings", "Settings"), ("/admin/audit", "Audit"),
    ];

    /// <summary>HTML-encodes text (null as empty).</summary>
    public static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>A URL query value.</summary>
    public static string U(string? text) => Uri.EscapeDataString(text ?? string.Empty);

    public static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Time(long unix) => AccountStore.Format(unix);

    /// <summary>"3 min ago" style age of a Unix time.</summary>
    public static string Ago(long unix, long now)
    {
        long s = Math.Max(0, now - unix);
        return s < 90 ? $"{s} s ago" : s < 5400 ? $"{s / 60} min ago" : s < 172800 ? $"{s / 3600} h ago" : $"{s / 86400} days ago";
    }

    public static string Duration(double seconds)
    {
        long s = (long)Math.Max(0, seconds);
        return s < 3600 ? $"{s / 60}m {s % 60}s" : s < 172800 ? $"{s / 3600}h {s % 3600 / 60}m" : $"{s / 86400}d {s % 86400 / 3600}h";
    }

    /// <summary>A whole page: the nav (with <paramref name="active"/> marked), a notice and the body.</summary>
    public static IResult Page(HttpContext context, string title, string active, string? admin, string body, string? notice = null,
        bool noticeBad = false, int status = 200, bool refresh = false)
    {
        Secure(context);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(H(title)).Append(" - OpenSCCT administration</title>")
            .Append($"<link rel=\"stylesheet\" href=\"{H(Web.WebHtml.AssetUrl("/web/assets/site.css"))}\"><link rel=\"stylesheet\" href=\"{H(Web.WebHtml.AssetUrl("/admin/assets/admin.css"))}\">")
            .Append($"<script src=\"{H(Web.WebHtml.AssetUrl("/admin/assets/admin.js"))}\" defer></script>")
            .Append("</head><body class=\"admin\"").Append(refresh ? " data-refresh=\"10\"" : string.Empty).Append('>')
            .Append("<a class=\"skip-link\" href=\"#main\">Skip to content</a>");
        if (admin is not null)
        {
            sb.Append("<aside class=\"admin-sidebar\"><a class=\"admin-brand\" href=\"/admin\"><span class=\"brand-mark\" aria-hidden=\"true\">O</span><span class=\"brand-copy\">")
                .Append(Web.WebHtml.Wordmark).Append("<small>CONTROL ROOM</small></span></a>")
                .Append("<details class=\"admin-navigation\" open><summary>Administration menu</summary><nav aria-label=\"Administration\">");
            foreach ((string path, string label) in s_nav)
            {
                sb.Append("<a href=\"").Append(path).Append('"').Append(path == active ? " class=\"on\" aria-current=\"page\"" : string.Empty).Append('>')
                    .Append("<span class=\"nav-mark\" aria-hidden=\"true\"></span>").Append(label).Append("</a>");
            }

            sb.Append("</nav></details><div class=\"sidebar-bottom\"><a href=\"/\">↗ Player hub</a><span class=\"admin-identity\">").Append(H(admin))
                .Append("<small>Administrator</small></span><form method=\"post\" action=\"/admin/logout\">").Append(Csrf(context))
                .Append("<button class=\"link\">Sign out</button></form></div></aside>")
                .Append("<div class=\"admin-workspace\"><header class=\"admin-topbar\"><span class=\"workspace-label\">OPERATIONS / ")
                .Append(H(title)).Append("</span><a class=\"button compact public-site-link\" href=\"/\">Back to public site</a>")
                .Append("<form action=\"/admin/search\" method=\"get\" class=\"global-search\"><label class=\"sr-only\" for=\"global-query\">Search players, servers and matches</label>")
                .Append("<input id=\"global-query\" name=\"q\" type=\"search\" placeholder=\"Search players, servers, matches…\" maxlength=\"100\"><button>Search</button></form></header>");
        }
        else
        {
            sb.Append("<div class=\"admin-signin\"><header class=\"admin-signin-header\"><a class=\"admin-brand\" href=\"/\"><span class=\"brand-mark\" aria-hidden=\"true\">O</span><span class=\"brand-copy\">")
                .Append(Web.WebHtml.Wordmark).Append("</span></a><a class=\"button compact public-site-link\" href=\"/\">Back to public site</a></header>");
        }

        sb.Append("<main id=\"main\"><div class=\"page-heading\"><div><p class=\"eyebrow\">ADMINISTRATION</p><h1>").Append(H(title)).Append("</h1></div>");
        if (refresh)
        {
            sb.Append("<div class=\"live-status\" role=\"status\"><span class=\"status-dot\"></span><span id=\"refresh-status\">Live · updating every 10s</span><button type=\"button\" id=\"refresh-now\" class=\"link\">Refresh</button></div>");
        }

        sb.Append("</div>");
        if (!string.IsNullOrEmpty(notice))
        {
            sb.Append("<p role=\"status\" class=\"notice").Append(noticeBad ? " bad" : string.Empty).Append("\">").Append(H(notice)).Append("</p>");
        }

        sb.Append(body).Append("<footer class=\"admin-footer\">OpenSCCT operations <span>Changes are recorded in the audit log.</span></footer></main></div></body></html>");
        return Results.Content(sb.ToString(), "text/html; charset=utf-8", Encoding.UTF8, status);
    }

    /// <summary>The portal's response headers: no caching, its own content policy, no framing, no referrer.</summary>
    public static void Secure(HttpContext context)
    {
        IHeaderDictionary h = context.Response.Headers;
        h.CacheControl = "no-store";
        h["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
        h["X-Frame-Options"] = "DENY";
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
    }

    /// <summary>The hidden CSRF field of the signed-in admin's session (set in <c>context.Items</c> by the portal).</summary>
    public static string Csrf(HttpContext context) =>
        $"<input type=\"hidden\" name=\"{CsrfField}\" value=\"{H(context.Items[CsrfField] as string)}\">";

    /// <summary>A POST form; <paramref name="confirm"/> makes the browser ask first (admin.js).</summary>
    public static string Form(HttpContext context, string action, string inner, string? confirm = null, string cls = "") =>
        $"<form method=\"post\" action=\"{H(action)}\" class=\"{cls}\"{(confirm is null ? string.Empty : $" data-confirm=\"{H(confirm)}\"")}>" +
        Csrf(context) + inner + "</form>";

    public static string Button(string label, string name = "", string value = "", string cls = "") =>
        $"<button{(name.Length > 0 ? $" name=\"{H(name)}\" value=\"{H(value)}\"" : string.Empty)}{(cls.Length > 0 ? $" class=\"{cls}\"" : string.Empty)}>{H(label)}</button>";

    public static string Input(string name, string placeholder = "", string value = "", string type = "text", bool required = false,
        int maxLength = 200) =>
        $"<input type=\"{type}\" name=\"{H(name)}\" aria-label=\"{H(placeholder.Length > 0 ? placeholder : name)}\" placeholder=\"{H(placeholder)}\" value=\"{H(value)}\" maxlength=\"{maxLength}\"{(type == "number" ? " step=\"any\"" : string.Empty)}{(required ? " required" : string.Empty)}>";

    public static string Select(string name, IEnumerable<(string Value, string Label)> options, string? selected = null) =>
        $"<select name=\"{H(name)}\" aria-label=\"{H(name)}\">" + string.Concat(options.Select(o =>
            $"<option value=\"{H(o.Value)}\"{(o.Value == selected ? " selected" : string.Empty)}>{H(o.Label)}</option>")) + "</select>";

    /// <summary>A table: header cells (plain text) and rows of already-built cell HTML.</summary>
    public static string Table(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows, string empty = "Nothing yet.")
    {
        var sb = new StringBuilder("<div class=\"table\"><table><thead><tr>");
        foreach (string h in headers)
        {
            sb.Append("<th scope=\"col\">").Append(H(h)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        int count = 0;
        foreach (IEnumerable<string> row in rows)
        {
            count++;
            sb.Append("<tr>");
            foreach (string cell in row)
            {
                sb.Append("<td>").Append(cell).Append("</td>");
            }

            sb.Append("</tr>");
        }

        if (count == 0)
        {
            sb.Append("<tr><td class=\"empty\" colspan=\"99\">").Append(H(empty)).Append("</td></tr>");
        }

        return sb.Append("</tbody></table></div>").ToString();
    }

    public static string Link(string href, string text) => $"<a href=\"{H(href)}\">{H(text)}</a>";

    public static string Badge(string text, string cls = "") => $"<span class=\"badge {cls}\">{H(text)}</span>";

    /// <summary>A row of figure tiles (label, value, optional note).</summary>
    public static string Tiles(params (string Label, string Value, string? Note)[] tiles) =>
        "<div class=\"tiles\" data-refresh-region=\"metrics\">" + string.Concat(tiles.Select(t =>
            $"<div class=\"tile\"><div class=\"label\">{H(t.Label)}</div><div class=\"value\">{H(t.Value)}</div>" +
            (t.Note is null ? string.Empty : $"<div class=\"note\">{H(t.Note)}</div>") + "</div>")) + "</div>";

    public static string Section(string title, string inner) =>
        $"<section class=\"panel-section\" id=\"{U(title.ToLowerInvariant().Replace(' ', '-'))}\" data-refresh-region=\"{H(title)}\"><div class=\"section-heading\"><h2>{H(title)}</h2></div>{inner}</section>";

    /// <summary>Previous / next page links for a list.</summary>
    public static string Pager(string basePath, int page, bool more)
    {
        string sep = basePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string prev = page > 0 ? $"<a href=\"{H(basePath + sep + "page=" + (page - 1).ToString(CultureInfo.InvariantCulture))}\">&larr; Newer</a>" : string.Empty;
        string next = more ? $"<a href=\"{H(basePath + sep + "page=" + (page + 1).ToString(CultureInfo.InvariantCulture))}\">Older &rarr;</a>" : string.Empty;
        return prev.Length + next.Length == 0 ? string.Empty : $"<p class=\"pager\">{prev} {next}</p>";
    }

    /// <summary>
    /// A line chart as inline SVG: <paramref name="points"/> are (Unix time, value) for each series (at most three,
    /// coloured by class), drawn over [<paramref name="from"/>, <paramref name="to"/>].
    /// </summary>
    public static string Chart(IReadOnlyList<(string Name, IReadOnlyList<(long Time, int Value)> Points)> series, long from, long to,
        Func<long, string> axisLabel)
    {
        const int w = 900, h = 220, left = 36, bottom = 22, top = 10;
        int max = Math.Max(1, series.SelectMany(s => s.Points).Select(p => p.Value).DefaultIfEmpty(0).Max());
        max = max <= 4 ? 4 : (int)Math.Ceiling(max / 4.0) * 4;
        double span = Math.Max(1, to - from);
        double X(long t) => left + ((t - from) / span * (w - left - 6));
        double Y(int v) => top + ((h - top - bottom) * (1 - (v / (double)max)));
        var sb = new StringBuilder($"<svg class=\"chart\" viewBox=\"0 0 {w} {h}\" role=\"img\" preserveAspectRatio=\"none\">");
        for (int i = 0; i <= 4; i++)
        {
            int v = max * i / 4;
            double y = Y(v);
            sb.Append(CultureInfo.InvariantCulture, $"<line class=\"grid\" x1=\"{left}\" x2=\"{w - 6}\" y1=\"{y:F1}\" y2=\"{y:F1}\"/>")
                .Append(CultureInfo.InvariantCulture, $"<text class=\"axis\" x=\"{left - 6}\" y=\"{y + 4:F1}\" text-anchor=\"end\">{v}</text>");
        }

        for (int i = 0; i <= 4; i++)
        {
            long t = from + (long)(span * i / 4);
            sb.Append(CultureInfo.InvariantCulture,
                $"<text class=\"axis\" x=\"{X(t):F1}\" y=\"{h - 6}\" text-anchor=\"{(i == 0 ? "start" : i == 4 ? "end" : "middle")}\">{H(axisLabel(t))}</text>");
        }

        int index = 0;
        foreach ((string name, IReadOnlyList<(long Time, int Value)> points) in series)
        {
            if (points.Count > 0)
            {
                string d = string.Join(' ', points.Select((p, i) =>
                    string.Create(CultureInfo.InvariantCulture, $"{(i == 0 ? 'M' : 'L')}{X(p.Time):F1},{Y(p.Value):F1}")));
                sb.Append($"<path class=\"s{index}\" d=\"{d}\"><title>{H(name)}</title></path>");
            }

            index++;
        }

        sb.Append("</svg><p class=\"legend\">");
        index = 0;
        foreach ((string name, _) in series)
        {
            sb.Append($"<span class=\"k{index++}\">{H(name)}</span>");
        }

        return sb.Append("</p>").ToString();
    }
}
