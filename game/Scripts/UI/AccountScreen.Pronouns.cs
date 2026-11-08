using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

public partial class AccountScreen
{
    private Task<AccountInfo>? _savePronouns;
    private string? _pronounsDraft;
    private long _pronounsAccountId;
    private long _pronounsDraftAccountId;
    private string _pronounsToken = "";

    private void BuildPronouns(AccountInfo? info)
    {
        long accountId = info?.Id ?? AccountSettings.Current.AccountId;
        if (_pronounsDraftAccountId != accountId) _pronounsDraft = null;
        _pronounsDraftAccountId = accountId;
        string selection = _pronounsDraft ?? PlayerPronouns.Normalize(info?.Pronouns ?? AccountSettings.Current.Pronouns);
        int selected = PlayerPronouns.Presets.ToList().IndexOf(selection);
        VBoxContainer panel = CareerPanels.Card(_content!, "Pronouns", "Pronouns");
        CareerPanels.Text(panel, "PronounsHelp", "Choose the pronouns used in messages about you. They/them is the default.", dim: true, size: 14);
        var choice = new OptionButton { Name = "PronounsChoice", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string preset in PlayerPronouns.Presets) choice.AddItem(preset == PlayerPronouns.Default ? preset + " (default)" : preset);
        choice.Select(selected);
        panel.AddChild(choice);
        Label example = CareerPanels.Text(panel, "PronounsExample",
            (info?.Name ?? AccountSettings.Current.Name) + " used " + PlayerPronouns.For(selection).Possessive + " last life", size: 14);
        choice.ItemSelected += _ =>
        {
            _pronounsDraft = PlayerPronouns.Presets[choice.Selected];
            example.Text = (info?.Name ?? AccountSettings.Current.Name) + " used " + PlayerPronouns.For(_pronounsDraft).Possessive + " last life";
        };
        Button save = ActionButton("Save pronouns", SavePronouns, "SavePronouns");
        save.Disabled = _savePronouns is not null;
        Actions(panel, "PronounsActions").AddChild(save);
    }

    private void SavePronouns()
    {
        if (Preview is not null || _savePronouns is not null) return;
        if (!PlayerPronouns.TryNormalize(_pronounsDraft ?? _service?.Account?.Pronouns ?? AccountSettings.Current.Pronouns, out string selection))
        {
            ShowStatus("Choose a pronoun set from the list.");
            return;
        }
        _pronounsAccountId = AccountSettings.Current.AccountId;
        _pronounsToken = AccountSettings.Current.Token;
        _savePronouns = AccountService.Call(c => c.SetPronounsAsync(selection));
        ShowStatus("Saving pronouns…");
        if (_content?.FindChild("SavePronouns", true, false) is Button button) button.Disabled = true;
    }

    private void ProcessPronouns()
    {
        if (_savePronouns is not { IsCompleted: true } save) return;
        _savePronouns = null;
        if (!AccountSettings.Current.SignedIn || AccountSettings.Current.AccountId != _pronounsAccountId ||
            AccountSettings.Current.Token != _pronounsToken) return;
        if (_content?.FindChild("SavePronouns", true, false) is Button button) button.Disabled = false;
        if (save.IsCompletedSuccessfully)
        {
            _pronounsDraft = null;
            _service!.Update(save.Result);
            ShowStatus("Pronouns saved.");
        }
        else if (AccountService.IsSignedOutError(save)) SignedOutByMaster();
        else ShowStatus(AccountService.ErrorOf(save));
    }
}
