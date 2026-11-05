using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;

namespace OpenScctGame.UI;

/// <summary>The same read-only round details opened from results, lobby and the match library.</summary>
public partial class RoundScoreboardDialog : Window
{
    public static RoundScoreboardDialog Open(Node owner, RoundScoreboard scoreboard, int localParticipantId = -1,
        Action? watch = null, string replayStatus = "No replay available", IReadOnlyList<MatchTimelineEvent>? timeline = null,
        OpenScct.Server.MapCatalog? maps = null)
    {
        var window = new RoundScoreboardDialog
        {
            Name = "RoundScoreboardDialog", Title = "Match scoreboard", Transient = true, Exclusive = true,
            MinSize = new Vector2I(320, 240), Theme = UiStyle.MenuTheme,
        };
        owner.AddChild(window);
        window.CloseRequested += window.QueueFree;
        window.AddChild(UiStyle.MenuBackdrop());
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        window.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        string outcome = scoreboard.Partial ? "Incomplete" : scoreboard.Winner == 0 ? "Spies win" : scoreboard.Winner == 1 ? "Mercenaries win" : "Round stopped";
        CareerPanels.Text(column, "Title", MapTitles.WithCode(maps ?? MapImages.Default, scoreboard.Map) + " · " + outcome + (scoreboard.Practice ? " · Practice" : ""), size: 22);
        CareerPanels.Text(column, "Metadata", $"{scoreboard.StartedUtc.ToLocalTime():dd MMM yyyy HH:mm} · {scoreboard.Server} · {RoundScoreboardModel.Clock(scoreboard.ElapsedSeconds)}", dim: true, size: 14);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddChild(scroll);
        scroll.AddChild(new CareerScroll());
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(content);
        content.AddChild(new RoundScoreboardView { Scoreboard = scoreboard, LocalParticipantId = localParticipantId, Detailed = true, Maps = maps,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var events = CareerPanels.Card(content, "Timeline", "Match timeline");
        if (timeline is null) CareerPanels.Text(events, "Unavailable", "Timeline expired or unavailable for this match.", dim: true, size: 14);
        else
        {
            int shown = 0;
            var more = UiStyle.MenuButton("Show more events", () => { }, "MoreEvents");
            void Append()
            {
                int end = Math.Min(shown + 100, timeline.Count);
                while (shown < end)
                {
                    var entry = timeline[shown++];
                    CareerPanels.Text(events, "Event", $"{entry.Seconds / 60:00}:{entry.Seconds % 60:00}  {entry.Text}", size: 14);
                }
                more.Visible = shown < timeline.Count;
                events.MoveChild(more, events.GetChildCount() - 1);
            }
            events.AddChild(more);
            more.Pressed += Append;
            Append();
        }
        var actions = new HBoxContainer();
        column.AddChild(actions);
        CareerPanels.Text(actions, "ReplayStatus", replayStatus, dim: true, size: 14).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (watch is not null) actions.AddChild(UiStyle.MenuButton("Watch demo", () => { window.QueueFree(); watch(); }, "Watch"));
        var close = UiStyle.MenuButton("Close", window.QueueFree, "Close");
        actions.AddChild(close);
        if (DisplayServer.GetName() == "headless")
        {
            // Popup clamps against the headless display's empty screen rectangle, even for embedded windows.
            // Assign the viewport size after showing it so CI exercises the same layout as the owning window.
            Vector2I available = owner.GetWindow().Size;
            var wanted = new Vector2I(Math.Min(1380, (int)(available.X * .94f)), Math.Min(820, (int)(available.Y * .94f)));
            window.Popup();
            window.Size = wanted;
            window.Position = (available - wanted) / 2;
        }
        else window.PopupCenteredClamped(new Vector2I(1380, 820), 0.94f);
        close.GrabFocus();
        return window;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { SetInputAsHandled(); QueueFree(); }
    }
}
