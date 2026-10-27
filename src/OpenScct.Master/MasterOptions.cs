using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// Settings of the master (the <c>Master</c> section of <c>svm-master.json</c>, overridable with environment
/// variables such as <c>SVM_Master__InviteKeys__0</c>). See <c>svm-master.example.json</c> and docs/ONLINE.md.
/// </summary>
public sealed class MasterOptions
{
    /// <summary>Where the HTTP API listens. On a VPS keep it on loopback behind a TLS proxy (docs/ONLINE.md).</summary>
    public string HttpUrl { get; set; } = "http://127.0.0.1:" + BuildInfo.DefaultMasterHttpPort;

    /// <summary>The name or address players and hosts reach this master's UDP ports at (put in every answer).</summary>
    public string PublicHost { get; set; } = "127.0.0.1";

    /// <summary>Address the UDP services bind (an empty value or "*" binds every interface).</summary>
    public string UdpBind { get; set; } = "*";

    /// <summary>UDP port of the NAT punch introducer (0 picks a free port, for tests).</summary>
    public int IntroducerPort { get; set; } = BuildInfo.DefaultIntroducerPort;

    /// <summary>UDP port of the relay (0 picks a free port, for tests).</summary>
    public int RelayPort { get; set; } = BuildInfo.DefaultRelayPort;

    /// <summary>UDP port the reachability probes go out from (0: a free port; hosts never send to it).</summary>
    public int ProbePort { get; set; }

    /// <summary>Run the introducer (NAT punch).</summary>
    public bool Introducer { get; set; } = true;

    /// <summary>Run the relay.</summary>
    public bool Relay { get; set; } = true;

    /// <summary>Probe hosts for direct reachability.</summary>
    public bool Probe { get; set; } = true;

    /// <summary>
    /// Invite keys: when any is set, every API call needs one in the <c>X-Svm-Key</c> header (a private master).
    /// Empty runs a public list.
    /// </summary>
    public List<string> InviteKeys { get; set; } = new();

    /// <summary>Message of the day shown above the server list.</summary>
    public string? Motd { get; set; }

    /// <summary>The newest client version (an update notice for older ones).</summary>
    public string? LatestVersion { get; set; }

    /// <summary>The oldest client version still listed and served; older hosts are refused with an update message.</summary>
    public string? MinVersion { get; set; }

    /// <summary>Seconds between heartbeats a host is asked for.</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>Seconds after the last heartbeat a server leaves the list.</summary>
    public int ExpirySeconds { get; set; } = 90;

    /// <summary>Seconds a host's introducer or relay registration stays valid without a refresh.</summary>
    public int RegistrationSeconds { get; set; } = 30;

    /// <summary>Seconds between reachability probes of one host (a new address or port probes at once).</summary>
    public int ReprobeSeconds { get; set; } = 300;

    /// <summary>Seconds a probe waits for its answer.</summary>
    public int ProbeTimeoutSeconds { get; set; } = 3;

    /// <summary>Most servers on the list.</summary>
    public int MaxServers { get; set; } = 500;

    /// <summary>Most servers one public address may list.</summary>
    public int MaxServersPerAddress { get; set; } = 8;

    /// <summary>Most relay sessions at once.</summary>
    public int MaxRelaySessions { get; set; } = 256;

    /// <summary>Seconds without traffic after which a relay session ends.</summary>
    public int RelayIdleSeconds { get; set; } = 60;

    /// <summary>Most bytes per second one relay session forwards in each direction.</summary>
    public int RelayBytesPerSecond { get; set; } = 64 * 1024;

    /// <summary>Most datagrams per second one relay session forwards in each direction.</summary>
    public int RelayFramesPerSecond { get; set; } = 400;

    /// <summary>API calls one address may make per minute (heartbeats, lists and joins together).</summary>
    public int RequestsPerMinute { get; set; } = 240;

    /// <summary>
    /// Read the client address from <c>X-Forwarded-For</c> when the request comes from loopback (the TLS proxy on the
    /// same machine). Leave false when Kestrel faces the internet itself.
    /// </summary>
    public bool TrustForwardedFor { get; set; } = true;

