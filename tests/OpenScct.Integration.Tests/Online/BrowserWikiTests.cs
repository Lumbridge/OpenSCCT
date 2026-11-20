using System;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserWikiTests
{
    [Theory]
    [InlineData("spy-grab")]
    [InlineData("vision-cancelling")]
    [InlineData("vision-cancelling-demo")]
    public void GuidesDescribeDesktopExercises(string slug)
    {
        var article = WikiContent.Find(slug);
        Assert.NotNull(article);
        string html = string.Join('\n', Array.ConvertAll(article.Sections, section => section.Html));
        Assert.Contains("OpenSCCT Godot", html);
        Assert.DoesNotContain("/play/", html);
        Assert.DoesNotContain("data-training-frame", html);
        Assert.DoesNotContain("browser-training.js", html);
        Assert.DoesNotContain("grab-window.js", html);
        Assert.DoesNotContain("vision-cancelling.js", html);
        Assert.DoesNotContain("Simplified movement", html);
    }
}
