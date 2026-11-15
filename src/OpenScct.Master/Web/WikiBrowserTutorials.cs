namespace OpenScct.Master.Web;

internal static class WikiBrowserTutorials
{
    internal static string Grab => Embed("spy-grab", "Practise grabbing a mercenary",
        "Approach the stopped mercenary from behind. Try Grab only, then repeat with the moving mercenary. He walks without attacking, and practice damage protection keeps you safe.");

    internal static string Vision => Embed("vision-cancelling", "Practise vision cancelling",
        "Walk off the TestRange tower. Switch night or thermal vision as your feet touch the floor and keep moving. The coach reads the game's actual falling and landing states.");

    private static string Embed(string lesson, string title, string instruction) => $$"""
        <link rel="stylesheet" href="{{WebHtml.AssetUrl("/web/assets/browser-training.css")}}">
        <section class="browser-training" data-browser-lesson="{{lesson}}">
          <div class="browser-training-intro"><span class="badge">PLAY OPENSCCT</span><h3>{{title}}</h3>
            <p>{{instruction}}</p><p>Uses the full game simulation, your saved controls and the real TestRange map. Sign in inside the game to use your account settings.</p>
            <button type="button" class="button" data-training-load>Load browser game</button>
            <a class="text-link" href="/play/?lesson={{lesson}}" target="_blank" rel="noopener">Open in a separate tab ↗</a>
            <p class="muted">Requires a desktop browser with WebGPU and hardware acceleration. The first load downloads the engine and game content. The desktop game remains available from Download.</p>
          </div>
          <iframe title="{{title}}" data-training-frame hidden allow="fullscreen; gamepad; autoplay" allowfullscreen></iframe>
          <p class="browser-training-status" data-training-status role="status"></p>
          <noscript><p>Enable JavaScript to load the game, or open the separate-tab link.</p></noscript>
        </section>
        <script src="{{WebHtml.AssetUrl("/web/assets/browser-training.js")}}" defer></script>
        """;
}
