using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The Lobby Room page's rules (spec/game/lobby-and-match-creation.md rules 11 to 18).</summary>
public sealed class MenuLobbyRoomTests
{
    [Fact]
    public void TheMainButtonIsLaunchForTheHostAndReadyForAClient()
    {
        Assert.Equal("Launch", LobbyRoomModel.MainButton(host: true, ready: false, countingDown: false));
        Assert.Equal("Cancel", LobbyRoomModel.MainButton(host: true, ready: false, countingDown: true));
        Assert.Equal("Ready", LobbyRoomModel.MainButton(host: false, ready: false, countingDown: false));
        Assert.Equal("Not Ready", LobbyRoomModel.MainButton(host: false, ready: true, countingDown: true));
    }

    [Fact]
    public void ChangedValuesBlinkForThreeSecondsThreeTimesASecond()
    {
        var room = new LobbyRoomModel();
        var setup = new LobbySetup { Map = "SteeE", TimeLimit = 10 };
        Assert.Empty(room.Show(setup, 0.0));
        Assert.Empty(room.Show(setup, 1.0));
        Assert.Equal([SessionRow.VoiceChat, SessionRow.TimeLimit],
            room.Show(setup with { TimeLimit = 5, VoiceMode = EVoiceChatMode.VCM_Open }, 10.0));
        Assert.True(room.Blinking(SessionRow.TimeLimit, 12.9));
        Assert.False(room.Blinking(SessionRow.TimeLimit, 13.0));
        Assert.False(room.Blinking(SessionRow.Map, 11.0));
        Assert.True(room.Lit(SessionRow.TimeLimit, 10.0));
        Assert.False(room.Lit(SessionRow.TimeLimit, 10.4));
        Assert.True(room.Lit(SessionRow.TimeLimit, 10.7));
        Assert.True(room.Lit(SessionRow.Map, 10.4));
    }

    [Fact]
    public void ALineWithinASecondOfTheLastIsNotSent()
    {
        var room = new LobbyRoomModel();
        Assert.True(room.TrySendChat(5.0));
        Assert.False(room.TrySendChat(5.5));
        Assert.True(room.TrySendChat(6.0));
        Assert.Equal("(Spam delay, 1 second)", LobbyRoomModel.SpamText);
    }

    [Fact]
    public void VolumesReadAsTheVoiceOverlayWritesThem() =>
        Assert.Equal("Sam - volume 40%", LobbyRoomModel.VolumeText("Sam", 0.4f));
}
