using OpenScctGame.Options;
using Xunit;
namespace OpenScctGame.Logic.Tests;
public sealed class HudEditorSettingsTests
{
    [Fact] public void OldProfilesKeepOriginalHud() { var s = GameSettings.FromJson("{}"); Assert.Empty(s.HudLayout.Spy); Assert.Empty(s.HudLayout.Merc); }
    [Fact] public void LayoutsPersistAndDraftIsIndependent()
    {
        var s = new GameSettings(); s.HudLayout.Element(false, "Vie").X = .2f;
        s.HudLayout.Element(true, "Ammo").Visible = false;
        var draft = s.Clone(); Assert.Equal(.2f, draft.HudLayout.Spy["Vie"].X); Assert.False(draft.HudLayout.Merc["Ammo"].Visible);
        draft.HudLayout.Spy["Vie"].X = 0; Assert.Equal(.2f, s.HudLayout.Spy["Vie"].X);
    }
    [Theory]
    [InlineData("Scoreboard")]
    [InlineData("Chat")]
    [InlineData("Activity")]
    [InlineData("Location")]
    [InlineData("Fps")]
    [InlineData("Network")]
    public void AddedGroupsPersistIndependently(string id)
    {
        var s = new GameSettings(); s.HudLayout.Element(false, id).Scale = 1.5f;
        s.HudLayout.Element(true, id).Visible = false;
        var loaded = s.Clone(); Assert.Equal(1.5f, loaded.HudLayout.Spy[id].Scale); Assert.False(loaded.HudLayout.Merc[id].Visible);
    }
    [Fact] public void InvalidValuesAndUnknownElementsAreSanitized()
    {
        var s = new HudEditorSettings(); var e = s.Element(false, "Vie"); e.X = float.NaN; e.Scale = -20; e.Opacity = 500;
        s.Element(false, "Flash"); s.Clamp(); Assert.Equal(0, e.X); Assert.Equal(.25f, e.Scale); Assert.Equal(1, e.Opacity); Assert.False(s.Spy.ContainsKey("Flash"));
        Assert.Empty(HudEditorSettings.Parse("{\"Spy\":null,\"Merc\":null}").Spy);
    }
}