    /// <summary>
    /// The https base address of this master for the sign-in pages (null: <c>https://</c> + <see cref="PublicHost"/>).
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>Discord user ids (snowflakes) of the master's admins: their tickets carry the admin flag.</summary>
    public List<string> Admins { get; set; } = new();

    /// <summary>The account database (SQLite); a relative path is next to the config file.</summary>
    public string Database { get; set; } = "accounts.db";

    /// <summary>The ticket signing key (PEM, made on first start); a relative path is next to the config file.</summary>
    public string TicketKeyFile { get; set; } = "ticket-key.pem";

    /// <summary>
    /// The folder relative <see cref="Database"/> and <see cref="TicketKeyFile"/> paths resolve against (svm-master
    /// sets the config file's folder; null: the working folder).
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>Discord sign-in. Accounts are on when both the client id and the secret are set.</summary>
    public DiscordOptions Discord { get; set; } = new();

    /// <summary>Our own servers: a heartbeat with one of these keys is trusted (admin tickets, results count).</summary>
    public List<TrustedServerOptions> TrustedServers { get; set; } = new();

    /// <summary>Round demos kept in S3 (docs/ONLINE.md, "Demos"). Off while <see cref="DemoOptions.Bucket"/> is empty.</summary>
    public DemoOptions Demos { get; set; } = new();

    /// <summary>True when demos are on: a bucket is named and players can sign in (downloads need an account).</summary>
    public bool DemosEnabled => !string.IsNullOrWhiteSpace(Demos.Bucket) && AccountsEnabled;

    /// <summary>True when players can sign in (Discord is set up).</summary>
    public bool AccountsEnabled => !string.IsNullOrWhiteSpace(Discord.ClientId) && !string.IsNullOrWhiteSpace(Discord.ClientSecret);

    /// <summary>The base address of the sign-in pages, without a trailing slash.</summary>
    public string PublicBaseUrl => (string.IsNullOrWhiteSpace(PublicUrl) ? "https://" + PublicHost : PublicUrl).TrimEnd('/');

    /// <summary>Where Discord sends the browser back to.</summary>
    public string DiscordRedirectUrl =>
        string.IsNullOrWhiteSpace(Discord.RedirectUrl) ? PublicBaseUrl + MasterApi.DiscordCallbackPath : Discord.RedirectUrl;

