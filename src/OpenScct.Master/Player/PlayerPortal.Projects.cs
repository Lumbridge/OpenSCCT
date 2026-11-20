using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
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

    private sealed record ProjectEdition(string Id, string Group, string Platform, string Name, string Scope, string Summary, string Requirement);
    private static readonly ProjectEdition[] Editions =
    [
        new("ps2", "Originals", "PlayStation 2", "PS2 original", "Retail / disc", "Run the console release from your own disc image and follow the reconstruction of its executables.", "Your PS2 ISO"),
        new("pc", "Originals", "Windows", "PC / Steam", "Retail / PC", "Play your installed copy of Chaos Theory. Keep the original game and choose community additions for Versus.", "Your PC installation"),
        new("godot", "Modernisations", "Godot 4", "OpenSCCT Godot", "Windows / Linux", "Play Spies vs. Mercenaries in Godot with bots, replays and community maps. Build the original Versus maps from your Steam installation.", "Your Steam installation"),
        new("unreal", "Modernisations", "Unreal Engine 5", "OpenSCCT Unreal", "Windows x64", "Play Spies vs. Mercenaries with Unreal lighting, materials and effects, using OpenSCCT’s shared simulation.", "Your Steam installation"),
    ];

    private async Task<IResult> ProjectHome(HttpContext c)
    {
        Dictionary<string, string> settings = _store.Settings();
        var body = new StringBuilder("<div class=\"project-site\">");
        if (settings.GetValueOrDefault("site.announcement") is { Length: > 0 } announcement)
            body.Append("<aside class=\"announcement\"><strong>Announcement</strong><p>").Append(H(announcement)).Append("</p></aside>");
        string heroImage = AssetUrl("/web/assets/showcase-bots.webp");
        var release = (await _releases.GetAsync(c.RequestAborted).ConfigureAwait(false)).Entries.FirstOrDefault();
        string releaseLabel = release is null ? "Release notes" : "Release " + release.Tag + " · " + release.Published.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        long matched = ProgressReport.Value.RootElement.GetProperty("editions").EnumerateArray()
            .SelectMany(e => e.GetProperty("targets").EnumerateArray()).Sum(t => t.GetProperty("matched_bytes").GetInt64());
        body.Append($"""
            <section class="project-hero" aria-labelledby="project-title">
              <img class="hero-backdrop" src="{H(heroImage)}" width="1280" height="720" fetchpriority="high" alt="">
              <div class="hero-inner"><div class="project-hero-copy">
                <a class="hero-release" href="/changelog#game-latest">{H(releaseLabel)} <span aria-hidden="true">↗</span></a>
                <p class="project-kicker">Splinter Cell</p>
                <h1 id="project-title">Chaos Theory<span>projects</span></h1>
                <p class="hero-summary">Reconstruct the PlayStation 2 and PC originals byte for byte. Explore Spies vs. Mercenaries with new graphics and features in Godot and Unreal.</p>
                <div class="hub-actions"><a class="hub-primary" href="/download">Download the launcher</a><a class="hub-secondary" href="/progress">See progress</a></div>
              </div>
              <aside class="hero-stat" aria-label="Matching decompilation progress"><strong>{N(matched)} <small>bytes</small></strong><span>of original code verified</span><p>{Editions.Count(e => e.Group == "Originals")} originals · {Editions.Count(e => e.Group == "Modernisations")} modernisations</p></aside>
              </div><p class="hero-caption">OpenSCCT Godot · Co-op boost in night vision</p>
            </section>
            <div id="editions">
            """);
        foreach (var editions in Editions.GroupBy(e => e.Group))
        {
            string id = editions.Key.ToLowerInvariant();
            body.Append($"<section class=\"hub-section\" id=\"{id}\" aria-labelledby=\"{id}-heading\"><header class=\"hub-heading\"><h2 id=\"{id}-heading\">{editions.Key}</h2></header>");
            if (editions.Key == "Modernisations")
                body.Append("<p class=\"edition-intro\">New engines, graphics and gameplay features for Spies vs. Mercenaries.</p>");
            body.Append("<div class=\"edition-grid\">");
            foreach (ProjectEdition e in editions)
                body.Append($"""
                <article class="edition-card {e.Id}">
                  <p class="edition-platform">{H(e.Platform)}</p><h3><a href="/projects/{e.Id}">{H(e.Name)}</a></h3>
                  <p>{H(e.Summary)}</p><div class="card-foot"><span>{H(e.Scope)}</span><span>{H(e.Requirement)}</span></div>
                </article>
                """);
            body.Append("</div></section>");
        }
        body.Append("""
            </div>
            """).Append(HomeProgress()).Append(MatchingWorkflow()).Append("""
            <section class="hub-section" aria-labelledby="work-heading">
              <header class="hub-heading"><h2 id="work-heading">Get involved</h2><a href="/wiki/contributing">Contributor guide</a></header>
              <div class="hub-resources">
            """).Append(Resource("/mods", "Browse mods", "Patched PS2 discs, modded PC installations and extra maps for the modernisations."))
            .Append(Resource("/tools", "Find a tool", "Editors, map collections and tools for studying the original game."))
            .Append(Resource(DecompGuide, "Reconstruct a function", "Identify an executable, compile a unit and compare the result against the original bytes."))
            .Append("""
            </div></section>
            <section class="hub-section" aria-labelledby="news-heading"><header class="hub-heading"><h2 id="news-heading">Project updates</h2></header><div class="hub-resources">
            """)
            .Append(Resource(UnrealSource + "/releases", "Unreal releases", "Builds and release notes for the Windows remake."))
            .Append(Resource("/changelog", "Godot, launcher and editor", "Read the release history for OpenSCCT Godot, its launcher and the map editor."))
            .Append(Resource(HubSource + "/blob/main/decomp/reports/2026-11-20.md", "20 November 2026 · Baseline report", "PS2 and Steam executable inventories, compiler research and the first candidate comparison."))
            .Append("</div></section>").Append(LegalSection()).Append("</div>");
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
        body.Append(Link("/#" + edition.Group.ToLowerInvariant(), edition.Group, "hub-back"))
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
            case "godot":
                setup = "Install Chaos Theory in Steam, including its Versus files. In the launcher, choose OpenSCCT Godot under Modernisations. The launcher builds the game assets, 11 match maps, eight tutorial maps and two front-end packages from your installed Steam copy. Choose Maps for optional Enhanced variants and community maps.";
                scope = "Godot 4 handles rendering, sound and input. The OpenSCCT simulation supports offline bot matches, network play and replays of Spies vs. Mercenaries.";
                source = HubSource + "/tree/main/game";
                break;
            case "unreal":
                setup = "Install the Unreal client through the Windows launcher. Use the original content built from your installed Steam copy. Community maps are available separately.";
                scope = "The Unreal client recreates Spies vs. Mercenaries using OpenSCCT simulation, Unreal rendering and converted maps. Check the release notes for changes to the current build.";
                source = UnrealSource;
                break;
            default:
                throw new InvalidDataException("No setup guide for " + edition.Id);
        }
        body.Append("<div class=\"edition-details\"><section><h2>Getting started</h2><p>").Append(H(setup))
            .Append("</p><div class=\"hub-actions\">").Append(Link("/download", "Download launcher", "hub-primary"))
            .Append(Link("/mods#" + edition.Id, "Mods for this edition", "hub-secondary")).Append("</div></section>")
            .Append("<section><h2>Project scope</h2><p>").Append(H(scope)).Append("</p><div class=\"hub-link-row\">")
            .Append(Link(source, "Project source")).Append(Link("/progress#" + edition.Id, "Progress report")).Append("</div></section></div>");
        if (edition.Group == "Originals")
            body.Append("<p class=\"hub-note\">").Append(Link("/projects/" + edition.Id + "/decomp", "Matching decompilation guide")).Append("</p>");
        if (edition.Id == "ps2")
            body.Append("<p class=\"hub-note\">").Append(Link(HubSource + "/blob/main/docs/editions/ps2.md", "Disc setup and supported serials")).Append("</p>");
        return Show(c, edition.Name, "projects", body.Append("</div>").ToString());
    }

    private IResult Mods(HttpContext c)
    {
        string body = ProjectHeading("Community", "Mods", "Choose a mod for the edition you play. Each edition uses its own format and setup.")
            + "<section class=\"hub-section\"><header class=\"hub-heading\"><h2>Originals</h2></header>"
            + GuideSection("ps2", "PS2 original", "Patched disc images",
                "Apply the mod author’s patch to a copy of your own disc image. Select the patched ISO in the launcher. Its disc serial must match your original. Switch back to the original image to play stock.",
                Link("/projects/ps2", "PS2 setup"))
            + GuideSection("pc", "PC / Steam", "Modded PC installations",
                "Choose a separate modded installation for campaign and co-op, Versus or the full PC game. Switch back to your base installation in Mods. For Versus, Enhanced updates maps and game balance, and Reloaded adds widescreen, input, controller and online improvements.",
                Link(EnhancedSource, "Enhanced SCCT Versus") + Link(ReloadedFeatures, "Reloaded features"))
            + "</section><section class=\"hub-section\"><header class=\"hub-heading\"><h2>Modernisations</h2></header>"
            + GuideSection("godot", "OpenSCCT Godot", "Additional Versus maps",
                "Start with the maps built from your installed Steam copy. Use Maps to add Enhanced variants or community maps to your collection.",
                Link("/projects/godot", "Godot setup") + Link("/wiki/maps", "Map guide"))
            + GuideSection("unreal", "OpenSCCT Unreal", "Custom map collections",
                "Build maps in OpenSCCT Editor. In the Windows launcher, enable the folder containing your compiled maps before starting Unreal. Give each map a unique ID.",
                Link("/wiki/editor", "OpenSCCT Editor guide") + Link(HubSource + "/blob/main/docs/UNREAL-MAP-MODS.md", "Map mod guide"))
            + "</section>";
        return Show(c, "Mods", "mods", "<div class=\"project-site\">" + body + "</div>");
    }

    private static string GuideSection(string id, string eyebrow, string title, string description, string links) =>
        $"<section id=\"{H(id)}\" class=\"hub-entry {H(id)}\"><p class=\"edition-platform\">{H(eyebrow)}</p><div><h2>{H(title)}</h2><p>{H(description)}</p><div class=\"hub-link-row\">{links}</div></div></section>";

    private IResult Tools(HttpContext c) => Show(c, "Tools", "tools", "<div class=\"project-site\">" +
        ProjectHeading("Development", "Tools & resources", "Find the matching guides for each original, the tools used to study retail executables and the editors for community maps.") +
        ReconstructionResources() + "<header class=\"hub-heading\"><h2>Map authoring and modding</h2></header>" +
        "<div class=\"hub-list\">" +
        GuideSection("maps", "Map authoring", "OpenSCCT Editor", "Edit geometry, lighting and objectives. Export a compiled map and playtest it with the target client.",
            Link("/wiki/editor", "Editor manual") + Link(EditorSource, "Editor source")) +
        GuideSection("retail-maps", "PC / Steam", "SCCT Map Manager", "Download and manage original, Enhanced and community maps for the retail PC Versus game.",
            Link("https://github.com/OpenSCCT/SCCT-Map-Manager", "Map Manager") + Link("https://github.com/OpenSCCT/SCCT-Maps", "Map collection")) + "</div></div>");

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
        return body.Append("<article><h3>").Append(Link("/progress#godot", "OpenSCCT Godot"))
            .Append("</h3><p>Windows and Linux builds</p><p class=\"progress-detail\">")
            .Append(Link("/changelog#game-latest", "Godot release notes")).Append("</p></article>")
            .Append("<article><h3>").Append(Link("/progress#unreal", "OpenSCCT Unreal"))
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
        body.Append(ProjectHeading("Development", "Progress reports", "Track byte matches for the original executables and playable releases for the Godot and Unreal modernisations."))
            .Append("<header class=\"hub-heading\"><h2>Originals</h2></header>")
            .Append("<p>Verified bytes come from complete functions rebuilt with the original compiler and flags. A percentage also needs a known total code size for that executable.</p>")
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
            foreach (JsonElement target in edition.GetProperty("targets").EnumerateArray())
                if (target.TryGetProperty("candidate_bytes", out var bytes) && bytes.GetInt64() > 0)
                    body.Append("<p><strong>Compiler experiment</strong> · ").Append(H(target.GetProperty("label").GetString()))
                        .Append(" has ").Append(N(bytes.GetInt64())).Append(" candidate bytes reproduced with an unverified toolchain. The verified total excludes them.</p>");
            body.Append("</section>");
        }
        body.Append("<section class=\"hub-section\"><header class=\"hub-heading\"><h2>Modernisations</h2></header>")
            .Append(GuideSection("godot", "Godot 4", "OpenSCCT Godot", "Follow Windows and Linux builds, gameplay features and the porting evidence for the shared simulation.",
                Link("/changelog#game-latest", "Godot release notes") + Link("/improvements", "Features") +
                Link(HubSource + "/blob/main/docs/progress/COVERAGE.md", "Simulation coverage")))
            .Append(GuideSection("unreal", "Unreal Engine 5", "OpenSCCT Unreal", "Follow packaged builds, gameplay changes and open issues for the modern Spies vs. Mercenaries client.",
                Link(UnrealSource + "/releases", "Unreal release notes") + Link(UnrealSource + "/issues", "Issue tracker")))
            .Append("</section></div>");
        return Show(c, "Progress reports", "progress", body.ToString());
    }
}
