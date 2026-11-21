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
    private const string DecompSource = "https://github.com/OpenSCCT/scct-decomp";
    private const string DecompGuide = DecompSource + "/tree/main";
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
        var release = (await _releases.GetAsync(c.RequestAborted).ConfigureAwait(false)).Entries.FirstOrDefault();
        string releaseLabel = release is null ? "Release notes" : "Release " + release.Tag;
        string releaseDate = release is null ? "" : " · " + release.Published.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        JsonElement progress = _progressReport.ReadEditions();
        long matched = progress.EnumerateArray().SelectMany(e => e.GetProperty("targets").EnumerateArray())
            .Sum(t => t.GetProperty("matched_bytes").GetInt64());
        body.Append($"""
            <section class="project-hero" aria-labelledby="project-title">
              <img class="hero-backdrop" src="{H(AssetUrl("/web/assets/showcase-hero.webp"))}" width="1920" height="817" fetchpriority="high" alt="">
              <div class="hero-inner"><div class="project-hero-copy">
                <a class="hero-release" href="/changelog#game-latest"><span class="release-dot"></span>{H(releaseLabel)}<span class="release-date">{H(releaseDate)}</span><span aria-hidden="true">↗</span></a>
                <h1 id="project-title">Chaos Theory<span>reconstructed.</span></h1>
                <p class="hero-summary">Play the PlayStation 2 and PC originals. Rediscover Spies vs. Mercenaries in Godot and Unreal. Follow the work to rebuild the original code, byte for byte.</p>
                <div class="hub-actions"><a class="hub-primary" href="/download">{HubIcon("download")} Download the launcher</a><a class="hub-secondary" href="/progress">{HubIcon("progress")} See progress</a></div>
                <p class="hero-requirement">Bring your own disc image or installed copy. <a href="/legal">How it works</a></p>
              </div>
              <aside class="hero-stat" aria-label="Matching decompilation progress"><strong>{N(matched)} <small>bytes</small></strong><span>of original code verified</span><p>{Editions.Count(e => e.Group == "Originals")} originals · {Editions.Count(e => e.Group == "Modernisations")} modernisations</p></aside></div>
            </section>
            <div class="directory-band"><div class="band-inner" id="editions">
              <header class="hub-heading"><div><p class="hub-eyebrow">Project directory</p><h2>Choose your edition</h2></div><a href="/progress">Full report and sources ↗</a></header>
            """);
        foreach (var group in Editions.GroupBy(e => e.Group))
        {
            string id = group.Key.ToLowerInvariant();
            string description = group.Key == "Originals" ? "The complete 2005 game on its original platforms." : "Spies vs. Mercenaries rebuilt with new engines, graphics and features.";
            body.Append($"<section class=\"edition-group\" id=\"{id}\" aria-labelledby=\"{id}-heading\"><header><h3 id=\"{id}-heading\">{group.Key}</h3><p>{description}</p></header><div class=\"edition-library\">");
            foreach (ProjectEdition edition in group) body.Append(LibraryCard(edition, progress));
            body.Append("</div></section>");
        }
        body.Append("</div></div><div class=\"workflow-band\"><div class=\"band-inner\">").Append(MatchingWorkflow())
            .Append("<div class=\"workflow-links\">").Append(Link("/projects/ps2/decomp", "PS2 matching guide →")).Append(Link("/projects/pc/decomp", "PC matching guide →")).Append("</div></div></div>")
            .Append("<div class=\"resources-band\"><div class=\"band-inner\">").Append(ReconstructionResources()).Append("</div></div>")
            .Append("""
              <div class="updates-band"><div class="band-inner"><section aria-labelledby="news-heading"><header class="hub-heading"><div><p class="hub-eyebrow">Development</p><h2 id="news-heading">Project updates</h2></div><a href="/changelog">All updates →</a></header><div class="hub-resources">
              """)
            .Append(Resource("/changelog#game-latest", releaseLabel + releaseDate, "Changes to OpenSCCT Godot, the launcher and the tools for building content from Steam."))
            .Append(Resource(UnrealSource + "/releases", "Unreal releases", "Windows builds, engine changes and the history of the Unreal modernisation."))
            .Append(Resource(DecompSource + "/tree/main/reports", "Reconstruction reports", "Read the measurements and compiler evidence behind each original target."))
            .Append("</div></section></div></div><div class=\"legal-band\"><div class=\"band-inner\">").Append(LegalSection()).Append("</div></div></div>");
        return Show(c, "Chaos Theory projects", "home", body.ToString());
    }

    private static string HubIcon(string name)
    {
        string lines = name switch
        {
            "download" => "<path d='M12 3v12m-5-5 5 5 5-5M4 16v4h16v-4'/>",
            "progress" => "<path d='M3 20h18M5 16v-4m6 4V8m6 8V4'/>",
            _ => "<path d='M5 12h14m-6-6 6 6-6 6'/>"
        };
        return $"<svg class='hub-icon' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'>{lines}</svg>";
    }

    private static string EditionImage(ProjectEdition edition) => AssetUrl("/web/assets/showcase-edition-" + edition.Id + ".webp");

    private static string LibraryCard(ProjectEdition edition, JsonElement progress)
    {
        string image = EditionImage(edition);
        string details;
        if (edition.Group == "Originals")
        {
            JsonElement targets = progress.EnumerateArray().First(p => p.GetProperty("id").GetString() == edition.Id).GetProperty("targets");
            long verified = targets.EnumerateArray().Sum(t => t.GetProperty("matched_bytes").GetInt64());
            var rows = new StringBuilder();
            foreach (JsonElement target in targets.EnumerateArray())
                rows.Append("<li><span>").Append(H(target.GetProperty("label").GetString())).Append("</span><strong>")
                    .Append(H(Measure(target).Bytes)).Append(" verified</strong></li>");
            details = $"<details class='card-evidence'><summary>{N(verified)} verified bytes · {N(targets.GetArrayLength())} targets</summary><ul>{rows}</ul>"
                + Link("/progress#" + edition.Id, "Compiler evidence and candidates →") + "</details>";
        }
        else
        {
            string href = edition.Id == "godot" ? "/changelog#game-latest" : UnrealSource + "/releases";
            details = $"<p class='card-release'><span class='release-dot'></span>{H(edition.Scope)} {Link(href, "Release notes ↗")}</p>";
        }
        return $"""
            <article class="library-card">
              <a href="/projects/{edition.Id}" class="library-image" aria-label="Explore {H(edition.Name)}"><img src="{H(image)}" width="1000" height="563" loading="lazy" alt=""></a>
              <div class="library-copy"><p class="edition-platform">{H(edition.Platform)}</p><h4><a href="/projects/{edition.Id}">{H(edition.Name)} {HubIcon("arrow")}</a></h4>
                <p>{H(edition.Summary)}</p>{details}</div>
            </article>
            """;
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
        (string title, string text)[] steps;
        string scope, source, status, proof, proofLabel;
        switch (edition.Id)
        {
            case "ps2":
                steps = [("Prepare your disc", "Make an uncompressed ISO from your own Chaos Theory disc."),
                    ("Configure PCSX2", "Set up PCSX2 with your console’s BIOS and controller settings."),
                    ("Select the ISO", "In Games, choose PS2 original. Select your ISO and PCSX2, then launch the game.")];
                scope = "Play the original console campaign and multiplayer components in PCSX2. Native reconstruction tracks the boot, offline and online programs from the disc separately.";
                source = DecompGuide + "/ps2"; status = "Play via PCSX2"; proof = "/projects/ps2/decomp"; proofLabel = "PS2 matching guide"; break;
            case "pc":
                steps = [("Install Chaos Theory", "Install the Steam edition or use the complete game from your PC media."),
                    ("Choose the installation", "Select PC / Steam in Games. The launcher can find your Steam copy or use a folder you choose."),
                    ("Choose a game mode", "Start campaign and co-op, or launch Versus separately. On Linux, Steam starts the Steam campaign and Wine runs Versus and manual installations.")];
                scope = "The complete PC original includes campaign, co-op and Spies vs. Mercenaries. Enhanced and Reloaded are optional Versus mods. You can keep a separate modded installation.";
                source = DecompGuide + "/pc"; status = "Play your installed game"; proof = "/projects/pc/decomp"; proofLabel = "PC matching guide"; break;
            case "godot":
                steps = [("Install the Steam game", "Install Chaos Theory with its Versus files on this computer."),
                    ("Build the original maps", "Under Modernisations, choose OpenSCCT Godot and select your installed Steam copy. Setup builds 11 match maps, eight tutorial maps and two front-end packages locally."),
                    ("Play or add a map", "Choose Play for the stock rotation. Open Maps for optional Enhanced variants and community maps.")];
                scope = "Godot 4 handles rendering, sound and input. OpenSCCT’s simulation supports offline bot matches, online play and replays of Spies vs. Mercenaries.";
                source = HubSource + "/tree/main/game"; status = "Windows and Linux"; proof = "/changelog#game-latest"; proofLabel = "Godot release notes"; break;
            case "unreal":
                steps = [("Install Chaos Theory", "Install the Steam version with Versus on your Windows PC."),
                    ("Set up Unreal", "In Modernisations, choose OpenSCCT Unreal to install the client. Open its setup window, select your Steam game folder and run the Unreal content converter."),
                    ("Start a match", "Launch the Unreal client with the locally built stock maps. Community map downloads and maps you compile with OpenSCCT Editor are optional.")];
                scope = "OpenSCCT’s shared simulation drives Spies vs. Mercenaries. Unreal Engine 5 handles lighting, materials, effects and the presentation of your locally converted maps and characters.";
                source = UnrealSource; status = "Windows x64"; proof = UnrealSource + "/releases"; proofLabel = "Unreal release notes"; break;
            default:
                throw new InvalidDataException("No setup guide for " + edition.Id);
        }
        var body = new StringBuilder($"""
            <div class="project-site edition-page"><section class="edition-hero">
              <img class="edition-backdrop" src="{H(EditionImage(edition))}" width="1000" height="563" alt="">
              <div class="edition-hero-copy"><a class="hub-back" href="/#{edition.Group.ToLowerInvariant()}">← {H(edition.Group)}</a>
              <p class="hub-eyebrow">{H(edition.Platform)} · {H(status)}</p><h1>{H(edition.Name)}</h1><p>{H(edition.Summary)}</p>
              <div class="hub-actions"><a class="hub-primary" href="/download">{HubIcon("download")} Download launcher</a>
              <a class="hub-secondary" href="/mods?edition={edition.Id}">Mods for this edition</a></div></div></section>
              <section class="hub-section" id="setup"><header class="hub-heading"><div><p class="hub-eyebrow">{H(edition.Requirement)}</p><h2>Getting started</h2></div></header><ol class="setup-cards">
            """);
        foreach (var (title, text) in steps) body.Append($"<li><h3>{H(title)}</h3><p>{H(text)}</p></li>");
        body.Append("</ol></section><section class='hub-section scope-band'><div><h2>Project scope</h2><p>").Append(H(scope)).Append("</p><div class='hub-link-row'>")
            .Append(Link(proof, proofLabel + " →")).Append(Link("/progress#" + edition.Id, "Progress report")).Append(Link(source, "Project source")).Append("</div></div>");
        if (edition.Group == "Originals")
        {
            JsonElement targets = _progressReport.ReadEditions().EnumerateArray().First(p => p.GetProperty("id").GetString() == edition.Id).GetProperty("targets");
            body.Append("<div class='edition-proof'><p class='hub-eyebrow'>Original reconstruction</p><strong>")
                .Append(N(targets.EnumerateArray().Sum(t => t.GetProperty("matched_bytes").GetInt64()))).Append(" verified bytes</strong><p>")
                .Append(N(targets.GetArrayLength())).Append(" retail executables tracked. See the report for revision and compiler evidence.</p></div>");
        }
        else
        {
            string shot = edition.Id == "unreal" ? "showcase-unreal-aquarius.webp" : "showcase-bots.webp";
            string caption = edition.Id == "unreal" ? "Aquarius in OpenSCCT Unreal, built from an installed Steam copy." : "A co-op boost in OpenSCCT Godot’s night vision.";
            body.Append($"<figure class='edition-shot'><img src='{H(AssetUrl("/web/assets/" + shot))}' width='1200' height='675' loading='lazy' alt='{H(caption)}'><figcaption>{H(caption)}</figcaption></figure>");
        }
        body.Append("</section>");
        if (edition.Id == "ps2") body.Append(Link(HubSource + "/blob/main/docs/editions/ps2.md", "Disc setup and supported serials →", "hub-back"));
        return Show(c, edition.Name, "projects", body.Append("</div>").ToString());
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

    private readonly ProjectProgressReport _progressReport = new(options.ResolvePath(ProjectProgressReport.Filename));

    private static (string Bytes, string Percent) Measure(JsonElement target)
    {
        JsonElement bytes = target.GetProperty("total_bytes");
        if (bytes.ValueKind != JsonValueKind.Number || bytes.GetInt64() <= 0)
            return ("Code boundaries under study", "Unmeasured");
        long total = bytes.GetInt64();
        long matched = target.GetProperty("matched_bytes").GetInt64();
        return (N(matched) + " / " + N(total) + " bytes", CoverageRate(matched, total));
    }

    private static string CoverageRate(long bytes, long total)
    {
        decimal percent = 100m * bytes / total;
        return percent is > 0 and < 0.01m ? "<0.01%" : percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    private static string CoverageCard(JsonElement target)
    {
        long bytes = target.GetProperty("candidate_bytes").GetInt64() + target.GetProperty("matched_bytes").GetInt64();
        long functions = target.GetProperty("candidate_units").GetInt64() + target.GetProperty("matched_units").GetInt64();
        JsonElement denominator = target.GetProperty("total_bytes");
        long total = denominator.ValueKind == JsonValueKind.Number ? denominator.GetInt64() : 0;
        string label = target.GetProperty("label").GetString()!;
        string[] title = label.Split(" · ", 2, StringSplitOptions.None);
        string platform = title.Length == 2 ? $"<small>{H(title[0])}</small>" : "";
        string meter = total > 0
            ? $"<progress aria-label='{H(label)} byte-identical coverage' value='{bytes}' max='{total}'></progress>"
            : "";
        string description = total > 0 ? $"{N(bytes)} of {N(total)} code bytes" : $"{N(bytes)} code bytes · total under study";
        string rate = total > 0 ? CoverageRate(bytes, total) : "Unmeasured";
        return $"""
            <article class="coverage-card">
              {platform}<h3>{H(title[^1])}</h3>
              <strong class="coverage-rate{(total > 0 ? "" : " pending")}">{H(rate)}</strong>
              {meter}<p>{description}</p>
              <footer><span>{N(functions)} {(functions == 1 ? "function" : "functions")}</span>{Link(DecompSource + "/blob/main/" + target.GetProperty("evidence").GetString(), "Evidence ↗")}</footer>
            </article>
            """;
    }

    private IResult ProjectProgress(HttpContext c)
    {
        var body = new StringBuilder("<div class=\"project-site\">");
        body.Append(ProjectHeading("Development", "Progress reports", "Track byte matches for the original executables and playable releases for the Godot and Unreal modernisations."))
            .Append("<header class=\"hub-heading\"><h2>Originals</h2></header>")
            .Append("<p>Coverage measures code rebuilt byte for byte against each original executable. Compiler verification also requires evidence of the original compiler and flags.</p>")
            .Append("<div class=\"hub-link-row report-links\">").Append(Link(DecompGuide, "Reproduction guide"))
            .Append(Link(DecompSource + "/blob/main/reports/progress.json", "Report source")).Append("</div>");
        var editions = _progressReport.ReadEditions().EnumerateArray();
        foreach (JsonElement edition in editions.OrderByDescending(e => e.GetProperty("targets").EnumerateArray()
                     .Any(t => t.GetProperty("total_bytes").ValueKind == JsonValueKind.Number && t.GetProperty("total_bytes").GetInt64() > 0)))
        {
            string? id = edition.GetProperty("id").GetString();
            ProjectEdition project = Editions.First(e => e.Id == id);
            body.Append($"""
                <section id="{H(id)}" class="hub-section coverage-edition">
                  <header class="coverage-banner"><img src="{H(EditionImage(project))}" alt="" loading="lazy">
                    <div><p class="edition-platform">{H(edition.GetProperty("stage").GetString())}</p><h2>{H(project.Name)}</h2></div>
                    {Link("/projects/" + id, "Edition setup ↗")}
                  </header><div class="coverage-grid">
                """);
            var rows = new List<string[]>();
            foreach (JsonElement target in edition.GetProperty("targets").EnumerateArray())
            {
                body.Append(CoverageCard(target));
                var measure = Measure(target);
                string evidence = DecompSource + "/blob/main/" + target.GetProperty("evidence").GetString();
                long units = target.GetProperty("candidate_units").GetInt64();
                string candidates = N(units) + (units == 1 ? " function" : " functions") + " · " + N(target.GetProperty("candidate_bytes").GetInt64()) + " bytes";
                rows.Add([Link(evidence, target.GetProperty("label").GetString()!), H(target.GetProperty("scope").GetString()), candidates, H(measure.Bytes), H(measure.Percent)]);
            }
            body.Append("</div>");
            if (rows.Count > 0)
                body.Append("<details class='coverage-proof'><summary>Compiler verification &amp; byte counts</summary>")
                    .Append(Table(["Executable", "Scope", "Byte-identical candidates", "Verified code", "Verified progress"], rows)).Append("</details>");
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
