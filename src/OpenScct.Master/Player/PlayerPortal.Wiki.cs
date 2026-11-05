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
        Get(app, "/wiki/{slug}", WikiArticle);
    }

    private IResult WikiIndex(HttpContext c)
    {
        string query = Query(c, "q", 96);
        string category = Query(c, "category", 24);
        if (!WikiContent.Categories.Any(x => x.Id == category)) category = string.Empty;
        WikiContent.Article[] articles = WikiContent.Search(query, category).ToArray();
        var body = new StringBuilder("<div class=\"wiki-page\">")
            .Append(PageHeader("FIELD GUIDE", "OpenSCCT wiki", "Learn the game, find an answer and build your next map. Guides for players, hosts and map makers, all here on the site."))
            .Append("<form class=\"wiki-search\" role=\"search\" method=\"get\" action=\"/wiki\"><label for=\"wiki-query\">Find a guide</label>")
            .Append("<div class=\"wiki-search-fields\"><input type=\"search\" id=\"wiki-query\" name=\"q\" maxlength=\"96\" placeholder=\"Try cloak, hosting or lighting\" value=\"").Append(H(query)).Append("\">")
            .Append("<label class=\"wiki-category-label\" for=\"wiki-category\">Topic</label><select id=\"wiki-category\" name=\"category\"><option value=\"\">All topics</option>");
        foreach (WikiContent.Category item in WikiContent.Categories)
            body.Append("<option value=\"").Append(item.Id).Append('"').Append(item.Id == category ? " selected" : "").Append('>').Append(H(item.Title)).Append("</option>");
        body.Append("</select><button class=\"button\" type=\"submit\">Search guides</button></div></form>");

        if (query.Length == 0 && category.Length == 0)
        {
            body.Append("<section class=\"wiki-start\" aria-labelledby=\"wiki-start-title\"><div><p class=\"eyebrow\">FIRST TIME HERE?</p><h2 id=\"wiki-start-title\">Start with your first game</h2>")
                .Append("<p>Install the launcher, learn a role and join a lobby. Map makers can follow the editor path from a first room to a tested build.</p></div><div class=\"button-row\">")
                .Append(Link("/wiki/getting-started", "Player quick start →", "button secondary"))
                .Append(Link("/wiki/editor-getting-started", "Editor quick start →", "text-link")).Append("</div></section>")
                .Append("<nav class=\"wiki-topic-nav\" aria-label=\"Wiki topics\">");
            foreach (WikiContent.Category item in WikiContent.Categories)
                body.Append(Link("#wiki-" + item.Id, item.Title));
            body.Append("</nav>");
        }
        else
        {
            body.Append("<div class=\"wiki-results-heading\"><p role=\"status\">").Append(N(articles.Length)).Append(articles.Length == 1 ? " guide" : " guides");
            if (query.Length > 0) body.Append(" matching “").Append(H(query)).Append('”');
            body.Append("</p>").Append(Link("/wiki", "Clear filters", "text-link")).Append("</div>");
        }

        if (articles.Length == 0)
            body.Append(Empty("No guides found", "Try a shorter search, a different word or All topics."));
        foreach (WikiContent.Category item in WikiContent.Categories)
        {
            WikiContent.Article[] group = articles.Where(a => a.Category == item.Id).ToArray();
            if (group.Length == 0) continue;
            body.Append("<section class=\"wiki-topic\" id=\"wiki-").Append(item.Id).Append("\" aria-labelledby=\"wiki-topic-").Append(item.Id)
                .Append("\"><div class=\"section-heading\"><div><h2 id=\"wiki-topic-").Append(item.Id).Append("\">").Append(H(item.Title)).Append("</h2><p>")
                .Append(H(item.Description)).Append("</p></div><span class=\"wiki-count\">").Append(N(group.Length)).Append(group.Length == 1 ? " guide" : " guides").Append("</span></div><div class=\"wiki-guide-grid\">");
            foreach (WikiContent.Article article in group) AppendWikiCard(body, article);
            body.Append("</div></section>");
        }
        body.Append("<section class=\"wiki-help panel\"><h2>Still need a hand?</h2><p>Find troubleshooting steps, send a bug report or help improve a guide.</p><div class=\"button-row\">")
            .Append(Link("/wiki/troubleshooting", "Troubleshooting →", "text-link"))
            .Append(Link("/help", "Help & feedback →", "text-link")).Append("</div></section></div>");
        return Show(c, "Wiki", "wiki", body.ToString());
    }

    private IResult WikiArticle(HttpContext c)
    {
        string slug = c.Request.RouteValues["slug"]?.ToString() ?? string.Empty;
        if (WikiContent.Find(slug) is not { } article)
            return Show(c, "Guide not found", "wiki", PageHeader("WIKI", "Guide not found", "That guide does not exist. Browse the wiki or search for the topic you need.") +
                Link("/wiki", "Browse all guides →", "button secondary"), status: 404);
        WikiContent.Category category = WikiContent.Categories.Single(x => x.Id == article.Category);
        var body = new StringBuilder("<div class=\"wiki-page\"><nav class=\"wiki-breadcrumb\" aria-label=\"Breadcrumb\">")
            .Append(Link("/wiki", "Wiki")).Append("<span aria-hidden=\"true\">/</span>")
            .Append(Link("/wiki?category=" + U(category.Id), category.Title)).Append("</nav>")
            .Append(PageHeader(category.Title.ToUpperInvariant(), article.Title, article.Summary))
            .Append("<div class=\"wiki-reading-layout\"><aside class=\"wiki-contents\" aria-label=\"On this page\"><p class=\"eyebrow\">ON THIS PAGE</p><nav><ol>");
        foreach (WikiContent.Section section in article.Sections)
            body.Append("<li>").Append(Link("#" + section.Id, section.Title)).Append("</li>");
        body.Append("</ol></nav>").Append(Link("/wiki", "← All guides", "text-link")).Append("</aside><article class=\"wiki-article\" aria-label=\"").Append(H(article.Title)).Append("\">");
        foreach (WikiContent.Section section in article.Sections)
            body.Append("<section id=\"").Append(H(section.Id)).Append("\" aria-labelledby=\"heading-").Append(H(section.Id)).Append("\"><h2 id=\"heading-")
                .Append(H(section.Id)).Append("\">").Append(H(section.Title)).Append("</h2>").Append(section.Html).Append("</section>");
        body.Append("<div class=\"wiki-article-feedback\"><p>Something missing or out of date? ")
            .Append(Link("/help", "Help improve this guide →", "text-link")).Append("</p></div></article></div>");
        if (article.Related.Length > 0)
        {
            body.Append("<section class=\"wiki-topic wiki-related\" aria-labelledby=\"wiki-related-title\"><h2 id=\"wiki-related-title\">Keep reading</h2><div class=\"wiki-guide-grid\">");
            foreach (string related in article.Related)
                if (WikiContent.Find(related) is { } next) AppendWikiCard(body, next);
            body.Append("</div></section>");
        }
        body.Append("</div>");
        return Show(c, article.Title, "wiki", body.ToString());
    }

    private static void AppendWikiCard(StringBuilder body, WikiContent.Article article) =>
        body.Append("<a class=\"wiki-guide-card\" href=\"/wiki/").Append(H(article.Slug)).Append("\"><h3>").Append(H(article.Title))
            .Append("</h3><p>").Append(H(article.Summary)).Append("</p><span class=\"wiki-read\">Read guide <span aria-hidden=\"true\">→</span></span></a>");
}
