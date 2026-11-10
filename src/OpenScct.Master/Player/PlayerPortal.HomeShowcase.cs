using System.Text;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private static readonly (string Title, ShowcaseImage Image)[] HomeSlides =
    [
        ("ROUND SCOREBOARD", new("showcase-scoreboard.webp", "The round scoreboard shows Spy and Mercenary teams in an Aquarius practice match.",
            "See round stats for both teams and revisit past scoreboards in Match History.")),
        ("OPENSCCT MAP EDITOR", new("showcase-editor.webp", "OpenSCCT Editor with a map open in perspective and orthographic viewports.",
            "Build maps, place objectives and playtest them in OpenSCCT.")),
        ("SPY CLOAK", new("showcase-cloak.webp", "A cloaked spy on Aquarius with the suit energy meter visible.",
            "Every spy has a built-in cloak that runs on rechargeable suit energy.")),
        ("CHALLENGES", new("showcase-challenges.webp", "The in-game Achievements screen with challenge progress and a reward preview.",
            "Track achievements for both roles, weapons, gadgets and maps.")),
        ("COSMETIC UNLOCKS", new("showcase-unlockables.webp", "The Armoury shows spy suits and light colours next to a 3D character preview.",
            "Earn suits, light colours and weapon finishes, then preview them in the Armoury.")),
        ("OFFLINE BOTS", new("showcase-bots.webp", "A spy and a teammate bot climb a ledge together in night vision.",
            "Fill out both teams with bots and practise co-op moves like boosts and hoists.", 1280, 720)),
        ("MERCENARY SIDEARMS", new("showcase-sidearm.png", "A mercenary holding a Desert Eagle during a Steel Squat match.",
            "Draw a backup pistol in sessions with sidearms enabled.")),
        ("GAMEPLAY REPLAYS", new("showcase-replays.png", "A mercenary replay on Aquarius, with playback controls visible.",
            "Watch a recorded match with playback controls and first-person or free camera views.")),
        ("COMMUNITY HUBS", new("showcase-hub.png", "A spy in the OpenSCCT community hub.",
            "Meet up between matches and join a game from the hub.")),
    ];

    private static string HomeShowcase()
    {
        var html = new StringBuilder("""
            <section class="community-hero-scene" aria-label="OpenSCCT features" aria-roledescription="carousel" data-home-showcase>
              <div class="hero-carousel-controls" hidden>
                <button type="button" data-rotation>Pause</button>
                <span data-slide-count></span>
                <button type="button" data-step="-1" aria-label="Previous feature" aria-controls="hero-carousel-track">←</button>
                <button type="button" data-step="1" aria-label="Next feature" aria-controls="hero-carousel-track">→</button>
              </div>
              <div class="hero-carousel-track" id="hero-carousel-track" role="group" aria-label="Feature images" tabindex="0">
            """);
        for (int i = 0; i < HomeSlides.Length; i++)
        {
            var (title, image) = HomeSlides[i];
            string priority = i == 0 ? "fetchpriority=\"high\"" : "loading=\"lazy\"";
            string asset = H(AssetUrl("/web/assets/" + image.File));
            html.Append($"""
                <figure role="group" aria-roledescription="slide" aria-label="{i + 1} of {HomeSlides.Length}">
                  <a href="{asset}" target="_blank" rel="noopener" aria-label="{H("Open full-size image. " + image.Alt)}">
                    <img src="{asset}" alt="{H(image.Alt)}" width="{image.Width}" height="{image.Height}" {priority}>
                  </a>
                  <figcaption><span>{H(title)}</span>{H(image.Caption)}</figcaption>
                </figure>
                """);
        }
        return html.Append("</div><span class=\"hero-carousel-announcement\" role=\"status\"></span></section>").ToString();
    }
}
