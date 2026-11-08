using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class PlayerPronounsTests
{
    [Theory]
    [InlineData(null, "their", "themselves")]
    [InlineData("", "their", "themselves")]
    [InlineData("they/them", "their", "themselves")]
    [InlineData("she/her", "her", "herself")]
    [InlineData("he/him", "his", "himself")]
    public void PossessiveAndReflexiveFormsFollowTheSelection(string? choice, string possessive, string reflexive)
    {
        Assert.Equal(possessive, PlayerPronouns.For(choice).Possessive);
        Assert.Equal(reflexive, PlayerPronouns.For(choice).Reflexive);
    }

    [Theory]
    [InlineData("she/her?Team=1")]
    [InlineData("it/its")]
    [InlineData("xe/xem")]
    [InlineData("ze/hir")]
    [InlineData("<b>she</b>/her")]
    [InlineData("ey/em/eir/eirs/emself")]
    [InlineData("ey/em/eir/eirs/")]
    [InlineData("ey/em/eir/eirs/em self")]
    [InlineData("ey/em/eir/eirs/emself/extra")]
    public void InvalidSelectionsCannotBecomeLoginOptionsOrMessageMarkup(string value)
    {
        Assert.False(PlayerPronouns.TryNormalize(value, out _));
        Assert.Equal("their", PlayerPronouns.For(value).Possessive);
    }

    [Fact]
    public void GenericStockTextIsNeutralAndUnrelatedTextIsPreserved()
    {
        Assert.Equal("used their last life", NeutralPlayerText.Apply("SBase.SGameMessages", "TeammateNoLivesLeft", -1, "used his last life"));
        Assert.Equal("eliminated themselves", NeutralPlayerText.Apply("SBase.SDeathMessage", "SuicideString", -1, "killed himself"));
        Assert.Equal("His Name", NeutralPlayerText.Apply("Test.Player", "Name", -1, "His Name"));
        Assert.DoesNotContain(" him", NeutralPlayerText.Apply("GUI.Tutorials_Tuto", "SPY_TutoExpli", 7, "grab him"));
        Assert.Equal("she/her", PlayerPronouns.Normalize(" SHE/HER "));
    }
}
