using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record ShowcaseImage(string File, string Alt, string Caption, int Width = 1920, int Height = 1080);

    private static readonly IReadOnlyDictionary<string, ShowcaseImage> ShowcaseImages = new Dictionary<string, ShowcaseImage>
    {
        ["play"] = new("showcase-play.png", "Create Session showing installed maps and match settings.", "Installed maps and match settings in Create Session."),
        ["equipment"] = new("showcase-sidearm.png", "A Desert Eagle with its default finish during a Steel Squat match.", "The default Desert Eagle in a Steel Squat match."),
        ["hub"] = new("showcase-hub.png", "A spy in the OpenSCCT community hub.", "The community hub viewed as a spy."),
        ["replays"] = new("showcase-replays.png", "A recorded Aquarius bot match playing in OpenSCCT.", "Playback of a recorded bot match on Aquarius."),
        ["player"] = new("showcase-player.png", "The Armoury showing a spy with the default appearance.", "The default spy appearance in the Armoury."),
        ["setup"] = new("showcase-setup.png", "OpenSCCT Display options.", "Display settings in OpenSCCT."),
    };

    private static string ImprovementsPreview() => """
        <section class="improvements-preview" aria-labelledby="improvements-preview-title">
          <div><p class="eyebrow">FEATURES AND DEVELOPMENT</p>
            <h2 id="improvements-preview-title">OpenSCCT improvements</h2>
            <p>Equipment, practice sessions, replays, community hubs, control settings, HUD layouts and map editing.</p>
          </div>
          <a class="button secondary" href="/improvements">View features and improvements →</a>
        </section>
        """;

    private IResult Improvements(HttpContext c)
    {
        var body = new StringBuilder("""
            <div class="improvements-page">
            <header class="improvements-heading">
              <p class="eyebrow">OPENSCCT / FEATURES</p>
              <h1>OpenSCCT improvements</h1>
              <p class="lead">Features and settings for matches, equipment, practice, hubs, recordings and player accounts. Each section links to the relevant guide.</p>
              <div class="button-row"><a class="text-link" href="/changelog">Latest update notes →</a><a class="text-link" href="/wiki">Read the wiki →</a><a class="text-link" href="/download">Download OpenSCCT →</a></div>
            </header>
            <div class="showcase-strip" aria-label="Screenshots from OpenSCCT">
            """);
        AppendStripImage(body, "showcase-sidearm.png", "A Desert Eagle with its default finish during a Steel Squat match.", "Equipment", "The default Desert Eagle in a Steel Squat match.");
        AppendStripImage(body, "showcase-hub.png", "A spy in the OpenSCCT community hub.", "Community hub", "The community hub viewed as a spy.");
        AppendStripImage(body, "showcase-armoury.png", "The Armoury showing a Desert Eagle with its default finish.", "Armoury", "The default Desert Eagle finish in the Armoury.");
        body.Append("""
            </div>
            <p class="showcase-capture-note">Screenshots captured in OpenSCCT. Select an image to view it at full size. Menus and options may look different in newer builds.</p>
            <section class="section" aria-labelledby="feature-highlights-title">
              <div class="section-heading"><h2 id="feature-highlights-title">Controls, HUD and map browser</h2></div>
              <div class="grid-three">
                <article class="feature-card"><h3>Key bindings</h3><p>Search actions and assigned inputs, edit primary and alternate bindings, and keep separate spy and mercenary layouts.</p><a class="text-link" href="#feature-input">View control settings →</a></article>
                <article class="feature-card"><h3>HUD editor</h3><p>Move and resize HUD elements, preview each role's layout, and copy and paste layouts.</p><a class="text-link" href="#feature-hud">View HUD settings →</a></article>
                <article class="feature-card"><h3>Map browser</h3><p>Browse Stock, Enhanced, Community and 3v3 map groups in a picture grid, list or thumbnail list.</p><a class="text-link" href="#feature-maps">View map browser details →</a></article>
              </div>
            </section>
            <nav class="showcase-navigation" aria-label="Feature sections">
            """);
        int sectionIndex = 0;
        foreach (GameAdditions.FeatureSection section in GameAdditions.Sections)
            body.Append("<a href=\"#").Append(H(section.Id)).Append("\"><span>").Append((++sectionIndex).ToString("D2", CultureInfo.InvariantCulture))
                .Append("</span>").Append(H(section.Title)).Append("</a>");
        body.Append("""
            </nav>
            <div class="showcase-search" hidden>
              <label for="feature-search">Find a feature</label>
              <input id="feature-search" type="search" maxlength="100" placeholder="Try bindings, HUD, cloak, editor or maps" aria-controls="feature-sections" aria-describedby="feature-search-status">
              <p id="feature-search-status" role="status"></p>
              <button type="button" class="button secondary compact" data-clear-features>Clear search</button>
            </div>
            <div id="feature-sections">
            """);
        sectionIndex = 0;
        foreach (GameAdditions.FeatureSection section in GameAdditions.Sections)
        {
            ShowcaseImage picture = ShowcaseImages[section.Id];
            body.Append("<section class=\"feature-chapter\" id=\"").Append(H(section.Id)).Append("\" aria-labelledby=\"").Append(H(section.Id)).Append("-title\" data-feature-section>")
                .Append("<header class=\"chapter-heading\"><span class=\"chapter-number\" aria-hidden=\"true\">").Append((++sectionIndex).ToString("D2", CultureInfo.InvariantCulture))
                .Append("</span><div><h2 id=\"").Append(H(section.Id)).Append("-title\">").Append(H(section.Title)).Append("</h2><p>").Append(H(section.Description)).Append("</p></div></header>")
                .Append("<div class=\"chapter-body\"><figure class=\"chapter-image\"><a class=\"showcase-zoom\" data-showcase-image href=\"").Append(H(AssetUrl("/web/assets/" + picture.File)))
                .Append("\" data-caption=\"").Append(H(picture.Caption)).Append("\" aria-label=\"").Append(H("View screenshot: " + section.Title)).Append("\">")
                .Append("<img src=\"").Append(H(AssetUrl("/web/assets/" + picture.File))).Append("\" alt=\"").Append(H(picture.Alt)).Append("\" width=\"").Append(picture.Width).Append("\" height=\"").Append(picture.Height).Append("\" loading=\"lazy\" decoding=\"async\">")
                .Append("<span class=\"showcase-expand\" aria-hidden=\"true\">View screenshot ↗</span></a><figcaption>").Append(H(picture.Caption)).Append("</figcaption></figure><div class=\"chapter-features\">");
            foreach (GameAdditions.Addition item in section.Items)
            {
                body.Append("<article class=\"showcase-feature\" id=\"feature-").Append(H(item.Id)).Append("\" data-feature>")
                    .Append("<h3>").Append(H(item.Title)).Append("</h3><p>").Append(H(item.Description)).Append("</p>");
                if (item.Details.Length > 0)
                {
                    body.Append("<ul>");
                    foreach (string detail in item.Details) body.Append("<li>").Append(H(detail)).Append("</li>");
                    body.Append("</ul>");
                }
                body.Append(Link(item.Url, item.LinkLabel + (item.Url.StartsWith('/') ? " →" : " ↗"), "feature-reference")).Append("</article>");
            }
            body.Append("</div></div></section>");
        }
        body.Append("""
            </div>
            <p class="feature-no-results" hidden>No features match that search. Try a shorter name or clear the search.</p>
            <aside class="showcase-updates"><div><h2>Guides and update notes</h2><p>The wiki covers gameplay, hosting, controls and map editing. Update notes list changes and fixes by version.</p></div><div class="button-row"><a class="button secondary" href="/wiki">Read the wiki →</a><a class="text-link" href="/changelog">Update notes →</a></div></aside>
            <dialog class="showcase-dialog" aria-labelledby="showcase-caption">
              <div class="showcase-dialog-toolbar"><button type="button" class="button secondary" data-close-showcase>Close screenshot ×</button></div>
              <img alt=""><p id="showcase-caption"></p>
            </dialog>
            </div>
            """);
        return Show(c, "OpenSCCT improvements", "improvements", body.ToString());
    }

    private static void AppendStripImage(StringBuilder body, string file, string alt, string label, string caption) =>
        body.Append("<a data-showcase-image href=\"").Append(H(AssetUrl("/web/assets/" + file))).Append("\" data-caption=\"").Append(H(caption))
            .Append("\" aria-label=\"").Append(H("View screenshot: " + label)).Append("\"><img src=\"").Append(H(AssetUrl("/web/assets/" + file)))
            .Append("\" alt=\"").Append(H(alt)).Append("\" width=\"1920\" height=\"1080\" decoding=\"async\"><span>").Append(H(label)).Append(" ↗</span></a>");
}
