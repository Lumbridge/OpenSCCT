using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private static string AccountTabs(string active) => "<nav class=\"account-tabs\" aria-label=\"Account pages\">" +
        string.Concat(new[] { ("/account", "Overview"), ("/account/appearance", "Appearance"), ("/account/privacy", "Privacy"), ("/account/security", "Sessions") }
            .Select(t => "<a href=\"" + t.Item1 + "\"" + (active == t.Item1 ? " aria-current=\"page\"" : "") + ">" + t.Item2 + "</a>")) + "</nav>";

    private IResult Account(HttpContext c, Viewer viewer, string? notice = null, bool bad = false)
    {
        AccountRow account = viewer.Account;
        ProgressRow p = _store.ProgressionOf(account.Id);
        ProfileVisibilityRow visibility = _store.ProfileVisibility(account.Id);
        var body = new StringBuilder(AccountTabs("/account") + PageHeader("YOUR ACCOUNT", account.Name is null ? "Welcome to OpenSCCT" : "Welcome back, " + account.Name,
            "Manage your name, privacy, and game progress."));
        if (account.Name is null) body.Append("<div class=\"notice\">Choose a player name below to finish setting up your account.</div>");
        if (_store.ActiveBan(account.Id, account.DiscordId, _clientIp(c)) is { } ban)
            body.Append("<div class=\"notice bad\">Your game account is currently restricted. ").Append(H(ban.Describe())).Append(" You can still manage your privacy and website sessions.</div>");
        body.Append("<div class=\"grid-two\"><section class=\"panel\"><h2>Your player name</h2><p class=\"account-side-note\">Your name is shared with the game. You can change it once every 30 days.</p>");
        long? nextRename = account.Name is not null && account.NameChanged is { } changed && changed + AccountStore.RenameCooldown > _store.Now ? changed + AccountStore.RenameCooldown : null;
        body.Append(Form("/account/name", viewer.Session.Csrf,
            "<label>Player name<input type=\"text\" name=\"name\" maxlength=\"16\" minlength=\"3\" required autocomplete=\"nickname\" value=\"" + H(account.Name) + "\"></label>" +
            (nextRename is { } next ? "<span class=\"field-help\">Next change available " + H(AccountStore.Format(next)) + ".</span>" : "<span class=\"field-help\">Choose an available name using the same rules as the game.</span>") +
            "<button class=\"button secondary\"" + (nextRename is not null ? " disabled" : "") + ">" + (account.Name is null ? "Claim name" : "Save name") + "</button>"));
        body.Append("</section><section class=\"panel\"><h2>Account visibility</h2><p class=\"lead\">")
            .Append(visibility.Hidden ? "Your public profile is currently hidden by moderation." : visibility.Public ? "Your profile is public." : "Your profile is private.")
            .Append("</p><p class=\"account-side-note\">Public profiles appear in player search and leaderboards. Private profiles are hidden, and your identity is removed from public match scoreboards.</p>")
            .Append(Link("/account/privacy", "Manage privacy →", "button secondary compact")).Append("</section></div>");
        body.Append(Section("Progression", "<div class=\"panel\"><div class=\"section-heading\"><div><p class=\"eyebrow\">" + H(p.Rank.IsMaster ? "MASTER PRESTIGE" : "LEVEL AND PRESTIGE") +
            "</p><h2>" + Rank(p.Rank) + "</h2></div><span class=\"muted\">" + N(p.TotalXp) + " lifetime XP</span></div>" +
            "<p class=\"account-side-note\">" + (p.Rank.AtCap ? "You have reached the level cap for this prestige." : N(p.Rank.XpIntoLevel) + " / " + N(p.Rank.XpForNext) + " XP toward the next level.") + "</p>" +
            (p.Rank.CanPrestige ? "<p>Prestiging resets your level and level-specific unlocks. Career stats, achievements, and permanent rewards stay with you.</p>" +
                Form("/account/prestige", viewer.Session.Csrf, "<label class=\"checkbox-label\"><input type=\"checkbox\" name=\"confirm\" value=\"yes\" required><span>I understand that my level and level-specific unlocks will reset.</span></label><button class=\"button\">Enter prestige " + (p.Prestige + 1) + "</button>",
                    "Enter the next prestige? Your level and level-specific unlocks will reset. Career stats and permanent rewards remain.") :
                "<p class=\"field-help\">" + (p.Rank.IsMaster ? "You have reached the final prestige." : "Reach level " + ProgressionK.MaxLevel + " to enter the next prestige.") + "</p>") + "</div>"));
        body.Append(Section("Your career", ProfileBody(account, true, PageOf(c))));
        return Show(c, "My account", "account", body.ToString(), notice, bad);
    }

    private IResult ChangeName(HttpContext c, Viewer viewer, IFormCollection form)
    {
        if (Restriction(c, viewer) is { } restriction) return Account(c, viewer, restriction, true);
        string name = Field(form, "name", 64);
        if (PlayerNames.ClaimProblem(name) is { } problem) return Account(c, viewer, problem, true);
        NameClaim result = _store.ClaimName(viewer.Account.Id, name, ignoreCooldown: false, out long retryAt);
        string? error = result switch
        {
            NameClaim.Taken => "That player name is already taken or reserved. Choose another name.",
            NameClaim.TooSoon => "You can change your name again on " + AccountStore.Format(retryAt) + ".",
            NameClaim.NoAccount => "This account is no longer available. Sign in again.",
            _ => null,
        };
        if (error is not null) return Account(c, viewer, error, true);
        if (_store.FindById(viewer.Account.Id) is { } updated)
        {
            viewer = viewer with { Account = updated };
            c.Items["player.viewer"] = viewer;
        }
        return Account(c, viewer, "Your player name has been saved.");
    }

    private IResult Prestige(HttpContext c, Viewer viewer, IFormCollection form)
    {
        if (Restriction(c, viewer) is { } restriction) return Account(c, viewer, restriction, true);
        if (Field(form, "confirm") != "yes") return Account(c, viewer, "Confirm that you understand the prestige reset before continuing.", true);
        PrestigeResult result = _store.Prestige(viewer.Account.Id);
        if (result == PrestigeResult.Done) _ = _store.Appearance(viewer.Account.Id); // Normalize selections whose level-specific entitlement ended.
        return Account(c, viewer, result switch
        {
            PrestigeResult.Done => "Prestige updated. Your new level and unlocks will appear in the game.",
            PrestigeResult.NotAtCap => "Reach level " + ProgressionK.MaxLevel + " before entering the next prestige.",
            _ => "You are already at the final prestige.",
        }, result != PrestigeResult.Done);
    }

    private IResult Privacy(HttpContext c, Viewer viewer, string? notice = null)
    {
        ProfileVisibilityRow privacy = _store.ProfileVisibility(viewer.Account.Id);
        string body = AccountTabs("/account/privacy") + PageHeader("YOUR ACCOUNT", "Profile privacy", "Choose whether other players can see your profile. Changes apply immediately.") +
            "<div class=\"narrow panel\">" + (privacy.Hidden ? "<div class=\"notice\">Moderation has hidden this profile. Your preference is saved, but the profile stays hidden until moderation restores it.</div>" : "") +
            Form("/account/privacy", viewer.Session.Csrf,
                "<label class=\"checkbox-label\"><input type=\"checkbox\" name=\"public\" value=\"1\"" + (privacy.Public ? " checked" : "") + "><span>Make my player profile public<small>Show my stats, achievements, progression, and match history.</small></span></label>" +
                "<div class=\"account-side-note\"><p>When private, your profile is removed from player search and leaderboards. Public match scoreboards show a private-player label instead of your identity.</p>" +
                "<p>You can still see your own career and matches here. Admins retain access for moderation. This setting does not change names seen during gameplay or in existing round recordings.</p></div>" +
                "<button class=\"button\">Save privacy preference</button>") + "</div>";
        return Show(c, "Profile privacy", "account", body, notice);
    }

    private IResult ChangePrivacy(HttpContext c, Viewer viewer, IFormCollection form)
    {
        bool isPublic = Field(form, "public") == "1";
        _store.SetProfilePublic(viewer.Account.Id, isPublic);
        return Privacy(c, viewer, isPublic ? "Your public-profile preference has been saved." : "Your profile is now private. Public match rows no longer identify you.");
    }

    private IResult Security(HttpContext c, Viewer viewer, string? notice = null, bool bad = false)
    {
        List<PlayerSessionRow> sessions = _store.PlayerSessions(viewer.Account.Id);
        string table = Table(["Session", "Signed in", "Last active", ""], sessions.Select(s => new[]
        {
            H(s.Kind == "game" ? "Game client" : "Website") + (s.Id == viewer.Session.Id ? " " + Badge("This browser", "ok") : ""),
            Time(s.Created), Time(s.LastUsed),
            Form("/account/security", viewer.Session.Csrf, Hidden("session", s.Id) + Hidden("kind", s.Kind) +
                "<button class=\"button secondary compact\" name=\"action\" value=\"revoke\">Sign out</button>",
                s.Id == viewer.Session.Id ? "Sign out of this browser?" : "End this sign-in session?", "inline-form"),
        }));
        string body = AccountTabs("/account/security") + PageHeader("YOUR ACCOUNT", "Manage your sessions", "Review your game and website sign-ins. End a session if you no longer use it.") + table +
            Section("Sign out other sessions", "<div class=\"panel\"><p class=\"account-side-note\">End all game sessions and other website sign-ins. This browser stays signed in.</p>" +
                Form("/account/security", viewer.Session.Csrf, "<button class=\"button danger\" name=\"action\" value=\"others\">Sign out other sessions</button>",
                    "End every game session and every other website session? This browser stays signed in.") + "</div>") +
            "<div class=\"section\">" + Form("/account/logout", viewer.Session.Csrf, "<button class=\"button secondary\">Sign out of this browser</button>") + "</div>";
        return Show(c, "Account sessions", "account", body, notice, bad);
    }

    private IResult RevokeSession(HttpContext c, Viewer viewer, IFormCollection form)
    {
        if (Field(form, "action") == "others")
        {
            int ended = _store.RevokePlayerSessions(viewer.Account.Id, viewer.Session.Id);
            return Security(c, viewer, ended + " other session(s) ended.");
        }
        string id = Field(form, "session"), kind = Field(form, "kind");
        bool endedOne = _store.RevokePlayerSession(viewer.Account.Id, kind, id);
        if (endedOne && id == viewer.Session.Id && kind == "web") return Logout(c);
        return Security(c, viewer, endedOne ? "The session has ended." : "That session is no longer available.", !endedOne);
    }

    private IResult Appearance(HttpContext c, Viewer viewer, string? notice = null, bool bad = false, AppearanceUpdateRequest? draft = null, bool conflict = false)
    {
        AccountAppearance appearance = _store.Appearance(viewer.Account.Id);
        IReadOnlyList<string> unlocked = _store.Unlocked(viewer.Account.Id);
        HashSet<string> entitled = unlocked.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<TitleItem> titles = _store.Titles(viewer.Account.Id);
        var body = new StringBuilder(AccountTabs("/account/appearance") + PageHeader("YOUR ACCOUNT", "Appearance", "Cosmetics update in menus and lobbies. Other players see a new title when you next join."));
        if (conflict) body.Append("<div class=\"notice bad\"><strong>Your appearance changed elsewhere.</strong> Your draft is preserved below. Reload to use the current selection, or review and reapply your draft.</div>");
        var fields = new StringBuilder(Hidden("revision", appearance.Revision.ToString(CultureInfo.InvariantCulture)) + "<div class=\"appearance-grid\">");
        foreach (CosmeticSide side in Enum.GetValues<CosmeticSide>())
        {
            IReadOnlyList<string> selectedIds = side == CosmeticSide.Spy ? draft?.Spy ?? appearance.Spy : draft?.Merc ?? appearance.Merc;
            CosmeticLook look = CosmeticLook.FromIds(side, selectedIds);
            string prefix = side == CosmeticSide.Spy ? "spy" : "merc";
            fields.Append("<section class=\"panel\"><p class=\"side-label\">").Append(side == CosmeticSide.Spy ? "SPY" : "MERCENARY").Append("</p><div class=\"appearance-slots\">");
            foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(side))
            {
                CosmeticItem? selected = look[slot];
                fields.Append("<div class=\"appearance-slot\"><div class=\"appearance-swatch\" aria-hidden=\"true\">").Append(Swatch(selected)).Append("</div><label>").Append(H(SlotName(side, slot)))
                    .Append("<select name=\"").Append(prefix).Append('.').Append(slot).Append("\"><option value=\"\"").Append(selected is null ? " selected" : "").Append(">Standard</option>");
                foreach (CosmeticItem item in CosmeticCatalog.All.Where(i => i.Side == side && i.Slot == slot).OrderBy(i => i.Name, StringComparer.Ordinal))
                    fields.Append("<option value=\"").Append(H(item.Id)).Append('"').Append(item.Id == selected?.Id ? " selected" : "").Append(entitled.Contains(item.Id) ? "" : " disabled")
                        .Append('>').Append(H(item.Name)).Append(entitled.Contains(item.Id) ? "" : " · Locked").Append("</option>");
                fields.Append("</select></label></div>");
            }
            fields.Append("</div></section>");
        }
        string? selectedTitle = draft is null ? appearance.Title : draft.Title;
        fields.Append("</div><section class=\"panel narrow\"><label>Player title<select name=\"title\"><option value=\"\"").Append(string.IsNullOrEmpty(selectedTitle) ? " selected" : "").Append(">No title</option>");
        foreach (TitleItem title in TitleCatalog.All)
            fields.Append("<option value=\"").Append(H(title.Id)).Append('"').Append(title.Id == selectedTitle ? " selected" : "").Append(titles.Any(t => t.Id == title.Id) ? "" : " disabled")
                .Append('>').Append(H(title.Text)).Append(titles.Any(t => t.Id == title.Id) ? "" : " · Locked").Append("</option>");
        fields.Append("</select><span class=\"field-help\">Unlocked titles appear under your name in the game.</span></label></section><div class=\"button-row\"><button class=\"button\">")
            .Append(conflict ? "Reapply my selection" : "Save appearance").Append("</button>").Append(Link("/account/appearance", conflict ? "Reload current selection" : "Reset unsaved changes", "button secondary"))
            .Append("</div><p class=\"field-help\">Keep your game up to date to receive website changes while it is running. Appearance stays unchanged during a round.</p>");
        body.Append(Form("/account/appearance", viewer.Session.Csrf, fields.ToString(), conflict ? "Replace the current appearance with the selection shown here?" : null));
        return Show(c, "Appearance", "account", body.ToString(), notice, bad, conflict ? 409 : 200);
    }

    private IResult ChangeAppearance(HttpContext c, Viewer viewer, IFormCollection form)
    {
        if (Restriction(c, viewer) is { } restriction) return Appearance(c, viewer, restriction, true);
        if (!long.TryParse(Field(form, "revision", 24), NumberStyles.None, CultureInfo.InvariantCulture, out long revision))
            return Appearance(c, viewer, "This appearance form has expired. Review your selection and try again.", true);
        IReadOnlyList<string> Selected(CosmeticSide side, string prefix) => CosmeticCatalog.SlotsOf(side)
            .Select(slot => Field(form, prefix + "." + slot, 128)).Where(id => id.Length > 0).ToArray();
        var request = new AppearanceUpdateRequest
        {
            ExpectedRevision = revision, Spy = Selected(CosmeticSide.Spy, "spy"), Merc = Selected(CosmeticSide.Merc, "merc"),
            Title = Field(form, "title", 128) is { Length: > 0 } title ? title : null,
        };
        AppearanceUpdateResult result = _store.UpdateAppearance(viewer.Account.Id, request);
        return result.Success ? Appearance(c, viewer, "Appearance saved. Cosmetics update in menus and lobbies; other players see your new title when you next join.")
            : Appearance(c, viewer, result.Error, true, request, result.Conflict);
    }

    private string? Restriction(HttpContext c, Viewer viewer) => _store.ActiveBan(viewer.Account.Id, viewer.Account.DiscordId, _clientIp(c)) is not null
        ? "Game account changes are unavailable while this account is restricted. You can still manage privacy and sessions." : null;

    private static string SlotName(CosmeticSide side, CosmeticSlot slot) => slot switch
    {
        CosmeticSlot.Body => side == CosmeticSide.Spy ? "Suit" : "Armour",
        CosmeticSlot.Light => side == CosmeticSide.Spy ? "Goggle lights" : "Helmet light",
        CosmeticSlot.Pistol => "Shocker", CosmeticSlot.GunAr => "Assault rifle", CosmeticSlot.GunSmg => "SMG", CosmeticSlot.GunShotgun => "Shotgun",
        CosmeticSlot.Suppressed => "Suppressed pistol", CosmeticSlot.Automatic => "Automatic pistol", CosmeticSlot.Magnum44 => ".44 Magnum",
        CosmeticSlot.DesertEagle => "Desert Eagle", CosmeticSlot.Unsuppressed => "Pistol", _ => slot.ToString(),
    };

    private static string Swatch(CosmeticItem? item)
    {
        if (item is null) return "<svg viewBox=\"0 0 32 32\" aria-hidden=\"true\"><rect x=\"4\" y=\"4\" width=\"24\" height=\"24\" rx=\"5\" fill=\"#52625b\"/><path d=\"M9 23L23 9\" stroke=\"#839188\" stroke-width=\"2\"/></svg>";
        if (item.Kind == CosmeticKind.Hue)
            return "<svg viewBox=\"0 0 32 32\" aria-hidden=\"true\"><circle cx=\"16\" cy=\"16\" r=\"11\" fill=\"hsl(" + (item.Hue * 360f).ToString("0", CultureInfo.InvariantCulture) + " 70% 65%)\"/></svg>";
        if (item.Kind == CosmeticKind.Texture && CosmeticCatalog.Patterns.FirstOrDefault(p => p.Id == item.Pattern) is { } pattern)
        {
            var svg = new StringBuilder("<svg viewBox=\"0 0 32 32\" aria-hidden=\"true\">");
            int count = Math.Max(1, pattern.Colours.Count);
            for (int i = 0; i < pattern.Colours.Count; i++)
                svg.Append("<rect x=\"").Append(i * 32 / count).Append("\" y=\"0\" width=\"").Append(32 / count + 1).Append("\" height=\"32\" fill=\"#").Append(pattern.Colours[i].ToString("X6", CultureInfo.InvariantCulture)).Append("\"/>");
            return svg.Append("</svg>").ToString();
        }
        return "<svg viewBox=\"0 0 32 32\" aria-hidden=\"true\"><rect x=\"2\" y=\"2\" width=\"28\" height=\"28\" rx=\"5\" fill=\"#" + item.Rgb.ToString("X6", CultureInfo.InvariantCulture) + "\"/></svg>";
    }
}
