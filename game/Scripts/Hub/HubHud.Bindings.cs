using System.Collections.Generic;
using System.Text;
using OpenScct.Server.Hub;
using OpenScctGame.Controls;

namespace OpenScctGame.Hub;

public partial class HubHud
{
    private KeyLayout? _hintLayout;
    private readonly Dictionary<string, string> _keyHints = new();
    private string KeyHint(string command) => _keyHints.GetValueOrDefault(command, "Unbound");

    private void UpdateBindingHints()
    {
        var layout = Round?.Controls?.Bindings?.Layout;
        if (layout is null || ReferenceEquals(layout, _hintLayout)) return;
        _hintLayout = layout;
        foreach (var binding in HubBindings.All) _keyHints[binding.Command] = HubBindings.Hint(layout, binding.Command);
        foreach (string command in new[] { "Talk", "TeamTalk", "ShowMenu" }) _keyHints[command] = HubBindings.Hint(layout, command);
        if (_help is not null) _help.Text = $"Hub   {KeyHint("HubTravel")} travel   {KeyHint("HubSpy")} spy   {KeyHint("HubMerc")} merc   " +
            $"{KeyHint("HubPlaza")} plaza   {KeyHint("HubEmotes")} emotes   {KeyHint("HubUse")} ready/use   {KeyHint("HubCancel")} stop   " +
            $"{KeyHint("HubWatch")} watch   {KeyHint("HubPlayers")} players   {KeyHint("HubArmoury")} armoury   " +
            $"{KeyHint("Talk")} chat   {KeyHint("TeamTalk")} team chat   {KeyHint("ShowMenu")} menu";
        if (_travelButton is not null) _travelButton.Text = $"Travel  [{KeyHint("HubTravel")}]";
        if (_emoteMenu is not null)
        {
            var text = new StringBuilder("EMOTES\n");
            for (int i = 0; i < HubEmotes.All.Count; i++) text.Append(KeyHint("HubEmote" + (i + 1))).Append("  ").Append(HubEmotes.All[i]).Append('\n');
            text.Append(KeyHint("HubEmotes")).Append(" / ").Append(KeyHint("ShowMenu")).Append("  close");
            _emoteMenu.Text = text.ToString();
        }
    }
}
