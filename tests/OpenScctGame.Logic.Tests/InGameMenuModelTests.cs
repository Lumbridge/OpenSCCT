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

    private static (InGameMenuButton, float)[] Tops(InGameMenuContext c) =>
        InGameMenuModel.Layout(c).Select(e => (e.Button, e.Top)).ToArray();

    [Fact]
    public void Offline_has_no_return_to_lobby()
    {
        Assert.Equal(
            [(InGameMenuButton.Back, 0.325f), (InGameMenuButton.StrategicMap, 0.425f), (InGameMenuButton.Options, 0.525f), (InGameMenuButton.QuitSession, 0.625f)],
            Tops(Offline));
    }

    [Fact]
    public void Network_host_gets_return_to_lobby()
    {
        Assert.Equal(
            [(InGameMenuButton.Back, 0.275f), (InGameMenuButton.StrategicMap, 0.375f), (InGameMenuButton.Options, 0.475f),
                (InGameMenuButton.ReturnToLobby, 0.575f), (InGameMenuButton.QuitSession, 0.675f)],
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
        Assert.Equal(["Back", "Strategic Map", "Options", "Return to Lobby", "Quit Session"], InGameMenuModel.Layout(LanHost).Select(e => e.Caption));
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
    public void Hidden_and_unported_buttons_do_nothing()
    {
        var m = new InGameMenuModel();
        m.Open(Offline, 0);
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.ReturnToLobby));
        Assert.Equal(InGameMenuAction.None, m.Click(InGameMenuButton.StrategicMap));
        Assert.Equal(InGameMenuPage.Main, m.Page);
        Assert.False(InGameMenuModel.StrategicMapAvailable);
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
