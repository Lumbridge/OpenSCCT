using System.Linq;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The In-Game Menu's rules (spec/hud/in-game-menu.md).</summary>
public sealed class InGameMenuModelTests
{
    private static readonly InGameMenuContext Offline = new(IAmServer: true, InGameMenuNet.None);
    private static readonly InGameMenuContext LanHost = new(IAmServer: true, InGameMenuNet.Lan);
    private static readonly InGameMenuContext LanClient = new(IAmServer: false, InGameMenuNet.Lan);

    [Fact]
    public void PracticeParticipantsCanSwitchTeamDirectlyFromTheMenu()
    {
        var context = LanClient with { Practice = true, MatchBegun = true };
        var menu = new InGameMenuModel();
        menu.Open(context, 0);
        Assert.Contains(menu.Entries, e => e.Button == InGameMenuButton.SwitchTeam);
        Assert.Equal(InGameMenuAction.SwitchTeam, menu.Click(InGameMenuButton.SwitchTeam));
        Assert.False(menu.IsOpen);
        foreach (var denied in new[] { context with { Practice = false }, context with { MercDeathmatch = true },
            context with { GameEnded = true }, context with { Spectator = true }, context with { Tutorial = true } })
            Assert.DoesNotContain(InGameMenuModel.Layout(denied), e => e.Button == InGameMenuButton.SwitchTeam);
    }

