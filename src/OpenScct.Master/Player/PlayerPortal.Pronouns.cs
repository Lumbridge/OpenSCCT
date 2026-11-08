using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Core.Engine;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private string PronounsPanel(Viewer viewer)
    {
        string selection = _store.Pronouns(viewer.Account.Id);
        PronounForms forms = PlayerPronouns.For(selection);
        var fields = new StringBuilder("<p class=\"account-side-note\">Choose the pronouns used in game messages about you. They/them is the default.</p><label>Pronouns<select name=\"pronouns\">");
        foreach (string preset in PlayerPronouns.Presets)
            fields.Append("<option value=\"").Append(H(preset)).Append('"').Append(selection == preset ? " selected" : "").Append('>')
                .Append(H(preset)).Append(preset == PlayerPronouns.Default ? " (default)" : "").Append("</option>");
        fields.Append("</select></label><p class=\"field-help\">").Append(H(viewer.Account.Name ?? "Player")).Append(" used ")
            .Append(H(forms.Possessive)).Append(" last life</p><button class=\"button secondary\">Save pronouns</button>");
        return Section("Pronouns", "<div class=\"panel\">" + Form("/account/pronouns", viewer.Session.Csrf, fields.ToString()) + "</div>");
    }

    private IResult ChangePronouns(HttpContext c, Viewer viewer, IFormCollection form)
    {
        if (Restriction(c, viewer) is { } restriction) return Account(c, viewer, restriction, true);
        string selection = Field(form, "pronouns", PlayerPronouns.MaxLength + 1);
        return _store.SetPronouns(viewer.Account.Id, selection)
            ? Account(c, viewer, "Your pronouns have been saved.")
            : Account(c, viewer, "Choose a pronoun set from the list.", true);
    }
}
