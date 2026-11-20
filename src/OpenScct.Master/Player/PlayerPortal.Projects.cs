using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private const string HubSource = "https://github.com/OpenSCCT/OpenSCCT";
    private const string UnrealSource = "https://github.com/OpenSCCT/OpenSCCT-Unreal";
    private const string EditorSource = "https://github.com/OpenSCCT/OpenSCCT-Editor";
    private const string DecompGuide = HubSource + "/tree/main/decomp";
    private const string EnhancedSource = "https://github.com/Joshhhuaaa/EnhancedSCCTVersus";
    private const string ReloadedFeatures = "https://allypal.github.io/SCCT_Versus_Reloaded/features/";

    private sealed record ProjectEdition(string Id, string Platform, string Name, string Scope, string Summary, string Requirement);
    private static readonly ProjectEdition[] Editions =
    [
        new("ps2", "PlayStation 2", "PS2 original", "Retail / disc", "Run the console release from your own disc image and follow the reconstruction of its executables.", "Your PS2 ISO"),
        new("pc", "Windows", "PC / Steam", "Retail / PC", "Play your installed copy of Chaos Theory. Keep the original game and choose community additions for Versus.", "Your PC installation"),
        new("unreal", "Unreal Engine 5", "OpenSCCT Unreal", "Modern remake", "Play the modern Spies vs. Mercenaries project, with downloadable maps and community map authoring.", "Windows x64"),
    ];

    private IResult ProjectHome(HttpContext c)
    {
        Dictionary<string, string> settings = _store.Settings();
        var body = new StringBuilder("<div class=\"project-site\">");
        if (settings.GetValueOrDefault("site.announcement") is { Length: > 0 } announcement)
            body.Append("<aside class=\"announcement\"><strong>Announcement</strong><p>").Append(H(announcement)).Append("</p></aside>");
        string heroImage = AssetUrl("/web/assets/showcase-bots.webp");
        body.Append($"""
            <section class="project-hero" aria-labelledby="project-title">
              <div class="project-hero-copy">
                <p class="project-kicker">Splinter Cell</p>
                <h1 id="project-title">Chaos Theory<span>projects</span></h1>
                <p class="hero-summary">Explore the original PlayStation 2 and PC editions of Chaos Theory, and the Unreal Engine remake of Spies vs. Mercenaries.</p>
                <div class="hub-actions"><a class="hub-primary" href="/download">Download launcher</a><a class="hub-secondary" href="/progress">View progress</a></div>
              </div>
              <figure class="project-frame"><img src="{H(heroImage)}" width="1280" height="720" fetchpriority="high" alt="Two spies perform a co-op boost in night vision in the OpenSCCT reference game."><figcaption>OpenSCCT <span>Co-op boost / Night vision</span></figcaption></figure>
            </section>
            <section class="hub-section" id="editions" aria-labelledby="editions-heading">
              <header class="hub-heading"><h2 id="editions-heading">Projects</h2><a href="/progress">Development progress</a></header>
              <div class="edition-grid">
            """);
        foreach (ProjectEdition e in Editions)
            body.Append($"""
                <article class="edition-card {e.Id}">
                  <p class="edition-platform">{H(e.Platform)}</p><h3><a href="/projects/{e.Id}">{H(e.Name)}</a></h3>
                  <p>{H(e.Summary)}</p><div class="card-foot"><span>{H(e.Scope)}</span><span>{H(e.Requirement)}</span></div>
                </article>
                """);
        body.Append("""
            </div></section>
            """).Append(HomeProgress()).Append("""
            <section class="hub-section" aria-labelledby="work-heading">
              <header class="hub-heading"><h2 id="work-heading">Get involved</h2><a href="/wiki/contributing">Contributor guide</a></header>
              <div class="hub-resources">
            """).Append(Resource("/mods", "Browse mods", "Patched PS2 discs, separate modded PC installations and community Unreal maps."))
            .Append(Resource("/tools", "Find a tool", "Editors, map collections and tools for studying the original game."))
            .Append(Resource(DecompGuide, "Reconstruct a function", "Identify an executable, compile a unit and compare the result against the original bytes."))
            .Append("""
            </div></section>
            <section class="hub-section" aria-labelledby="news-heading"><header class="hub-heading"><h2 id="news-heading">Project updates</h2></header><div class="hub-resources">
            """)
            .Append(Resource(UnrealSource + "/releases", "Unreal releases", "Builds and release notes for the Windows remake."))
            .Append(Resource("/changelog", "Launcher, game and editor", "Search the release history for the reference game and editor."))
            .Append(Resource(HubSource + "/blob/main/decomp/reports/2026-11-20.md", "20 November 2026 · Baseline report", "The first PS2 and Steam executable inventories and a verified campaign code match."))
            .Append("</div></section></div>");
        return Show(c, "Chaos Theory projects", "home", body.ToString());
    }

    private static string Resource(string url, string title, string description) =>
        $"<article class=\"hub-resource\"><h3>{Link(url, title)}</h3><p>{H(description)}</p></article>";

    private static string ProjectHeading(string section, string title, string description) =>
        $"<header class=\"hub-page-head\"><p class=\"project-kicker\">{H(section)}</p><h1>{H(title)}</h1><p>{H(description)}</p></header>";

    private IResult Edition(HttpContext c)
    {
        ProjectEdition? edition = Editions.FirstOrDefault(e => e.Id == c.Request.RouteValues["edition"]?.ToString());
        if (edition is null)
            return Show(c, "Project unavailable", "projects", ProjectHeading("Projects", "Project unavailable", "Choose an edition from the project directory.") +
                Link("/", "Browse projects", "button secondary"), status: 404);
        var body = new StringBuilder($"<div class=\"project-site edition-page {edition.Id}\">");
        body.Append(Link("/#editions", "All projects", "hub-back"))
            .Append(ProjectHeading(edition.Platform, edition.Name, edition.Summary));
        string setup;
        string scope;
        string source;
        switch (edition.Id)
        {
            case "ps2":
                setup = "Select an uncompressed ISO made from your own Chaos Theory disc. Select your configured PCSX2 installation to start that disc with your existing BIOS and settings.";
                scope = "The original console game, including its campaign and multiplayer components. Emulated play uses the retail executable. Native reconstruction is tracked separately for each executable on the disc.";
                source = DecompGuide + "/ps2";
                break;
            case "pc":
                setup = "Install your copy through Steam or from your PC media. Select that installation for campaign and co-op, or launch Versus separately. On Linux, a Steam installation starts campaign and co-op through Steam. Versus and manual PC installations use Wine.";
                scope = "The original PC game covers campaign, co-op and Spies vs. Mercenaries. Enhanced and Reloaded are optional community additions for Versus.";
                source = DecompGuide + "/pc";
                break;
            default:
                setup = "Use the Windows launcher to install OpenSCCT Unreal and its shared content. Choose a map pack and start the game from the launcher.";
                scope = "The Unreal client recreates Spies vs. Mercenaries using OpenSCCT simulation, Unreal rendering and converted maps. Check the release notes for changes to the current build.";
                source = UnrealSource;
                break;
        }
        body.Append("<div class=\"edition-details\"><section><h2>Getting started</h2><p>").Append(H(setup))
            .Append("</p><div class=\"hub-actions\">").Append(Link("/download", "Download launcher", "hub-primary"))
            .Append(Link("/mods#" + edition.Id, "Mods for this edition", "hub-secondary")).Append("</div></section>")
            .Append("<section><h2>Project scope</h2><p>").Append(H(scope)).Append("</p><div class=\"hub-link-row\">")
            .Append(Link(source, "Project source")).Append(Link("/progress#" + edition.Id, "Progress report")).Append("</div></section></div>");
        if (edition.Id == "ps2")
            body.Append("<p class=\"hub-note\">").Append(Link(HubSource + "/blob/main/docs/editions/ps2.md", "Disc setup and supported serials")).Append("</p>");
        return Show(c, edition.Name, "projects", body.Append("</div>").ToString());
    }

    private IResult Mods(HttpContext c)
    {
        string body = ProjectHeading("Community", "Mods", "Choose a mod for the edition you play. Each edition uses its own format and setup.")
            + "<div class=\"hub-list\">"
            + GuideSection("ps2", "PS2 original", "Patched disc images",
                "Apply the mod author’s patch to a copy of your own disc image. Select the patched ISO in the launcher. Its disc serial must match your original. Switch back to the original image to play stock.",
                Link("/projects/ps2", "PS2 setup"))
            + GuideSection("pc", "PC / Steam", "Modded PC installations",
                "Choose a separate modded installation for campaign and co-op, Versus or the full PC game. Switch back to your base installation in Mods. For Versus, Enhanced updates maps and game balance, and Reloaded adds widescreen, input, controller and online improvements.",
                Link(EnhancedSource, "Enhanced SCCT Versus") + Link(ReloadedFeatures, "Reloaded features"))
            + GuideSection("unreal", "OpenSCCT Unreal", "Custom map collections",
                "Build maps in OpenSCCT Editor. In the Windows launcher, enable the folder containing your compiled maps before starting Unreal. Give each map a unique ID.",
                Link("/wiki/editor", "OpenSCCT Editor guide") + Link(HubSource + "/blob/main/docs/UNREAL-MAP-MODS.md", "Map mod guide"))
            + "</div>";
        return Show(c, "Mods", "mods", "<div class=\"project-site\">" + body + "</div>");
    }

    private static string GuideSection(string id, string eyebrow, string title, string description, string links) =>
        $"<section id=\"{H(id)}\" class=\"hub-entry {H(id)}\"><p class=\"edition-platform\">{H(eyebrow)}</p><div><h2>{H(title)}</h2><p>{H(description)}</p><div class=\"hub-link-row\">{links}</div></div></section>";

    private IResult Tools(HttpContext c) => Show(c, "Tools", "tools", "<div class=\"project-site\">" +
        ProjectHeading("Development", "Tools & resources", "Set up an edition, build a map or investigate the original executables.") +
        "<div class=\"hub-list\">" +
        GuideSection("maps", "Map authoring", "OpenSCCT Editor", "Edit geometry, lighting and objectives. Export a compiled map and playtest it with the target client.",
            Link("/wiki/editor", "Editor manual") + Link(EditorSource, "Editor source")) +
        GuideSection("retail-maps", "PC / Steam", "SCCT Map Manager", "Download and manage original, Enhanced and community maps for the retail PC Versus game.",
            Link("https://github.com/OpenSCCT/SCCT-Map-Manager", "Map Manager") + Link("https://github.com/OpenSCCT/SCCT-Maps", "Map collection")) +
        GuideSection("ps2-tools", "PlayStation 2", "PCSX2", "Boot a verified local disc in PCSX2. Its debugger helps contributors compare observed behaviour with the reconstruction.",
            Link("https://pcsx2.net/", "PCSX2") + Link(DecompGuide + "/ps2", "Disc research")) +
        GuideSection("decomp-tools", "Reconstruction", "Executable inspection and comparison", "Identify the exact input, inspect code boundaries, rebuild a unit and compare its bytes with the retail executable.",
            Link(DecompGuide, "Decompilation guide") + Link(DecompGuide + "/tools", "Project tools")) + "</div></div>");

    private static readonly Lazy<JsonDocument> ProgressReport = new(() =>
    {
        using Stream stream = typeof(PlayerPortal).Assembly.GetManifestResourceStream("project-progress.json")
            ?? throw new InvalidDataException("The project progress report is missing.");
        return JsonDocument.Parse(stream);
    });

    private static string HomeProgress()
    {
        var body = new StringBuilder("""
            <section class="hub-section" aria-labelledby="home-progress-heading">
              <header class="hub-heading"><h2 id="home-progress-heading">Progress</h2><a href="/progress">Full report and sources</a></header>
              <div class="home-progress">
            """);
        foreach (JsonElement edition in ProgressReport.Value.RootElement.GetProperty("editions").EnumerateArray())
        {
            string? id = edition.GetProperty("id").GetString();
            ProjectEdition project = Editions.First(e => e.Id == id);
            JsonElement targets = edition.GetProperty("targets");
            body.Append("<article><h3>").Append(Link("/progress#" + id, project.Name)).Append("</h3>");
            if (targets.GetArrayLength() == 0)
                body.Append("<p>Awaiting a verified baseline.</p>");
            else if (targets.EnumerateArray().All(t => t.GetProperty("total_bytes").ValueKind == JsonValueKind.Null))
                body.Append("<p>").Append(N(targets.GetArrayLength())).Append(" reference executables</p><p class=\"progress-detail\">Code boundaries under study</p>");
            else
            {
                foreach (JsonElement target in targets.EnumerateArray())
                    body.Append("<div class=\"home-measure\"><p>").Append(H(target.GetProperty("label").GetString())).Append("</p>")
                        .Append("<span>").Append(H(Measure(target).Bytes)).Append("</span></div>");
            }
            body.Append("</article>");
        }
        return body.Append("<article><h3>").Append(Link("/progress#unreal", "OpenSCCT Unreal"))
            .Append("</h3><p>Playable Windows builds</p><p class=\"progress-detail\">")
            .Append(Link(UnrealSource + "/releases/latest", "Latest release notes")).Append("</p></article></div></section>").ToString();
    }

    private static (string Bytes, string Percent) Measure(JsonElement target)
    {
        JsonElement bytes = target.GetProperty("total_bytes");
        if (bytes.ValueKind != JsonValueKind.Number || bytes.GetInt64() <= 0)
            return ("Code boundaries under study", "Unmeasured");
        long total = bytes.GetInt64();
        long matched = target.GetProperty("matched_bytes").GetInt64();
        decimal percent = 100m * matched / total;
        string rate = percent is > 0 and < 0.01m ? "<0.01%" : percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        return (N(matched) + " / " + N(total) + " bytes", rate);
    }

    private IResult ProjectProgress(HttpContext c)
    {
        var body = new StringBuilder("<div class=\"project-site\">");
        body.Append(ProjectHeading("Development", "Progress reports", "Each original executable has its own matching report. Unreal development is tracked through releases and playtesting."))
            .Append("<div class=\"hub-link-row report-links\">").Append(Link(DecompGuide, "Reproduction guide"))
            .Append(Link(HubSource + "/blob/main/decomp/reports/progress.json", "Report source")).Append("</div>");
        foreach (JsonElement edition in ProgressReport.Value.RootElement.GetProperty("editions").EnumerateArray())
        {
            string? id = edition.GetProperty("id").GetString();
            ProjectEdition project = Editions.First(e => e.Id == id);
            body.Append("<section id=\"").Append(H(id)).Append("\" class=\"hub-section\"><header class=\"hub-heading\"><h2>")
                .Append(H(project.Name)).Append("</h2>").Append(Link("/projects/" + id, "Edition setup")).Append("</header>");
            body.Append("<p>").Append(H(edition.GetProperty("stage").GetString())).Append("</p>");
            var rows = new List<string[]>();
            foreach (JsonElement target in edition.GetProperty("targets").EnumerateArray())
            {
                var measure = Measure(target);
                string evidence = HubSource + "/blob/main/" + target.GetProperty("evidence").GetString();
                rows.Add([Link(evidence, target.GetProperty("label").GetString()!), H(target.GetProperty("scope").GetString()), H(measure.Bytes), H(measure.Percent)]);
            }
            if (rows.Count > 0) body.Append(Table(["Executable", "Scope", "Matched code", "Progress"], rows));
            body.Append("</section>");
        }
        body.Append("<div class=\"hub-list\">")
            .Append(GuideSection("unreal", "Unreal Engine 5", "OpenSCCT Unreal", "Follow packaged builds, gameplay changes and open issues for the modern Spies vs. Mercenaries client.",
                Link(UnrealSource + "/releases", "Unreal release notes") + Link(UnrealSource + "/issues", "Issue tracker")))
            .Append("</div><p class=\"hub-note\">The reference game has its own ")
            .Append(Link("/improvements", "feature overview")).Append(" and ")
            .Append(Link(HubSource + "/blob/main/docs/progress/COVERAGE.md", "simulation coverage report")).Append(".</p></div>");
        return Show(c, "Progress reports", "progress", body.ToString());
    }
}
