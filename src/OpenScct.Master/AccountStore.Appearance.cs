using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    private void MigrateAppearance() => Exec("""
        CREATE TABLE IF NOT EXISTS account_appearance (
            account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
            revision INTEGER NOT NULL,
            spy TEXT NOT NULL,
            merc TEXT NOT NULL,
            title TEXT,
            managed INTEGER NOT NULL DEFAULT 0,
            updated_ms INTEGER NOT NULL);
        """);

    /// <summary>Seeds from the existing profile once and removes selections whose entitlement has since ended.</summary>
    public AccountAppearance Appearance(long accountId)
    {
        lock (_gate)
        {
            AccountAppearance? stored = ReadAppearanceLocked(accountId);
            AccountAppearance clean = CleanAppearance(accountId, stored ?? ReadLegacyAppearance(ProfileLocked(accountId)?.Data) ?? new());
            if (stored is null)
            {
                clean = clean with { Revision = 1 };
                WriteAppearanceLocked(accountId, clean, managed: false);
            }
            else if (!SameAppearance(stored, clean))
            {
                clean = clean with { Revision = stored.Revision + 1 };
                WriteAppearanceLocked(accountId, clean, managed: null);
            }

            return clean;
        }
    }

    public AppearanceUpdateResult UpdateAppearance(long accountId, AppearanceUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            AccountAppearance before = Appearance(accountId);
            AppearanceUpdateResult Refused(string error, bool conflict = false) => new() { Appearance = before, Error = error, Conflict = conflict };
            if (FindById(accountId) is not { } account)
            {
                return Refused("The account no longer exists.");
            }

            if (ActiveBan(accountId, account.DiscordId, null) is not null)
            {
                return Refused("Appearance cannot be changed while this account is banned.");
            }

            if (request.ExpectedRevision != before.Revision)
            {
                return Refused("Your appearance changed elsewhere. Reload the current selection or reapply your choices.", conflict: true);
            }

            var unlocked = Unlocked(accountId).ToHashSet(StringComparer.Ordinal);
            string? Validate(IReadOnlyList<string>? ids, CosmeticSide side)
            {
                if (ids is null || ids.Count > CosmeticCatalog.SlotsOf(side).Count())
                {
                    return "Choose at most one cosmetic per slot.";
                }

                var slots = new HashSet<CosmeticSlot>();
                foreach (string id in ids)
                {
                    if (string.IsNullOrEmpty(id) || id.Length > 96 || CosmeticCatalog.Find(id) is not { } item || item.Side != side || !unlocked.Contains(id))
                    {
                        return "One of the selected cosmetics is unavailable or not unlocked for this side.";
                    }

                    if (!slots.Add(item.Slot))
                    {
                        return "Choose at most one cosmetic per slot.";
                    }
                }

                return null;
            }

            string? error = Validate(request.Spy, CosmeticSide.Spy) ?? Validate(request.Merc, CosmeticSide.Merc);
            string? title = string.IsNullOrEmpty(request.Title) ? null : request.Title;
            if (error is null && title is not null && !Titles(accountId).Any(t => t.Id == title))
            {
                error = "That title is not unlocked.";
            }

            if (error is not null)
            {
                return Refused(error);
            }

            AccountAppearance after = new()
            {
                Revision = before.Revision + 1,
                Spy = request.Spy.OrderBy(id => CosmeticCatalog.Find(id)!.Slot).ToArray(),
                Merc = request.Merc.OrderBy(id => CosmeticCatalog.Find(id)!.Slot).ToArray(),
                Title = title,
            };
            return InTransaction(() =>
            {
                WriteAppearanceLocked(accountId, after, managed: true);
                AuditChange("appearance.update", "account:" + accountId.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, before, after);
                return new AppearanceUpdateResult { Appearance = after };
            });
        }
    }

    private AccountAppearance? ReadAppearanceLocked(long accountId) => One(
        "SELECT revision, spy, merc, title FROM account_appearance WHERE account_id = $a;",
        r => new AccountAppearance
        {
            Revision = r.GetInt64(0), Spy = JsonSerializer.Deserialize<string[]>(r.GetString(1)) ?? [],
            Merc = JsonSerializer.Deserialize<string[]>(r.GetString(2)) ?? [], Title = r.IsDBNull(3) ? null : r.GetString(3),
        }, ("$a", accountId));

    private void WriteAppearanceLocked(long accountId, AccountAppearance appearance, bool? managed)
    {
        long updated = _time.GetUtcNow().ToUnixTimeMilliseconds();
        Exec("""
            INSERT INTO account_appearance (account_id, revision, spy, merc, title, managed, updated_ms)
            VALUES ($a, $r, $s, $m, $t, $managed, $u)
            ON CONFLICT(account_id) DO UPDATE SET revision = $r, spy = $s, merc = $m, title = $t,
                managed = CASE WHEN $keep = 1 THEN account_appearance.managed ELSE $managed END,
                updated_ms = MAX(account_appearance.updated_ms + 1, $u);
            """, ("$a", accountId), ("$r", appearance.Revision), ("$s", JsonSerializer.Serialize(appearance.Spy)),
            ("$m", JsonSerializer.Serialize(appearance.Merc)), ("$t", appearance.Title), ("$managed", managed == true ? 1 : 0),
            ("$keep", managed is null ? 1 : 0), ("$u", updated));
    }

    private AccountAppearance CleanAppearance(long accountId, AccountAppearance appearance)
    {
        CosmeticUnlocks unlocks = CosmeticUnlocks.Of(Unlocked(accountId));
        string[] Clean(CosmeticSide side, IReadOnlyList<string> ids)
        {
            CosmeticLook look = CosmeticLook.FromIds(side, ids.Where(id => !string.IsNullOrEmpty(id))).Validate(side, unlocks);
            return CosmeticCatalog.SlotsOf(side).Select(slot => look[slot]).OfType<CosmeticItem>().Select(item => item.Id).ToArray();
        }

        return appearance with
        {
            Spy = Clean(CosmeticSide.Spy, appearance.Spy), Merc = Clean(CosmeticSide.Merc, appearance.Merc),
            Title = appearance.Title is { } title && Titles(accountId).Any(t => t.Id == title) ? title : null,
        };
    }

    private static bool SameAppearance(AccountAppearance a, AccountAppearance b) =>
        a.Title == b.Title && a.Spy.SequenceEqual(b.Spy) && a.Merc.SequenceEqual(b.Merc);

    private static AccountAppearance? ReadLegacyAppearance(string? data)
    {
        if (data is null) return null;
        try
        {
            JsonNode? record = JsonNode.Parse(data);
            if (record?["version"]?.GetValue<int>() != 1 || record["settings"] is not JsonObject settings ||
                (!settings.ContainsKey("cosmetics_spy") && !settings.ContainsKey("cosmetics_merc") && !settings.ContainsKey("title"))) return null;
            return new AccountAppearance
            {
                Spy = settings["cosmetics_spy"]?.Deserialize<string[]>() ?? [],
                Merc = settings["cosmetics_merc"]?.Deserialize<string[]>() ?? [],
                Title = settings["title"]?.GetValue<string>(),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Legacy clients may edit appearance until the first revision-checked edit takes ownership.</summary>
    private void AcceptLegacyAppearanceLocked(long accountId, string data)
    {
        bool managed = Convert.ToInt64(Scalar("SELECT COALESCE((SELECT managed FROM account_appearance WHERE account_id = $a), 0);", ("$a", accountId)),
            System.Globalization.CultureInfo.InvariantCulture) != 0;
        if (managed || ReadLegacyAppearance(data) is not { } legacy) return;
        AccountAppearance? before = ReadAppearanceLocked(accountId);
        AccountAppearance after = CleanAppearance(accountId, legacy);
        if (before is not null && SameAppearance(before, after)) return;
        WriteAppearanceLocked(accountId, after with { Revision = (before?.Revision ?? 0) + 1 }, managed: false);
    }

    /// <summary>Older games still receive selected appearance through their familiar v1 profile.</summary>
    private ProfileData WithAppearanceLocked(long accountId, ProfileData profile)
    {
        if (ReadAppearanceLocked(accountId) is not { } appearance) return profile;
        try
        {
            JsonNode? record = JsonNode.Parse(profile.Data);
            if (record?["version"]?.GetValue<int>() != 1 || record["settings"] is not JsonObject settings) return profile;
            settings["cosmetics_spy"] = JsonSerializer.SerializeToNode(appearance.Spy);
            settings["cosmetics_merc"] = JsonSerializer.SerializeToNode(appearance.Merc);
            settings["title"] = appearance.Title;
            long appearanceTime = Convert.ToInt64(Scalar("SELECT updated_ms FROM account_appearance WHERE account_id = $a;", ("$a", accountId)),
                System.Globalization.CultureInfo.InvariantCulture);
            return profile with { Data = record.ToJsonString(), Updated = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(profile.Updated.ToUnixTimeMilliseconds(), appearanceTime)) };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return profile;
        }
    }
}
