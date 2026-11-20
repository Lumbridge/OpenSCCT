using Microsoft.AspNetCore.Http;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private static string MatchingWorkflow() => """
        <section class="hub-section" id="how-it-works" aria-labelledby="workflow-heading">
          <header class="hub-heading"><h2 id="workflow-heading">How matching decompilation works</h2><a href="/tools#project-guides">Project guides</a></header>
          <ol class="hub-resources workflow-steps">
            <li><h3>Disassemble</h3><p>Start with your own disc or Steam installation. Identify the retail executable by its hash, then separate instructions from data in your local copy.</p></li>
            <li><h3>Rewrite</h3><p>Reconstruct one function in readable C or C++. Build it with the original compiler and the flags used for that executable.</p></li>
            <li><h3>Verify</h3><p>Compare the complete function with its retail bytes. Only an exact match with the verified original toolchain counts towards progress.</p></li>
          </ol>
        </section>
        """;

    private static string ReconstructionResources() =>
        """
        <section id="project-guides" class="hub-section"><header class="hub-heading"><h2>Project guides</h2></header><div class="edition-grid">
        """ + Resource("/projects/ps2/decomp", "PlayStation 2", "Identify the disc revision and its MIPS boot, offline and online executables.")
        + Resource("/projects/pc/decomp", "PC / Steam", "Identify the Windows x86 campaign, co-op and Versus programs and compare compiled functions.")
        + """
        </div></section><section id="decomp-tools" class="hub-section"><header class="hub-heading"><h2>Decompilation tools</h2></header>
        <p>These tools support different stages of the reconstruction. OpenSCCT publishes its verified counts in the <a href="/progress">project report</a>.</p>
        <div class="tool-grid">
        """ + Resource("https://bytematch.dev/OpenSCCT/OpenSCCT", "ByteMatch", "Track the measured OpenSCCT original builds from the same verified-byte reports shown here.")
        + Resource("https://decomp.me/", "decomp.me", "Experiment with a single function on a supported compiler and architecture.")
        + Resource("https://github.com/encounter/objdiff", "objdiff", "Compare instructions, symbols and relocations in supported object files.")
        + Resource("https://github.com/ethteck/splat", "splat", "Describe segments and split a supported binary for reconstruction.")
        + Resource("https://github.com/Decompollaborate/spimdisasm", "spimdisasm", "Disassemble the PS2’s MIPS instructions and inspect references.")
        + Resource("https://github.com/matt-kempster/m2c", "m2c", "Draft C from MIPS assembly as a starting point for the PS2 rewrite.")
        + Resource("https://github.com/NationalSecurityAgency/ghidra", "Ghidra", "Inspect control flow, functions and data in PS2 MIPS and PC x86 executables.")
        + Resource("https://pcsx2.net/", "PCSX2", "Use the PS2 debugger to trace a function running from your own disc image.")
        + "</div></section>";

    private IResult ReconstructionGuide(HttpContext c)
    {
        string? edition = c.Request.RouteValues["edition"]?.ToString();
        if (edition is not ("ps2" or "pc"))
            return Show(c, "Guide unavailable", "projects", ProjectHeading("Originals", "Guide unavailable", "Choose the PS2 or PC matching guide.") + Link("/tools#project-guides", "Project guides"), status: 404);
        bool ps2 = edition == "ps2";
        string title = ps2 ? "PlayStation 2 matching guide" : "PC / Steam matching guide";
        string body = Link("/projects/" + edition, ps2 ? "PS2 original" : "PC / Steam", "hub-back")
            + ProjectHeading("Originals", title, ps2
                ? "The European SLES-53287 revision 1.01 has separate MIPS boot, offline and online programs. Each has its own build identity."
                : "Steam build 252084 contains separate 32-bit x86 executables for campaign and co-op, and for Versus.")
            + GuideSection("disassemble", "Disassemble", "Identify the retail input", ps2
                ? "Read SYSTEM.CNF from your local ISO. Check the boot program SLES_532.87 and the SC3_OFF.ELF and SC3_ON.ELF programs against their manifests. Their loaded sections mix instructions and data, so each code range needs evidence."
                : "Check System/splintercell3.exe and Versus/System/SCCT_Versus.ex against their manifests. The .ex file is the Versus payload. Preserve the distinction between stock and modded installations when comparing builds.",
                Link(DecompGuide + "/" + edition + "/config", "Executable manifests"))
            + GuideSection("rewrite", "Rewrite", "Reconstruct one function", ps2
                ? "Inspect the MIPS instructions and data references in Ghidra. splat, spimdisasm and m2c can help with splitting and drafting source. Use the PCSX2 debugger for observed behaviour. Record the compiler, flags and function boundaries for this exact revision."
                : "Inspect PE32 functions and references in Ghidra. Record the calling convention, original x86 compiler and build flags. Compile the function from source and identify its complete output in the object or linked binary.",
                Link("/tools#decomp-tools", "Tool resources"))
            + GuideSection("verify", "Verify", "Compare the complete bytes",
                "Check the full function length, relocations and retail hash. Only functions built with the verified original compiler and flags that reproduce every byte contribute to the report. Other toolchain experiments are recorded separately.",
                Link(DecompGuide + "/" + edition, "Build and verification commands") + Link("/progress#" + edition, "Current evidence"));
        return Show(c, title, "projects", "<div class=\"project-site\">" + body + "</div>");
    }

    private const string LegalCards = """
        <div class="hub-resources legal-cards">
          <section><h3>Unofficial</h3><p>OpenSCCT is an independent fan project. Ubisoft has no affiliation with the project.</p></section>
          <section><h3>No game files</h3><p>The matching project publishes source, hashes and build instructions. Retail executables and disc images stay on the contributor’s computer.</p></section>
          <section><h3>Your own copy</h3><p>Bring your PS2 disc image or installed PC game to play an original. The Godot and Unreal modernisations and OpenSCCT Editor use base assets built from your installed Steam copy.</p></section>
        </div>
        """;
    private const string Trademark = "<p class=\"trademark\">Splinter Cell and Splinter Cell Chaos Theory are trademarks of Ubisoft Entertainment.</p>";

    private static string LegalSection() =>
        "<section class=\"hub-section\" id=\"legal\" aria-labelledby=\"legal-heading\"><header class=\"hub-heading\"><h2 id=\"legal-heading\">Legal</h2>"
        + Link("/legal", "Project notice") + "</header>" + LegalCards + Trademark + "</section>";

    private IResult Legal(HttpContext c) => Show(c, "Legal", "legal", "<div class=\"project-site\">"
        + ProjectHeading("OpenSCCT", "Legal", "Game ownership, project downloads and attribution.") + LegalCards + Trademark
        + "<section class=\"hub-section\"><h2>Modernisations and community content</h2><p>Steam-based setup requires launcher 0.9.2 and game tools 0.8.166 or later. OpenSCCT Unreal 0.1.10 and Editor 0.2.54 use the same local build. Additional community maps are available separately. Earlier releases and the historical content catalogue include converted assets.</p><div class=\"hub-link-row\">"
        + Link("/projects/godot", "Godot setup") + Link("/projects/unreal", "Unreal setup") + Link(DecompGuide, "Matching project source")
        + Link("https://www.ubisoft.com/en-US/games/9058ea04-847b-4c8d-af09-438b8d804640", "Ubisoft game page") + "</div></section></div>");
}
