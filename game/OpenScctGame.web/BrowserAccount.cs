using System.Net.Http.Json;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Net.Discovery;
using OpenScctGame.Controls;
using OpenScctGame.Online;
using OpenScctGame.Options;
using HttpClient = System.Net.Http.HttpClient;

namespace OpenScctGame.Web;

internal static partial class BrowserAccount
{
    private sealed record Snapshot(bool SignedIn, long AccountId, string? Name, string? Csrf, ProfileData? Profile);
    private sealed record Pending(DateTimeOffset? ExpectedUpdated, string Data);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new();
    private static Uri? s_url;
    private static Snapshot? s_account;
    private static CookieAccountHandler? s_handler;
    private static string s_inviteKey = "";
    private static bool s_dirty, s_saving, s_stopped;
    private static DateTimeOffset s_retryAt;
    private static int s_retrySeconds = 5;

    [JSImport("globalThis.openscctAccountStatus")]
    private static partial void Status(string name, string message, bool needsChoice = false);

    [JSImport("globalThis.openscctLoadPendingProfile")]
    private static partial string LoadPending(string accountId);

    [JSImport("globalThis.openscctSavePendingProfile")]
    private static partial void SavePending(string accountId, string data);

    internal static async Task Fetch(string contentUrl)
    {
        s_url = new Uri(new Uri(contentUrl), "profile?settingsOnly=true");
        try
        {
            using var response = await Http.GetAsync(s_url);
            if (response.IsSuccessStatusCode) s_account = await response.Content.ReadFromJsonAsync<Snapshot>(Json);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            Console.Error.WriteLine("Account settings unavailable: " + error.Message);
        }
        BrowserStorage.Scope = s_account?.SignedIn == true ? "account." + s_account.AccountId : "guest";
        BrowserPlatform.MakeAccountClient = MakeClient;
        BrowserPlatform.AccountSignedOut = SignedOut;
        Status(s_account?.SignedIn == true ? s_account.Name ?? "Your account" : "", "");
    }

    internal static void Apply()
    {
        if (s_url is not null)
        {
            OnlineSettings.Current.MasterUrl = s_url.GetLeftPart(UriPartial.Authority);
            OnlineSettings.Current.InviteKey = OnlineSettings.Current.MasterUrl == OnlineSettings.DefaultMasterUrl ? OnlineSettings.DefaultInviteKey : "";
        }
        if (s_account?.SignedIn != true) return;
        BrowserPlatform.AccountId = s_account.AccountId;
        BrowserPlatform.AccountName = s_account.Name;
        AccountSettings.Current.Token = "";
        AccountSettings.Current.AccountId = s_account.AccountId;
        AccountSettings.Current.Name = s_account.Name ?? "";
        bool readable = s_account.Profile is null || CanRead(s_account.Profile.Data);
        if (!readable)
        {
            s_stopped = true;
            Status(s_account.Name ?? "Your account", "This profile needs a newer game build. Browser settings will stay local.");
        }
        Pending? pending = null;
        try { pending = JsonSerializer.Deserialize<Pending>(LoadPending(s_account.AccountId.ToString()), Json); }
        catch (Exception error) when (error is JsonException or JSException)
        { Console.Error.WriteLine("Pending browser settings: " + error.Message); }
        if (pending is not null && string.IsNullOrEmpty(pending.Data)) pending = null;
        // A response can be lost after the server committed the save. Treat the matching record as acknowledged.
        if (pending is not null && s_account.Profile is { } acknowledged && SameProfile(pending.Data, acknowledged.Data))
        {
            StorePending("");
            pending = null;
        }
        if (pending is not null)
        {
            if (!ProfileSync.Apply(pending.Data, separateAppearance: true)) s_stopped = true;
            s_dirty = true;
            if (pending.ExpectedUpdated != s_account.Profile?.Updated)
            {
                s_stopped = true;
                Status(s_account.Name ?? "Your account", "Your browser has unsynced changes, and your account changed elsewhere. Your browser settings are kept.", true);
            }
        }
        else if (readable && s_account.Profile is { } profile && !ProfileSync.Apply(profile.Data, separateAppearance: true))
        {
            s_stopped = true;
            Status(s_account.Name ?? "Your account", "This profile needs a newer game build. Browser settings will stay local.");
        }
        // Restoring a profile must never be mistaken for a local edit.
        SettingsStore.Saved += _ => MarkDirty();
        InputBindings.UserFileSaved += MarkDirty;
    }

    private static bool SameProfile(string first, string second)
    {
        static JsonNode? Settings(string data)
        {
            JsonNode? profile = JsonNode.Parse(data);
            if (profile?["settings"] is JsonObject settings)
                foreach (string key in new[] { "cosmetics_spy", "cosmetics_merc", "title" }) settings.Remove(key);
            return profile;
        }
        try { return JsonNode.DeepEquals(Settings(first), Settings(second)); }
        catch (JsonException) { return false; }
    }

