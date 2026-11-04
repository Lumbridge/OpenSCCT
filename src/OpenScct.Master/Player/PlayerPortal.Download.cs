using Microsoft.AspNetCore.Http;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private IResult Download(HttpContext c) => Show(c, "Download OpenSCCT", "download",
        PageHeader("DOWNLOAD OPENSCCT", "Get the game", "Choose your platform and start playing. Free to download, with no original game required.") +
        """
        <div class="download-platforms">
          <section class="panel platform-card" aria-labelledby="download-windows">
            <div class="platform-heading">
              <svg class="platform-icon" viewBox="0 0 32 32" fill="currentColor" aria-hidden="true"><path d="M3 5l12-2v12H3zm14-2l12-2v14H17zM3 17h12v12L3 27zm14 0h12v14l-12-2z"/></svg>
              <span class="platform-kind">Native launcher</span>
            </div>
            <h2 id="download-windows">Windows</h2>
            <p class="platform-summary">Install once. Let the launcher keep your game ready to play.</p>
            <ul class="platform-features"><li>Automatic game and launcher updates</li><li>Downloads game files and extra maps</li><li>Join games straight from the website</li></ul>
            <p class="platform-requirements">Windows · Intel/AMD 64-bit</p>
            <div class="platform-actions">
              <a class="button wide" href="https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT.Launcher.exe">Download for Windows ↓</a>
              <span class="platform-format">.exe · Ready to run</span>
              <a class="text-link" href="#install-windows">Windows setup →</a>
            </div>
          </section>
          <section class="panel platform-card" aria-labelledby="download-linux">
            <div class="platform-heading">
              <svg class="platform-icon" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="28" height="24" rx="4"/><path d="m8 11 5 5-5 5m9 0h7"/></svg>
              <span class="platform-kind">Native launcher</span>
            </div>
            <h2 id="download-linux">Linux</h2>
            <p class="platform-summary">The same install-and-play experience, built natively for Linux.</p>
            <ul class="platform-features"><li>Automatic game and launcher updates</li><li>No Wine, Proton or separate .NET install</li><li>App menu entry and website Join links</li></ul>
            <p class="platform-requirements">Intel/AMD 64-bit · X11 or XWayland<br>Ubuntu 22.04+, Debian 12+, Fedora, Arch</p>
            <div class="platform-actions">
              <a class="button wide" href="https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT.Launcher-linux-x64.tar.gz">Download for Linux ↓</a>
              <span class="platform-format">.tar.gz · Includes desktop installer</span>
              <a class="text-link" href="#install-linux">Linux setup →</a>
            </div>
          </section>
          <section class="panel platform-card" aria-labelledby="download-flatpak">
            <div class="platform-heading">
              <svg class="platform-icon" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m16 2 13 7v14l-13 7-13-7V9zm0 14 13-7M16 16 3 9m13 7v14M9.5 5.5l13 7v7"/></svg>
              <span class="platform-kind">Flatpak package</span>
            </div>
            <h2 id="download-flatpak">Linux / Steam Deck</h2>
            <p class="platform-summary">Prefer Flatpak? Install a self-contained game package and play directly.</p>
            <ul class="platform-features"><li>Downloads game content on first launch</li><li>Set up in Desktop Mode on Steam Deck</li><li>Install a newer bundle for game updates</li></ul>
            <p class="platform-requirements">Intel/AMD 64-bit · Flatpak required<br>Steam Deck hardware testing pending</p>
            <div class="platform-actions">
              <a class="button secondary wide" href="https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT-linux-x86_64.flatpak">Download Flatpak ↓</a>
              <span class="platform-format">.flatpak · Alternative game package</span>
              <a class="text-link" href="#install-flatpak">Flatpak &amp; Steam Deck setup →</a>
            </div>
          </section>
        </div>
        <div class="download-notes">
          <p>Linux gameplay and Steam Deck support are experimental.</p>
          <div class="button-row"><a class="text-link" href="/changelog">Release notes →</a><a class="text-link" href="https://github.com/Lumbridge/OpenSCCT/releases/latest">All files &amp; checksums ↗</a></div>
        </div>
        <section class="section download-install" aria-labelledby="install-heading">
          <div class="section-heading"><h2 id="install-heading">Up and running</h2></div>
          <details id="install-windows">
            <summary>Install on Windows</summary>
            <ol class="download-steps">
              <li><strong>Open the launcher</strong><p>Save OpenSCCT.Launcher.exe in a writable folder, such as Downloads, and run it.</p></li>
              <li><strong>Install OpenSCCT</strong><p>Choose Install. The launcher downloads the game and required files.</p></li>
              <li><strong>Start playing</strong><p>Choose PLAY. Find a Session joins a match, Hub opens a social space, and bots are available for offline practice.</p></li>
            </ol>
            <a class="text-link" href="https://github.com/Lumbridge/OpenSCCT/blob/main/docs/LAUNCHER.md">Full launcher guide ↗</a>
          </details>
          <details id="install-linux">
            <summary>Install the native Linux launcher</summary>
            <p>Open a terminal in the folder where you saved the archive, then run:</p>
            <pre><code>tar -xzf OpenSCCT.Launcher-linux-x64.tar.gz
        cd OpenSCCT-Launcher
        ./install.sh</code></pre>
            <p>This adds <strong>OpenSCCT Launcher</strong> to your application menu and enables website Join links. No sudo is needed. Open the launcher, choose <strong>Install</strong>, then <strong>PLAY</strong>.</p>
            <p class="account-side-note">Use a desktop with X11 or XWayland and working graphics drivers. ARM and Alpine/musl are not supported. Fedora 45 also needs <code>openssl3-libs</code>; see the guide for dependencies.</p>
            <a class="text-link" href="https://github.com/Lumbridge/OpenSCCT/blob/main/docs/LINUX.md#native-launcher">Full Linux setup guide ↗</a>
          </details>
          <details id="install-flatpak">
            <summary>Install Flatpak / set up Steam Deck</summary>
            <p>On Steam Deck, switch to Desktop Mode first. Open a terminal in the folder containing the downloaded bundle:</p>
            <pre><code>flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
        flatpak install --user ./OpenSCCT-linux-x86_64.flatpak
        flatpak run io.github.Lumbridge.OpenSCCT</code></pre>
            <p>On first launch, choose <strong>Download and play</strong>. Install a newer Flatpak bundle when you want to update the game.</p>
            <p class="account-side-note">Steam Deck hardware validation is still pending. The guide covers adding OpenSCCT to Steam and setting up controllers.</p>
            <a class="text-link" href="https://github.com/Lumbridge/OpenSCCT/blob/main/docs/LINUX.md#flatpak-install">Flatpak &amp; Steam Deck guide ↗</a>
          </details>
        </section>
        """ +
        Section("Getting started", "<div class=\"grid-three\">" +
            Feature("01", "Playing without an account", "You can play offline and join servers marked Guests welcome from inside the game without an account. Sign in with Discord to reserve a name and save your stats.", "/account", "Set up your account →") +
            Feature("02", "Find people to play with", "See which games have room for more players. Hubs are social spaces where you can meet between matches. Some servers need an account or password.", "/servers", "Browse games and hubs →") +
            Feature("03", "Need help installing?", "Find setup guides, report a problem or suggest an improvement.", "/help", "Get help →") + "</div>"));
}