    [Fact]
    public void ResignRequiresAnActiveNetworkParticipantAndConfirmation()
    {
        var active = LanClient with { MatchBegun = true, Lives = 3 };
        Assert.True(active.CanResign);
        foreach (var denied in new[] { active with { Net = InGameMenuNet.None }, active with { Tutorial = true },
            active with { MatchBegun = false }, active with { GameEnded = true }, active with { Spectator = true }, active with { Lives = 0 } })
        {
            Assert.False(denied.CanResign);
            Assert.DoesNotContain(InGameMenuModel.Layout(denied), e => e.Button == InGameMenuButton.Resign);
        }
        var m = new InGameMenuModel();
        m.Open(active, 0);
        Assert.Equal(InGameMenuAction.None, m.ConfirmResign());
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.Resign));
        Assert.Equal(InGameMenuPage.ConfirmResign, m.Page);
        m.Cancel();
        Assert.Equal(InGameMenuPage.Main, m.Page);
        Assert.Equal(InGameMenuAction.None, m.ConfirmResign());
        m.Click(InGameMenuButton.Resign);
        Assert.Equal(InGameMenuAction.Resign, m.ConfirmResign());
        Assert.False(m.IsOpen);
        Assert.Equal(InGameMenuAction.None, m.ConfirmResign());
    }

    [Fact]
    public void RoundEndingWhileResignDialogIsOpenRevokesTheAction()
    {
        var active = LanHost with { MatchBegun = true, Lives = 1 };
        var m = new InGameMenuModel();
        m.Open(active, 0);
        m.Click(InGameMenuButton.Resign);
        m.UpdateContext(active with { GameEnded = true });
        Assert.Equal(InGameMenuPage.Main, m.Page);
        Assert.Equal(InGameMenuAction.None, m.ConfirmResign());
        Assert.DoesNotContain(m.Entries, e => e.Button == InGameMenuButton.Resign);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void MissingMapBlocksReadyButNeverBlocksUnready(bool ready, bool mapMatches, bool allowed) =>
        Assert.Equal(allowed, LobbyRoomModel.CanToggleReady(ready, mapMatches));

    private static (InGameMenuButton, float)[] Tops(InGameMenuContext c) =>
        InGameMenuModel.Layout(c).Select(e => (e.Button, e.Top)).ToArray();

    // Change Gear (an OpenSCCT addition) goes under Back; the column keeps the 0.1 spacing, raised by half a step.
    private static void AssertTops((InGameMenuButton Button, float Top)[] expected, (InGameMenuButton Button, float Top)[] actual)
    {
        Assert.Equal(expected.Select(e => e.Button), actual.Select(e => e.Button));
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Top, actual[i].Top, 0.0001f);
        }
    }

    [Fact]
    public void Offline_has_no_return_to_lobby()
    {
        AssertTops(
            [(InGameMenuButton.Back, 0.275f), (InGameMenuButton.ChangeGear, 0.375f), (InGameMenuButton.StrategicMap, 0.475f),
                (InGameMenuButton.Options, 0.575f), (InGameMenuButton.QuitSession, 0.675f)],
            Tops(Offline));
    }

    [Fact]
    public void Network_host_gets_return_to_lobby()
    {
        AssertTops(
            [(InGameMenuButton.Back, 0.225f), (InGameMenuButton.ChangeGear, 0.325f), (InGameMenuButton.StrategicMap, 0.425f),
                (InGameMenuButton.Options, 0.525f), (InGameMenuButton.ReturnToLobby, 0.625f), (InGameMenuButton.QuitSession, 0.725f)],
            Tops(LanHost));
    }

    [Fact]
    public void Network_client_is_laid_out_as_offline()
    {
        Assert.Equal(Tops(Offline), Tops(LanClient));
    }

    [Fact]
    public void Tutorial_hides_the_strategic_map()
    {
        Assert.Equal(
            [(InGameMenuButton.Back, 0.325f), (InGameMenuButton.Options, 0.475f), (InGameMenuButton.QuitSession, 0.625f)],
            Tops(Offline with { Tutorial = true }));
        Assert.Equal(
            [(InGameMenuButton.Back, 0.325f), (InGameMenuButton.Options, 0.425f), (InGameMenuButton.ReturnToLobby, 0.525f), (InGameMenuButton.QuitSession, 0.625f)],
            Tops(LanHost with { Tutorial = true }));
    }

    [Fact]
    public void Captions_are_the_originals()
    {
        Assert.Equal(["Back", "Change Gear", "Strategic Map", "Options", "Return to Lobby", "Quit Session"], InGameMenuModel.Layout(LanHost).Select(e => e.Caption));
        Assert.Equal("In-Game Menu", InGameMenuK.Title);
    }

    [Fact]
    public void Keys_are_ignored_for_half_a_second_then_escape_and_right_mouse_close()
    {
        var m = new InGameMenuModel();
        Assert.Equal(InGameMenuAction.Opened, m.Open(Offline, 10.0));
        Assert.Equal(InGameMenuAction.None, m.Open(Offline, 10.1));
        Assert.Equal(InGameMenuAction.None, m.KeyPressed("Escape", 10.49));
        Assert.True(m.IsOpen);
        Assert.Equal(InGameMenuAction.None, m.KeyPressed("W", 11.0));
        Assert.True(m.IsOpen);
        Assert.Equal(InGameMenuAction.Resume, m.KeyPressed("Escape", 10.5));
        Assert.False(m.IsOpen);

        m.Open(Offline, 20.0);
        Assert.Equal(InGameMenuAction.Resume, m.KeyPressed("RightMouse", 20.6));
    }

    [Fact]
    public void Back_closes_and_options_come_back_to_the_menu()
    {
        var m = new InGameMenuModel();
        m.Open(Offline, 0);
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.Options));
        Assert.Equal(InGameMenuPage.Options, m.Page);

        // Escape belongs to the options page while it is shown.
        Assert.Equal(InGameMenuAction.None, m.KeyPressed("Escape", 5));
        m.OptionsClosed();
        Assert.Equal(InGameMenuPage.Main, m.Page);
        Assert.Equal(InGameMenuAction.Resume, m.Click(InGameMenuButton.Back));
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void Hidden_buttons_do_nothing_and_the_strategic_map_returns_to_the_menu()
    {
        var m = new InGameMenuModel();
        m.Open(Offline, 0);
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.ReturnToLobby));
        Assert.Equal(InGameMenuPage.Main, m.Page);
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.StrategicMap));
        Assert.Equal(InGameMenuPage.StrategicMap, m.Page);
        Assert.Equal(InGameMenuAction.None, m.StrategicMapKey());
        Assert.Equal(InGameMenuAction.None, m.StrategicMapBack());
        Assert.Equal(InGameMenuPage.Main, m.Page);
    }

    [Fact]
    public void Server_quit_asks_its_question_and_ends_the_session_for_everyone()
    {
        var m = new InGameMenuModel();
        m.Open(Offline, 0);
        m.Click(InGameMenuButton.QuitSession);
        Assert.Equal(InGameMenuPage.ConfirmQuit, m.Page);
        Assert.Equal("Leaving the game now will end this session.|Are you sure?", m.Question);
        m.Cancel();
        Assert.Equal(InGameMenuPage.Main, m.Page);
        m.Click(InGameMenuButton.QuitSession);
        Assert.Equal(InGameMenuAction.EndSessionForAll, m.EndSession(1));
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void Client_quit_shows_leaving_then_leaves()
    {
        var m = new InGameMenuModel();
        m.Open(LanClient, 0);
        m.Click(InGameMenuButton.QuitSession);
        Assert.Equal("Are you sure you wish|to quit ?", m.Question);
        Assert.Equal(InGameMenuAction.None, m.EndSession(2.0));
        Assert.Equal(InGameMenuPage.Leaving, m.Page);
        Assert.Equal("Leaving session, please wait ...", m.Question);
        Assert.Equal(InGameMenuAction.None, m.Tick(2.59));
        Assert.Equal(InGameMenuAction.LeaveSession, m.Tick(2.6));
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void Quit_game_and_escape_on_the_dialog()
    {
        var m = new InGameMenuModel();
        m.Open(LanClient, 0);
        m.Click(InGameMenuButton.QuitSession);
        Assert.Equal(InGameMenuAction.None, m.KeyPressed("Escape", 3));
        Assert.Equal(InGameMenuPage.Main, m.Page);
        m.Click(InGameMenuButton.QuitSession);
        Assert.Equal(InGameMenuAction.QuitGame, m.QuitGame());
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void Host_returns_everyone_to_the_lobby_after_confirming()
    {
        var m = new InGameMenuModel();
        m.Open(LanHost, 0);
        m.Click(InGameMenuButton.ReturnToLobby);
        Assert.Equal(InGameMenuPage.ConfirmLobby, m.Page);
        Assert.Equal("Are you sure you wish|to return to lobby?", m.Question);
        m.Cancel();
        Assert.Equal(InGameMenuPage.Main, m.Page);
        m.Click(InGameMenuButton.ReturnToLobby);
        Assert.Equal(InGameMenuAction.ReturnToLobby, m.ConfirmLobby());
        Assert.False(m.IsOpen);
    }
}
