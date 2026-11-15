using System;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserWikiTests
{
    [Theory]
    [InlineData("spy-grab")]
    [InlineData("vision-cancelling")]
    public void LessonsEmbedTheRealGameOnDemand(string slug)
    {
        var article = WikiContent.Find(slug);
        Assert.NotNull(article);
        string html = string.Join('\n', Array.ConvertAll(article.Sections, section => section.Html));
        Assert.Contains("/play/?lesson=" + slug, html);
        Assert.Contains("data-training-frame hidden", html);
        Assert.Contains("browser-training.js", html);
        Assert.DoesNotContain("grab-window.js", html);
        Assert.DoesNotContain("vision-cancelling.js", html);
        Assert.DoesNotContain("Simplified movement", html);
    }
}