    private static bool CanRead(string data)
    {
        try
        {
            using var record = JsonDocument.Parse(data);
            return record.RootElement.TryGetProperty("version", out var value) && value.TryGetInt32(out int version)
                && version is >= 1 and <= ProfileSync.Version;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    private static void MarkDirty()
    {
        if (s_account?.Profile is { } profile && SameProfile(ProfileSync.Build(true), profile.Data)) return;
        s_dirty = true;
        BrowserStorage.Flush();
        KeepPending();
    }

    private static void KeepPending()
    {
        if (s_account?.SignedIn == true)
            StorePending(JsonSerializer.Serialize(new Pending(s_account.Profile?.Updated, ProfileSync.Build(true)), Json));
    }

    internal static void UseAccountSettings()
    {
        if (s_account?.SignedIn == true)
        {
            BrowserStorage.DiscardBindings();
            StorePending("");
        }
    }

    private static void StorePending(string data)
    {
        try { SavePending(s_account!.AccountId.ToString(), data); }
        catch (JSException error) { Console.Error.WriteLine("Pending browser settings could not be saved: " + error.Message); }
    }

    internal static void Flush()
    {
        if (!s_dirty || s_saving || s_stopped || s_account?.SignedIn != true || s_url is null || DateTimeOffset.UtcNow < s_retryAt) return;
        s_dirty = false;
        s_saving = true;
        _ = SaveAsync(ProfileSync.Build(true));
    }

    private static MasterClient? MakeClient()
    {
        if (s_url is null) return null;
        string authority = s_url.GetLeftPart(UriPartial.Authority);
        return new MasterClient(authority, authority == OnlineSettings.DefaultMasterUrl ? OnlineSettings.DefaultInviteKey : s_inviteKey,
            s_handler ??= new CookieAccountHandler(authority));
    }

    // HttpClient on WebAssembly uses same-origin fetch credentials. Tokens stay in the HttpOnly cookie;
    // custom headers bind every native account request to the session that booted this game.
    private sealed class CookieAccountHandler(string authority) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.GetLeftPart(UriPartial.Authority) != authority)
                throw new HttpRequestException("Account requests must stay on this game's master.");
            if (s_account?.SignedIn == true)
            {
                request.Headers.Add("X-CSRF-Token", s_account.Csrf);
                request.Headers.Add("X-OpenSCCT-Account", s_account.AccountId.ToString(CultureInfo.InvariantCulture));
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static void SignedOut()
    {
        s_stopped = true;
        s_account = null;
        BrowserPlatform.AccountName = null;
        if (OperatingSystem.IsBrowser()) BrowserExports.ReloadAfterSignOut();
    }

    internal static async Task Join(string serverId, string password, string inviteKey)
    {
        if (!OperatingSystem.IsBrowser()) return;
        if (s_url is null) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(s_url, "join"));
            if (s_account?.Csrf is { } csrf) request.Headers.Add("X-CSRF-Token", csrf);
            // Match the desktop build's default-master access. Custom masters still use the explicitly entered key.
            if (string.IsNullOrWhiteSpace(inviteKey) && s_url.GetLeftPart(UriPartial.Authority) == OnlineSettings.DefaultMasterUrl)
                inviteKey = OnlineSettings.DefaultInviteKey;
            s_inviteKey = inviteKey;
            OnlineSettings.Current.InviteKey = inviteKey;
            if (!string.IsNullOrEmpty(inviteKey)) request.Headers.Add(MasterApi.KeyHeader, inviteKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new { serverId }, Json), System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            using var record = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!response.IsSuccessStatusCode)
            {
                BrowserExports.OnlineStatus(record.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "Join refused." : "Join refused.");
                return;
            }
            var data = record.RootElement;
            var socket = new UriBuilder(new Uri(s_url, data.GetProperty("socket").GetString()))
                { Scheme = s_url.Scheme == "https" ? "wss" : "ws" };
            BrowserPlatform.JoinOnline?.Invoke(data.GetProperty("name").GetString()!, socket.Uri.AbsoluteUri,
                password, data.TryGetProperty("ticket", out var ticket) ? ticket.GetString() : null,
                data.TryGetProperty("playerName", out var playerName) ? playerName.GetString() : null);
            BrowserExports.OnlineStatus("");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException)
        { BrowserExports.OnlineStatus("Could not connect: " + error.Message); }
    }

    private static async Task SaveAsync(string data)
    {
        try
        {
            Status(s_account!.Name ?? "Your account", "Saving settings…");
            using var request = new HttpRequestMessage(HttpMethod.Post, s_url);
            request.Headers.Add("X-CSRF-Token", s_account.Csrf);
            request.Content = JsonContent.Create(new { accountId = s_account.AccountId,
                profile = new ProfileData { Data = data, Updated = DateTimeOffset.UtcNow },
                expectedUpdated = s_account.Profile?.Updated }, options: Json);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                s_stopped = true;
                Status(s_account.Name ?? "Your account", "Sync paused. Your changes are saved in this browser. Your account may have changed elsewhere.", response.StatusCode == System.Net.HttpStatusCode.Conflict);
                return;
            }
            ProfileData? saved = await response.Content.ReadFromJsonAsync<ProfileData>(Json);
            if (saved is null) throw new JsonException("Missing saved profile.");
            s_account = s_account with { Profile = saved };
            s_retrySeconds = 5;
            if (s_dirty) KeepPending();
            else StorePending("");
            Status(s_account.Name ?? "Your account", "Settings synced");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            s_dirty = true;
            s_retryAt = DateTimeOffset.UtcNow.AddSeconds(s_retrySeconds);
            s_retrySeconds = Math.Min(s_retrySeconds * 2, 60);
            Status(s_account!.Name ?? "Your account", "Offline. Your changes are saved in this browser.");
            Console.Error.WriteLine("Account settings sync: " + error.Message);
        }
        finally { s_saving = false; }
    }
}
