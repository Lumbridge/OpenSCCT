using System.Linq;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class QuickChatTests
{
    [Fact]
    public void EveryCategoryAndPhraseRequiresTwoChoicesAndHonorsCooldown()
    {
        for (int category = 0; category < 4; category++)
            for (int phrase = 0; phrase < 4; phrase++)
            {
                var state = new QuickChatState();
                Assert.True(state.Open(0));
                Assert.Null(state.Choose(category, .1));
                Assert.True(state.IsOpen);
                Assert.Equal(QuickChatState.Categories[category].Messages[phrase], state.Choose(phrase, .2));
                Assert.False(state.IsOpen);
                Assert.Null(state.Choose(phrase, .3));
                Assert.False(state.Open(.3));
                Assert.True(state.Open(.2 + QuickChatState.CooldownSeconds));
            }
    }

    [Fact]
    public void BackTimeoutAndInvalidChoicesCannotSend()
    {
        var state = new QuickChatState();
        state.Open(0);
        Assert.Null(state.Choose(-1, 0));
        Assert.Null(state.Category);
        state.Choose(1, .1);
        state.Back();
        Assert.True(state.IsOpen);
        Assert.Null(state.Category);
        state.Back();
        Assert.False(state.IsOpen);
        Assert.True(state.Open(.2));
        state.Choose(0, .2);
        Assert.Null(state.Choose(0, .2 + QuickChatState.TimeoutSeconds));
        Assert.False(state.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuickChatIsOptionalAndOfferedAsAnIndependentControllerAction(bool merc)
    {
        ControlAction action = ControlActions.ForRole(merc).Single(a => a.Id == "quickchat");
        Assert.True(ControlActions.Direct(action));
        Assert.Empty(new ActionBindingDraft(DefaultKeyLayout.Create()).Keys(action));
        Assert.DoesNotContain("Action:quickchat", PadSettings.DefaultButtons(merc).Values);
    }
}
