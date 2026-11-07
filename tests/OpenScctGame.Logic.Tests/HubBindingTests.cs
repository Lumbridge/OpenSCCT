using System.Linq;
using OpenScctGame.Controls;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class HubBindingTests
{
    [Fact]
    public void LegacyProfilesGainEveryHubActionWithoutLosingExistingCommands()
    {
        var layout = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), "[Engine.Input]\nIK_U=CustomReady 7", "Gadget");
        foreach (var binding in HubBindings.All)
        {
            Assert.Contains(layout.Resolve(binding.Key), a => a.Name == binding.Command);
            var action = Assert.Single(ControlActions.All, a => a.Id == binding.Id);
            Assert.Equal("Hub", action.Category);
            Assert.True(ControlActions.Direct(action));
        }
        Assert.Contains(layout.Resolve("U"), a => a.Name == "CustomReady" && a.Arg == 7);
        Assert.DoesNotContain(layout.Resolve("U"), a => a.Name == "HubTravel");
        Assert.Equal("F4", HubBindings.Hint(layout, "HubTravel"));
    }

    [Fact]
    public void RebindingToMouseChordAndUnbindingPersistWithoutChangingSharedGameplayKeys()
    {
        var draft = new ActionBindingDraft(DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, "Gadget"));
        var travel = ControlActions.All.Single(a => a.Id == "hub_travel");
        draft.Remove(travel);
        draft.Assign(travel, "Ctrl+MiddleMouse");
        draft.Remove(ControlActions.All.Single(a => a.Id == "hub_emotes"));
        var layout = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), draft.Layout.ToIni(), "Gadget");
        Assert.Equal("Ctrl+MiddleMouse", HubBindings.Hint(layout, "HubTravel"));
        Assert.DoesNotContain(layout.Resolve("F4"), a => a.Name == "HubTravel");
        Assert.Contains(layout.Resolve("F4"), a => a.Name == "DisplayHud");
        Assert.Equal("Unbound", HubBindings.Hint(layout, "HubEmotes"));
        Assert.Contains(layout.Resolve("G"), a => a.Name == "SwapWeapon");
        Assert.Equal(draft.Layout.ToIni(), layout.ToIni());
    }
}
