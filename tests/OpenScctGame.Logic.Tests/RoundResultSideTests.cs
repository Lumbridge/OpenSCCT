using OpenScct.Server.Online;
using OpenScctGame.Online;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The end banner's side: the local player's row of the round's results, not his lobby side.</summary>
public sealed class RoundResultSideTests
{
    // Game two of a Revenge pair: the host chose the spies in the lobby, the round swapped him to the mercs, and the
    // mercs won on time. The lobby still lists his chosen side (0).
    private static readonly MatchResults RevengeGameTwo = new()
    {
        Map = "TestBox",
        WinningTeam = 1,
        WinningReason = 1,
        Rows = [new ResultRow(0, "Host", 1, 10, 0, 0, 3), new ResultRow(1, "Guest", 0, 0, 0, 0, 3)],
    };

    [Fact]
    public void TheSwappedHostIsTheWinningMercInRevengeGameTwo()
    {
        Assert.Equal(1, RoundResultSide.LocalTeam(RevengeGameTwo, localId: 0, lobbyTeam: 0));
    }

    [Fact]
    public void TheSwappedGuestIsTheLosingSpyInRevengeGameTwo()
    {
        Assert.Equal(0, RoundResultSide.LocalTeam(RevengeGameTwo, localId: 1, lobbyTeam: 1));
    }

    [Fact]
    public void ASpectatorWithNoRowKeepsHisLobbySide()
    {
        Assert.Equal(1, RoundResultSide.LocalTeam(RevengeGameTwo, localId: 7, lobbyTeam: 1));
    }
}