    /// <summary><paramref name="path"/> made absolute against <see cref="DataDirectory"/>.</summary>
    public string ResolvePath(string path) =>
        Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(DataDirectory) ? Directory.GetCurrentDirectory() : DataDirectory, path));

    /// <summary>The trusted server whose key is <paramref name="key"/>, or null.</summary>
    public TrustedServerOptions? TrustedServer(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (TrustedServerOptions s in TrustedServers)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s.Key), Encoding.UTF8.GetBytes(key)))
            {
                return s;
            }
        }

        return null;
    }

    public bool IsAdmin(string discordId) => Admins.Contains(discordId, StringComparer.Ordinal);

    public void Validate()
    {
        if (HeartbeatSeconds < 5 || ExpirySeconds <= HeartbeatSeconds)
        {
            throw new InvalidOperationException("HeartbeatSeconds must be at least 5 and below ExpirySeconds.");
        }

        foreach (int port in new[] { IntroducerPort, RelayPort, ProbePort })
        {
            if (port is < 0 or > 65535)
            {
                throw new InvalidOperationException($"UDP port {port} is out of range.");
            }
        }

        if (string.IsNullOrWhiteSpace(PublicHost))
        {
            throw new InvalidOperationException("PublicHost must name this master's public address.");
        }

        bool id = !string.IsNullOrWhiteSpace(Discord.ClientId);
        bool secret = !string.IsNullOrWhiteSpace(Discord.ClientSecret);
        if (id != secret)
        {
            throw new InvalidOperationException(
                "Discord.ClientId and Discord.ClientSecret must both be set (the secret in SVM_Master__Discord__ClientSecret) or both be empty.");
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out Uri? publicUrl) || (publicUrl.Scheme != Uri.UriSchemeHttps && publicUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("PublicUrl must be an http(s) address.");
        }

        if (!Uri.TryCreate(DiscordRedirectUrl, UriKind.Absolute, out Uri? redirect) || (redirect.Scheme != Uri.UriSchemeHttps && redirect.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Discord.RedirectUrl must be an http(s) address.");
        }

        if (!Uri.TryCreate(Discord.ApiUrl, UriKind.Absolute, out _) || !Uri.TryCreate(Discord.AuthorizeUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Discord.ApiUrl and Discord.AuthorizeUrl must be addresses.");
        }

        foreach (string admin in Admins)
        {
            if (!IsSnowflake(admin))
            {
                throw new InvalidOperationException($"Admins: '{admin}' is not a Discord user id (digits only).");
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (TrustedServerOptions s in TrustedServers)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || s.Key.Length < 16)
            {
                throw new InvalidOperationException("Every TrustedServers entry needs a Name and a Key of at least 16 characters.");
            }

            if (!keys.Add(s.Key))
            {
                throw new InvalidOperationException($"TrustedServers: the key of '{s.Name}' is used twice.");
            }
        }

        if (!string.IsNullOrWhiteSpace(Demos.Bucket))
        {
            if (string.IsNullOrWhiteSpace(Demos.Region) || string.IsNullOrWhiteSpace(Demos.AccessKey) || string.IsNullOrWhiteSpace(Demos.SecretKey))
            {
                throw new InvalidOperationException(
                    "Demos needs Region, AccessKey and SecretKey with its Bucket (the secret in SVM_Master__Demos__SecretKey).");
            }

            if (Demos.KeepDays is < 1 or > 365 || Demos.MaxBytes < 1024 * 1024)
            {
                throw new InvalidOperationException("Demos.KeepDays must be 1 to 365 and Demos.MaxBytes at least 1 MB.");
            }
        }

        if (string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(TicketKeyFile))
        {
            throw new InvalidOperationException("Database and TicketKeyFile must name files.");
        }
    }

    /// <summary>True when <paramref name="text"/> looks like a Discord user id (17 to 20 digits).</summary>
    public static bool IsSnowflake(string? text)
    {
        if (text is null || text.Length is < 17 or > 20)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The Discord application players sign in with (discord.com/developers, OAuth2).</summary>
public sealed class DiscordOptions
{
    public string? ClientId { get; set; }

    /// <summary>Keep it out of the config file: set <c>SVM_Master__Discord__ClientSecret</c> in the environment.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The OAuth2 redirect registered with Discord (null: the master's own <c>/auth/discord/callback</c>).</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>Discord's API base (tests point it at a fake).</summary>
    public string ApiUrl { get; set; } = "https://discord.com/api";

    /// <summary>Discord's authorize page (tests point it at a fake).</summary>
    public string AuthorizeUrl { get; set; } = "https://discord.com/oauth2/authorize";
}

/// <summary>
/// Where round demos are kept: a private S3 bucket the master signs short-lived upload and download links for (the
/// files never pass through the master). The IAM user's keys need only s3:PutObject and s3:GetObject on demos/*.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>The bucket's name; empty turns demos off.</summary>
    public string? Bucket { get; set; }

    public string Region { get; set; } = "eu-west-2";

    public string? AccessKey { get; set; }

    /// <summary>Keep it out of the config file: set <c>SVM_Master__Demos__SecretKey</c> in the environment.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Days a demo is listed (the bucket's lifecycle rule deletes the file after the same time).</summary>
    public int KeepDays { get; set; } = 7;

    /// <summary>The largest demo a server may upload, bytes.</summary>
    public long MaxBytes { get; set; } = 64L * 1024 * 1024;
}

/// <summary>One of our own servers, known by its key (sent in <c>X-Svm-Server-Key</c>).</summary>
public sealed class TrustedServerOptions
{
    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}
