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
            <p>Game screenshots and details of equipment, practice tools, hubs, replays, player stats and settings.</p>
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
              <p class="lead">A visual guide to the features, options and tools in OpenSCCT.</p>
              <div class="button-row"><a class="text-link" href="/changelog">Recent changes →</a><a class="text-link" href="/download">Download and setup →</a></div>
            </header>
            <div class="showcase-strip" aria-label="Screenshots from OpenSCCT">
            """);
        AppendStripImage(body, "showcase-sidearm.png", "A Desert Eagle with its default finish during a Steel Squat match.", "Equipment", "The default Desert Eagle in a Steel Squat match.");
        AppendStripImage(body, "showcase-hub.png", "A spy in the OpenSCCT community hub.", "Community hub", "The community hub viewed as a spy.");
        AppendStripImage(body, "showcase-armoury.png", "The Armoury showing a Desert Eagle with its default finish.", "Armoury", "The default Desert Eagle finish in the Armoury.");
        body.Append("""
            </div>
            <p class="showcase-capture-note">Screenshots captured in OpenSCCT. Select an image to view it at full size.</p>
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
              <input id="feature-search" type="search" maxlength="100" placeholder="Try sidearms, bots, voice or maps" aria-controls="feature-sections" aria-describedby="feature-search-status">
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
            <aside class="showcase-updates"><div><h2>Updates and fixes</h2><p>The update notes list changes and fixes for each version, including movement, equipment, maps, sound and online play.</p></div><a class="button secondary" href="/changelog">Read update notes →</a></aside>
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
