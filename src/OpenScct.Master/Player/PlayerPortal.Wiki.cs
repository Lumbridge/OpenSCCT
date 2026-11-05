using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private void MapWiki(WebApplication app)
    {
        Get(app, "/wiki", WikiIndex);
        Get(app, "/wiki/game", c => WikiArea(c, "game"));
        Get(app, "/wiki/editor", c => WikiArea(c, "editor"));
        Get(app, "/wiki/{slug}", WikiArticle);
    }

    private IResult WikiIndex(HttpContext c)
    {
        string query = Query(c, "q", 96);
        string category = Query(c, "category", 24);
        // Keep bookmarked searches useful while sending them to the appropriate area.
        if (query.Length > 0 || category.Length > 0)
        {
            WikiContent.Category? topic = WikiContent.Categories.FirstOrDefault(x => x.Id == category);
            string area = topic?.Area ?? (query.Length > 0 && !WikiContent.Search(query, "", "game").Any() &&
                WikiContent.Search(query, "", "editor").Any() ? "editor" : "game");
            string target = "/wiki/" + area;
            if (query.Length > 0) target += "?q=" + U(query);
            if (topic is not null) target += (query.Length > 0 ? "&" : "?") + "category=" + U(topic.Id);
            return Results.Redirect(target);
        }
        var body = new StringBuilder("<div class=\"wiki-page\">")
            .Append(PageHeader("WIKI", "OpenSCCT wiki", "Choose the game guides to play, or the editor guides to build your own maps."))
            .Append("<div class=\"wiki-area-grid\">");
        AppendWikiAreaCard(body, "game", "Game wiki", "PLAY OPENSCCT", "Install the game, learn each role, choose your equipment and join or host a session.", "Browse game guides", "/web/assets/mark.svg");
        AppendWikiAreaCard(body, "editor", "Editor wiki", "CREATE MAPS", "Set up the map editor, build a layout, add objectives and lighting, then test and share your map.", "Browse editor guides", "/web/assets/mark-editor.svg");
        body.Append("</div><section class=\"wiki-help panel\"><h2>Need a hand?</h2><p>Get help with a problem or suggest a documentation correction.</p>")
            .Append(Link("/help", "Help & feedback →", "text-link")).Append("</section></div>");
        return Show(c, "Wiki", "wiki", body.ToString());
    }

    private IResult WikiArea(HttpContext c, string area)
    {
        bool editor = area == "editor";
        string areaUrl = "/wiki/" + area;
        string title = editor ? "Editor wiki" : "Game wiki";
        string query = Query(c, "q", 96);
        string category = Query(c, "category", 24);
        WikiContent.Category[] categories = WikiContent.CategoriesFor(area).ToArray();
        if (!categories.Any(x => x.Id == category)) category = string.Empty;
        WikiContent.Article[] articles = WikiContent.Search(query, category, area).ToArray();
        var body = new StringBuilder("<div class=\"wiki-page\">")
            .Append(WikiAreaNavigation(area))
            .Append(PageHeader(editor ? "OPENSCCT EDITOR" : "OPENSCCT GAME", title, editor
                ? "Create maps with guides to project setup, geometry, actors, objectives, lighting and playtesting."
                : "Get ready to play with guides to installation, roles, equipment, online sessions and player stats."))
            .Append("<form class=\"wiki-search\" role=\"search\" method=\"get\" action=\"").Append(areaUrl).Append("\"><label for=\"wiki-query\">Find ").Append(editor ? "an editor" : "a game").Append(" guide</label>")
            .Append("<div class=\"wiki-search-fields\"><input type=\"search\" id=\"wiki-query\" name=\"q\" maxlength=\"96\" placeholder=\"").Append(editor ? "Try lighting, objectives or blockout" : "Try cloak, hosting or controls").Append("\" value=\"").Append(H(query)).Append("\">")
            .Append("<label class=\"wiki-category-label\" for=\"wiki-category\">Topic</label><select id=\"wiki-category\" name=\"category\"><option value=\"\">All topics</option>");
        foreach (WikiContent.Category item in categories)
            body.Append("<option value=\"").Append(item.Id).Append('"').Append(item.Id == category ? " selected" : "").Append('>').Append(H(item.Title)).Append("</option>");
        body.Append("</select><button class=\"button\" type=\"submit\">Search guides</button></div></form>");

        if (query.Length == 0 && category.Length == 0)
        {
            body.Append("<section class=\"wiki-start\" aria-labelledby=\"wiki-start-title\"><div><p class=\"eyebrow\">GETTING STARTED</p><h2 id=\"wiki-start-title\">Setup guides</h2>")
                .Append("<p>").Append(editor ? "Set up your first project, build a test map and check it in the game." : "Install OpenSCCT, learn your role's controls and join your first session.")
                .Append("</p></div><div class=\"button-row\">")
                .Append(Link(editor ? "/wiki/editor-getting-started" : "/wiki/getting-started", editor ? "Editor quick start →" : "Player quick start →", "button secondary")).Append("</div></section>")
                .Append("<nav class=\"wiki-topic-nav\" aria-label=\"Wiki topics\">");
            foreach (WikiContent.Category item in categories)
                body.Append(Link("#wiki-" + item.Id, item.Title));
            body.Append("</nav>");
        }
        else
        {
            body.Append("<div class=\"wiki-results-heading\"><p role=\"status\">").Append(N(articles.Length)).Append(articles.Length == 1 ? " guide" : " guides");
            if (query.Length > 0) body.Append(" matching “").Append(H(query)).Append('”');
            body.Append("</p>").Append(Link(areaUrl, "Clear filters", "text-link")).Append("</div>");
        }

        if (articles.Length == 0)
            body.Append(Empty("No guides found", "Try a shorter search, a different word or All topics."));
        foreach (WikiContent.Category item in categories)
        {
            WikiContent.Article[] group = articles.Where(a => a.Category == item.Id).ToArray();
            if (group.Length == 0) continue;
            body.Append("<section class=\"wiki-topic\" id=\"wiki-").Append(item.Id).Append("\" aria-labelledby=\"wiki-topic-").Append(item.Id)
                .Append("\"><div class=\"section-heading\"><div><h2 id=\"wiki-topic-").Append(item.Id).Append("\">").Append(H(item.Title)).Append("</h2><p>")
                .Append(H(item.Description)).Append("</p></div><span class=\"wiki-count\">").Append(N(group.Length)).Append(group.Length == 1 ? " guide" : " guides").Append("</span></div><div class=\"wiki-guide-grid\">");
            foreach (WikiContent.Article article in group) AppendWikiCard(body, article);
            body.Append("</div></section>");
        }
        body.Append("<section class=\"wiki-help panel\"><h2>Further help</h2><p>Troubleshooting, bug reports and documentation corrections.</p><div class=\"button-row\">")
            .Append(Link(editor ? "/wiki/editor-testing#check" : "/wiki/troubleshooting", editor ? "Map checks →" : "Troubleshooting →", "text-link"))
            .Append(Link("/help", "Help & feedback →", "text-link")).Append("</div></section></div>");
        return Show(c, title, "wiki", body.ToString(), editorTheme: editor);
    }

    private IResult WikiArticle(HttpContext c)
    {
        string slug = c.Request.RouteValues["slug"]?.ToString() ?? string.Empty;
        if (WikiContent.Find(slug) is not { } article)
            return Show(c, "Guide not found", "wiki", PageHeader("WIKI", "Guide not found", "That guide does not exist. Browse the wiki or search for the topic you need.") +
                Link("/wiki", "Browse all guides →", "button secondary"), status: 404);
        WikiContent.Category category = WikiContent.Categories.Single(x => x.Id == article.Category);
        string areaUrl = "/wiki/" + category.Area;
        bool editor = category.Area == "editor";
        var body = new StringBuilder("<div class=\"wiki-page\">").Append(WikiAreaNavigation(category.Area))
            .Append("<nav class=\"wiki-breadcrumb\" aria-label=\"Breadcrumb\">")
            .Append(Link("/wiki", "Wiki")).Append("<span aria-hidden=\"true\">/</span>")
            .Append(Link(areaUrl, editor ? "Editor wiki" : "Game wiki")).Append("<span aria-hidden=\"true\">/</span>")
            .Append(Link(areaUrl + "?category=" + U(category.Id), category.Title)).Append("</nav>")
            .Append(PageHeader(category.Title.ToUpperInvariant(), article.Title, article.Summary))
            .Append("<div class=\"wiki-reading-layout\"><aside class=\"wiki-contents\" aria-label=\"On this page\"><p class=\"eyebrow\">ON THIS PAGE</p><nav><ol>");
        foreach (WikiContent.Section section in article.Sections)
            body.Append("<li>").Append(Link("#" + section.Id, section.Title)).Append("</li>");
        body.Append("</ol></nav>").Append(Link(areaUrl, editor ? "← All editor guides" : "← All game guides", "text-link")).Append("</aside><article class=\"wiki-article\" aria-label=\"").Append(H(article.Title)).Append("\">");
        foreach (WikiContent.Section section in article.Sections)
            body.Append("<section id=\"").Append(H(section.Id)).Append("\" aria-labelledby=\"heading-").Append(H(section.Id)).Append("\"><h2 id=\"heading-")
                .Append(H(section.Id)).Append("\">").Append(H(section.Title)).Append("</h2>").Append(section.Html).Append("</section>");
        body.Append("<div class=\"wiki-article-feedback\"><p>For missing or outdated information: ")
            .Append(Link("/help", "Report a documentation issue →", "text-link")).Append("</p></div></article></div>");
        if (article.Related.Length > 0)
        {
            body.Append("<section class=\"wiki-topic wiki-related\" aria-labelledby=\"wiki-related-title\"><h2 id=\"wiki-related-title\">Related guides</h2><div class=\"wiki-guide-grid\">");
            foreach (string related in article.Related)
                if (WikiContent.Find(related) is { } next) AppendWikiCard(body, next);
            body.Append("</div></section>");
        }
        body.Append("</div>");
        return Show(c, article.Title, "wiki", body.ToString(), editorTheme: editor);
    }

    private static string WikiAreaNavigation(string area) =>
        "<nav class=\"wiki-area-nav\" aria-label=\"Wiki areas\">" + Link("/wiki", "Wiki home") +
        "<a href=\"/wiki/game\"" + (area == "game" ? " aria-current=\"page\"" : "") + ">Game wiki</a>" +
        "<a href=\"/wiki/editor\"" + (area == "editor" ? " aria-current=\"page\"" : "") + ">Editor wiki</a></nav>";

    private static void AppendWikiAreaCard(StringBuilder body, string area, string title, string eyebrow, string description, string action, string logo) =>
        body.Append("<a class=\"wiki-area-card wiki-area-").Append(area).Append("\" href=\"/wiki/").Append(area).Append("\"><img src=\"")
            .Append(H(AssetUrl(logo))).Append("\" alt=\"\" width=\"56\" height=\"56\"><span class=\"eyebrow\">").Append(H(eyebrow)).Append("</span><h2>")
            .Append(H(title)).Append("</h2><p>").Append(H(description)).Append("</p><span class=\"wiki-read\">").Append(H(action)).Append(" <span aria-hidden=\"true\">→</span></span></a>");

    private static void AppendWikiCard(StringBuilder body, WikiContent.Article article) =>
        body.Append("<a class=\"wiki-guide-card\" href=\"/wiki/").Append(H(article.Slug)).Append("\"><h3>").Append(H(article.Title))
            .Append("</h3><p>").Append(H(article.Summary)).Append("</p><span class=\"wiki-read\">Read guide <span aria-hidden=\"true\">→</span></span></a>");
}
