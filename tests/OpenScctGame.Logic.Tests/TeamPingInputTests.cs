using System.Collections.Generic;
using System.Linq;
using OpenScct.Server;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class TeamPingInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TeamPingIsAvailableForBothRolesWithoutADefault(bool merc)
    {
        ControlAction action = ControlActions.ForRole(merc).Single(action => action.Id == ControlActions.PingId);
        Assert.Empty(new ActionBindingDraft(DefaultKeyLayout.Create()).Keys(action));
        Assert.DoesNotContain(ControlActions.PadPrefix + action.Id, PadSettings.DefaultButtons(merc).Values);
        Assert.Contains(ControlActions.VirtualActions(ControlActions.VirtualPrefix + "Back:" + action.Id),
            binding => binding.Kind == KeyActionKind.Command && binding.Name == ControlActions.PingCommand);
    }

    [Fact]
    public void RoleBindingsKeepTheirOwnSavedKeysAndControllerButtons()
    {
        ControlAction ping = ControlActions.All.Single(action => action.Id == ControlActions.PingId);
        var spy = new ActionBindingDraft(DefaultKeyLayout.Create());
        var merc = new ActionBindingDraft(DefaultKeyLayout.Create());
        spy.Add(ping, "MiddleMouse");
        merc.Add(ping, "P");

        Assert.Equal(["MiddleMouse"], new ActionBindingDraft(KeyLayout.Parse(spy.Layout.ToIni())).Keys(ping));
        Assert.Equal(["P"], new ActionBindingDraft(KeyLayout.Parse(merc.Layout.ToIni())).Keys(ping));

        var settings = new GameSettings();
        string actionId = ControlActions.PadPrefix + ping.Id;
        settings.Pad.SpyButtons["A"] = actionId;
        settings.Pad.MercButtons["X"] = actionId;
        var saved = GameSettings.FromJson(settings.ToJson());
        Assert.Equal(actionId, saved.Pad.SpyButtons["A"]);
        Assert.Equal(actionId, saved.Pad.MercButtons["X"]);
        Assert.NotEqual(actionId, saved.Pad.SpyButtons.GetValueOrDefault("X"));
        Assert.NotEqual(actionId, saved.Pad.MercButtons.GetValueOrDefault("A"));
    }

    [Fact]
    public void TapQueuesOneGenericPingWithoutOpeningWheel()
    {
        var input = Ready(out var sent);
        input.WheelOpened += () => Assert.Fail("A tap opened the wheel.");
        input.Press("R", 10, merc: false);
        input.Press("MiddleMouse", 30, merc: false);
        input.Release("MiddleMouse", 60);
        input.Release("R", 80);
        Assert.Empty(sent);
        input.Advance(80, inputAvailable: true);
        input.Advance(90, inputAvailable: true);
        Assert.Equal([(TeamPingKind.Generic, false)], sent);
    }

    [Theory]
    [InlineData(false, 0f, -1f, TeamPingKind.Watch)]
    [InlineData(false, 1f, 0f, TeamPingKind.Check)]
    [InlineData(false, 0f, 1f, TeamPingKind.CoopBoost)]
    [InlineData(false, -1f, 0f, TeamPingKind.Mine)]
    [InlineData(true, 0f, -1f, TeamPingKind.Watch)]
    [InlineData(true, 1f, 0f, TeamPingKind.Check)]
    [InlineData(true, 0f, 1f, null)]
    [InlineData(true, -1f, 0f, null)]
    [InlineData(false, 0f, 0f, null)]
    public void HoldUsesRoleChoicesAndQueuesTheSelectedKind(bool merc, float x, float y, TeamPingKind? kind)
    {
        var input = Ready(out var sent);
        int opens = 0;
        input.WheelOpened += () => opens++;
        input.Press("MiddleMouse", 0, merc);
        input.Advance(TeamPingInput.HoldMilliseconds - 1, inputAvailable: true);
        Assert.False(input.WheelOpen);
        input.Advance(TeamPingInput.HoldMilliseconds, inputAvailable: true);
        input.Advance(400, inputAvailable: true);
        Assert.True(input.WheelOpen);
        Assert.Equal(1, opens);
        input.StickDirection(x, y);
        Assert.Equal(kind, input.Selection);
        input.Release("MiddleMouse", 401);
        Assert.False(input.WheelOpen);
        input.Advance(402, inputAvailable: true);
        if (kind is { } selected) Assert.Equal([(selected, true)], sent);
        else Assert.Empty(sent);
    }

    [Fact]
    public void MouseMotionAccumulatesAndCanReturnToCenter()
    {
        var input = Ready(out var sent);
        input.Press("R", 0, merc: false);
        input.Advance(TeamPingInput.HoldMilliseconds, inputAvailable: true);
        input.MouseMotion(-12, 0);
        Assert.Null(input.Selection);
        input.MouseMotion(-12, 0);
        Assert.Equal(TeamPingKind.Mine, input.Selection);
        input.MouseMotion(24, 0);
        Assert.Null(input.Selection);
        input.Release("R", 400);
        input.Advance(400, inputAvailable: true);
        Assert.Empty(sent);
    }

    [Fact]
    public void CancellingOrChangingContextDiscardsHeldAndQueuedReleases()
    {
        var input = Ready(out var sent);
        object pawn = new();
        int team = 0;
        input.Context = () => (pawn, team);
        input.Press("R", 0, merc: false);
        input.Release("R", 50);
        team++;
        input.Advance(55, inputAvailable: true);
        Assert.Empty(sent);

        input.Press("Pad", 100, merc: false);
        input.Advance(400, inputAvailable: true);
        input.StickDirection(0, 1);
        input.CancelSource("Pad");
        input.Release("Pad", 401);
        input.Advance(401, inputAvailable: true);
        Assert.False(input.WheelOpen);
        Assert.Empty(sent);

        input.Press("R", 500, merc: false);
        input.Release("R", 510);
        input.Advance(520, inputAvailable: false);
        input.Advance(530, inputAvailable: true);
        Assert.Empty(sent);
    }

    private static TeamPingInput Ready(out List<(TeamPingKind Kind, bool Locked)> sent)
    {
        var input = new TeamPingInput { Context = () => 0 };
        var events = new List<(TeamPingKind, bool)>();
        input.Submitted += (kind, locked) => events.Add((kind, locked));
        sent = events;
        return input;
    }
}
