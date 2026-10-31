using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenScct.Net.Discovery;

/// <summary>
/// How a listed host can be reached, as the master's probes found it (docs/ONLINE.md, "Reachability"):
/// a probe from an unrelated port got through (<see cref="Direct"/>: port forwarded, UPnP or a public address), the
/// host's NAT maps one port for every destination so a NAT punch should work (<see cref="NeedsIntro"/>), or it maps a
/// new port per destination (or offers no punch) and only the relay reaches it (<see cref="RelayOnly"/>).
/// </summary>
public enum Reachability
{
    Unknown,
    Direct,
    NeedsIntro,
    RelayOnly,
}

/// <summary>What a listed server is: a game server (lobby and rounds) or a hub (the social space, docs/HUB.md).</summary>
public enum ServerKind
{
    Game,
    Hub,
}

/// <summary>Paths, header and JSON settings of the master's HTTP API (src/OpenScct.Master).</summary>
public static class MasterApi
{
    /// <summary>Header carrying the invite key of a private master.</summary>
    public const string KeyHeader = "X-Svm-Key";

    public const string HeartbeatPath = "/api/v1/servers/heartbeat";

    public const string ListPath = "/api/v1/servers";

    public const string StatusPath = "/api/v1/status";

    public static string ServerPath(string serverId) => ListPath + "/" + Uri.EscapeDataString(serverId);

    public static string JoinPath(string serverId) => ServerPath(serverId) + "/join";

    /// <summary>Header carrying a trusted server's key (heartbeats and round results).</summary>
    public const string ServerKeyHeader = "X-Svm-Server-Key";

    public const string AccountPath = "/api/v1/account";

    public const string LinkPath = "/api/v1/account/link";

    public const string AccountNamePath = "/api/v1/account/name";

    public const string SignOutPath = "/api/v1/account/signout";

    /// <summary>Moves a level 55 account to the next prestige (level 1 again).</summary>
    public const string PrestigePath = "/api/v1/account/prestige";

    public const string ProfilePath = "/api/v1/account/profile";

    public const string ResultsPath = "/api/v1/results";

    /// <summary>Ready round demos for signed-in players (<c>?page=</c>, from 0).</summary>
    public const string DemosPath = "/api/v1/demos";

    /// <summary>A trusted server asks for a demo upload link.</summary>
    public const string DemoUploadPath = "/api/v1/demos/upload";

    /// <summary>A trusted server confirms a demo's upload.</summary>
    public static string DemoDonePath(long demoId) => DemosPath + "/" + demoId.ToString(CultureInfo.InvariantCulture) + "/done";

    /// <summary>A signed-in player asks for a demo's download link.</summary>
    public static string DemoDownloadPath(long demoId) => DemosPath + "/" + demoId.ToString(CultureInfo.InvariantCulture) + "/download";

    /// <summary>Demos per page of <see cref="DemosPath"/>.</summary>
    public const int DemoPageSize = 25;

    /// <summary>The page the game opens in the browser to sign in (outside /api, so no invite key).</summary>
    public const string LinkPagePrefix = "/link/";

    public const string DiscordCallbackPath = "/auth/discord/callback";

    public static string LinkPollPath(string code) => LinkPath + "/" + Uri.EscapeDataString(code);

    /// <summary>The largest settings record the master keeps per account.</summary>
    public const int MaxProfileBytes = 64 * 1024;

    /// <summary>camelCase JSON, enums as text.</summary>
    public static JsonSerializerOptions Json { get; } = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>The list's <c>kind</c> query value: <c>game</c> (also when absent) or <c>hub</c>.</summary>
    public static string KindText(ServerKind kind) => kind == ServerKind.Hub ? "hub" : "game";

    /// <summary>A 64-bit hash as the API writes it (16 lower-case hex digits).</summary>
    public static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

    public static bool TryParseHex(string? text, out ulong value)
    {
        value = 0;
        return !string.IsNullOrEmpty(text) && text.Length <= 16 &&
            ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>A host's heartbeat: who it is, what it plays and how to reach it. Sent every 30 s.</summary>
public sealed record HeartbeatRequest
{
    /// <summary>The id the master gave on the first heartbeat (null on the first).</summary>
    public string? ServerId { get; init; }

    /// <summary>The host token the master gave with the id; it proves later heartbeats come from the same host.</summary>
    public string? HostToken { get; init; }

    public string Name { get; init; } = "OpenSCCT server";

    /// <summary>UDP port of the game socket on the host.</summary>
    public int GamePort { get; init; }

    /// <summary>The host's own addresses (for players behind the same router).</summary>
    public IReadOnlyList<string> LanAddresses { get; init; } = [];

    public string Map { get; init; } = string.Empty;

    public int Mode { get; init; }

    public SessionPhase Phase { get; init; }

    public int Players { get; init; }

    public int MaxPlayers { get; init; }

    public bool Password { get; init; }

    public bool Dedicated { get; init; }

    /// <summary>Protocol hash, hex.</summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>Content hash of the map, hex.</summary>
    public string Content { get; init; } = string.Empty;

    public string Version { get; init; } = BuildInfo.Version;

    /// <summary>False keeps the server off the public list (still joinable by its id or address).</summary>
    public bool Listed { get; init; } = true;

    /// <summary>The host registers with the introducer for NAT punches.</summary>
    public bool NatPunch { get; init; } = true;

    /// <summary>The host takes relay sessions.</summary>
    public bool Relay { get; init; } = true;

    /// <summary>The players' names, for the browser's details.</summary>
    public IReadOnlyList<string> PlayerNames { get; init; } = [];

    /// <summary>Each name's side (0 spies, 1 mercs, 255 spectator), in the order of <see cref="PlayerNames"/>; older hosts send none.</summary>
    public IReadOnlyList<byte> PlayerTeams { get; init; } = [];

    /// <summary>Only signed-in players (with a master ticket) may join.</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>A practice server (rl:net.practice): unlimited lives, practice commands, no stats.</summary>
    public bool Practice { get; init; }

    /// <summary>Account ids of the signed-in players on the server (for global ban kicks).</summary>
    public IReadOnlyList<long> AccountIds { get; init; } = [];

    /// <summary>A game server or a hub (older hosts send none: a game server).</summary>
    public ServerKind Kind { get; init; }

    /// <summary>Where the server stands, as its operator names it (for example <c>EU</c>, <c>US</c>), or empty.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Every player with team, ping and account (the admin portal; older hosts send none).</summary>
    public IReadOnlyList<HeartbeatPlayer> PlayerDetails { get; init; } = [];

    /// <summary>Seconds left in the round being played, or -1.</summary>
    public int RoundSecondsLeft { get; init; } = -1;

    /// <summary>What the commands of the last answers printed (<see cref="HeartbeatResponse.Commands"/>).</summary>
    public IReadOnlyList<MasterCommandResult> CommandResults { get; init; } = [];

    /// <summary>
    /// Signed-in players who dropped out of the round being played and may rejoin it (the master offers them the
    /// rejoin when their game asks for the account; older hosts send none).
    /// </summary>
    public IReadOnlyList<long> RejoinAccounts { get; init; } = [];
}

/// <summary>One player of a server as its heartbeat describes it for the admin portal.</summary>
public sealed record HeartbeatPlayer
{
    /// <summary>The session's player id (the server's kick and ban commands take it).</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>0 spies, 1 mercs, 255 spectator.</summary>
    public int Team { get; init; }

    public int PingMs { get; init; }

    public long? AccountId { get; init; }

    public bool Bot { get; init; }

    public bool Admin { get; init; }

    public bool Host { get; init; }
}

/// <summary>An admin command for a trusted server: a line of its console (OnlineServer.Execute).</summary>
public sealed record MasterCommand
{
    public long Id { get; init; }

    public string Line { get; init; } = string.Empty;
}

/// <summary>What a server printed for one <see cref="MasterCommand"/>.</summary>
public sealed record MasterCommandResult
{
    public long Id { get; init; }

    public string Output { get; init; } = string.Empty;
}

/// <summary>The master's answer to a heartbeat.</summary>
public sealed record HeartbeatResponse
{
    public string ServerId { get; init; } = string.Empty;

    public string HostToken { get; init; } = string.Empty;

    /// <summary>The host's address as the master sees it.</summary>
    public string PublicAddress { get; init; } = string.Empty;

    public Reachability Reachability { get; init; }

    /// <summary>"host:port" of the NAT punch introducer, or null when the master runs none.</summary>
    public string? Introducer { get; init; }

    /// <summary>"host:port" of the relay, or null when the master runs none.</summary>
    public string? Relay { get; init; }

    /// <summary>Seconds until the next heartbeat is due.</summary>
    public int HeartbeatSeconds { get; init; } = 30;

    /// <summary>A line for the host's lobby (update notice, a warning), or null.</summary>
    public string? Message { get; init; }

    public string? LatestVersion { get; init; }

    public string? MinVersion { get; init; }

    public bool Listed { get; init; }

    /// <summary>True when the heartbeat carried a trusted server key the master knows (admin tickets and results count).</summary>
    public bool Trusted { get; init; }

    /// <summary>The master's ticket public key (base64 SubjectPublicKeyInfo), or null when it signs no tickets.</summary>
    public string? TicketKey { get; init; }

    /// <summary>Signed-in players on the server who are now banned: drop them.</summary>
    public IReadOnlyList<long> KickAccounts { get; init; } = [];

    /// <summary>Admin commands to run (trusted servers only; others get none and must ignore any).</summary>
    public IReadOnlyList<MasterCommand> Commands { get; init; } = [];
}

/// <summary>One server of the list.</summary>
public sealed record ServerEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Public IP address of the host.</summary>
    public string Address { get; init; } = string.Empty;

    public int Port { get; init; }

    public IReadOnlyList<string> LanAddresses { get; init; } = [];

    public string Map { get; init; } = string.Empty;

    public int Mode { get; init; }

    public SessionPhase Phase { get; init; }

    public int Players { get; init; }

    public int MaxPlayers { get; init; }

    public bool Password { get; init; }

    public bool Dedicated { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public Reachability Reachability { get; init; }

    /// <summary>Seconds since the last heartbeat.</summary>
    public int AgeSeconds { get; init; }

    public IReadOnlyList<string> PlayerNames { get; init; } = [];

    /// <summary>Each name's side (0 spies, 1 mercs, 255 spectator), or empty when the host sent none.</summary>
    public IReadOnlyList<byte> PlayerTeams { get; init; } = [];

    /// <summary>Only signed-in players may join.</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>A server the master trusts (one of its own): achievements count there.</summary>
    public bool Trusted { get; init; }

    /// <summary>A practice server (rl:net.practice): unlimited lives, practice commands, no stats.</summary>
    public bool Practice { get; init; }

    /// <summary>A game server or a hub.</summary>
    public ServerKind Kind { get; init; }

    /// <summary>The server's region (<c>EU</c>, <c>US</c>...), or empty when its operator gave none.</summary>
    public string Region { get; init; } = string.Empty;
}

/// <summary>The list, with the asker's own public address and the master's notices.</summary>
public sealed record ServerListResponse
{
    public IReadOnlyList<ServerEntry> Servers { get; init; } = [];

    public string YourAddress { get; init; } = string.Empty;

    public string? Motd { get; init; }

    public string? LatestVersion { get; init; }

    public string? MinVersion { get; init; }
}

/// <summary>
/// How to join one server: its direct address, a NAT punch token for the introducer, and a relay token, each when the
/// master offers it; the client tries them in the order the reachability suggests.
/// </summary>
public sealed record JoinResponse
{
    public string ServerId { get; init; } = string.Empty;

    /// <summary>"ip:port" of the host's game socket on its public address.</summary>
    public string? Direct { get; init; }

    /// <summary>"ip:port" of the host on its own network (tried first when the asker shares the host's public address).</summary>
    public IReadOnlyList<string> LanAddresses { get; init; } = [];

    /// <summary>True when the asker and the host have the same public address (same router).</summary>
    public bool SameNetwork { get; init; }

    public Reachability Reachability { get; init; }

    public string? Introducer { get; init; }

    public string? PunchToken { get; init; }

    public string? Relay { get; init; }

    public string? RelayToken { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Protocol { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Map { get; init; } = string.Empty;

    /// <summary>Only signed-in players may join.</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>
    /// The asker's ticket for this server (<see cref="PlayerTicket"/> text) when the join was sent signed in, or null.
    /// </summary>
    public string? Ticket { get; init; }

    /// <summary>Why no ticket was made for a signed-in asker (banned, signed out), or null.</summary>
    public string? TicketProblem { get; init; }
}

/// <summary>The master's status page (also a health check).</summary>
public sealed record MasterStatus
{
    /// <summary>The game version the master was built with (OpenScct.Net, <see cref="BuildInfo.Version"/>).</summary>
    public string Version { get; init; } = BuildInfo.Version;

    /// <summary>
    /// The master's own version ("1.0.0", MasterVersion in OpenScct.Master.csproj), apart from the game's release
    /// numbers; null from a master older than this field.
    /// </summary>
    public string? MasterVersion { get; init; }

    /// <summary>The master's build number (its commit's UTC timestamp, yyyyMMdd.HHmm), or null.</summary>
    public string? MasterBuild { get; init; }

    /// <summary>The short commit hash the master was built from, or null.</summary>
    public string? MasterCommit { get; init; }

    public string? Motd { get; init; }

    public string? LatestVersion { get; init; }

    public string? MinVersion { get; init; }

    public int Servers { get; init; }

    public int RelaySessions { get; init; }

    public bool InviteOnly { get; init; }

    /// <summary>The ticket public key (base64 SubjectPublicKeyInfo), or null when the master has no accounts.</summary>
    public string? TicketKey { get; init; }

    /// <summary>True when players can sign in (Discord is set up).</summary>
    public bool Accounts { get; init; }
}

/// <summary>An error the master returned, as JSON <c>{ "error": "..." }</c>.</summary>
public sealed record MasterError
{
    public string Error { get; init; } = string.Empty;
}

/// <summary>A sign-in started from the game: the page to open, and what the game polls with.</summary>
public sealed record LinkStartResponse
{
    /// <summary>Short code shown in the game and on the page (for example <c>K7PX-29QD</c>).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The page to open in the browser (it goes on to Discord).</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Secret only the game knows; the poll needs it, so seeing the code is not enough to take the sign-in.</summary>
    public string PollSecret { get; init; } = string.Empty;

    public int ExpiresSeconds { get; init; }
}

public sealed record LinkPollRequest
{
    public string PollSecret { get; init; } = string.Empty;
}

public enum LinkStatus
{
    Pending,
    Done,
    Expired,
    Refused,
}

/// <summary>Where a sign-in is: still waiting, done (with the session token), expired or refused (banned, Discord said no).</summary>
public sealed record LinkPollResponse
{
    public LinkStatus Status { get; init; }

    /// <summary>The game's sign-in token once done; sent as <c>Authorization: Bearer</c> from then on.</summary>
    public string? SessionToken { get; init; }

    public AccountInfo? Account { get; init; }

    /// <summary>Why it was refused, for the player.</summary>
    public string? Error { get; init; }
}

/// <summary>The signed-in player's account as the game shows it.</summary>
public sealed record AccountInfo
{
    public long Id { get; init; }

    /// <summary>The claimed name, or null until the player picks one.</summary>
    public string? Name { get; init; }

    public bool Admin { get; init; }

    /// <summary>When the name may next be changed (null: now).</summary>
    public DateTimeOffset? NameChangeAllowed { get; init; }

    public AccountStats Stats { get; init; } = new();

    public IReadOnlyList<AchievementInfo> Achievements { get; init; } = [];

    /// <summary>The cosmetics the earned achievements unlock (OpenScct.Net.Cosmetics.CosmeticCatalog ids).</summary>
    public IReadOnlyList<string> Unlocked { get; init; } = [];

    /// <summary>Level, prestige and titles (null from masters before levels).</summary>
    public ProgressInfo? Progress { get; init; }

    /// <summary>A round the player dropped out of that is still being played, or null (and from older masters).</summary>
    public RejoinOffer? Rejoin { get; init; }
}

/// <summary>A round in progress the player left (a crash, a lost connection, a quit) and may rejoin on his side.</summary>
public sealed record RejoinOffer
{
    /// <summary>The server's master id (join it as a listed server, through <see cref="MasterApi.JoinPath"/>).</summary>
    public string ServerId { get; init; } = string.Empty;

    public string ServerName { get; init; } = string.Empty;

    public string Map { get; init; } = string.Empty;

    public int Mode { get; init; }

    public SessionPhase Phase { get; init; }
}

/// <summary>An account's level and prestige (spec/additions/progression.md).</summary>
public sealed record ProgressInfo
{
    public int Prestige { get; init; }

    public int Level { get; init; } = 1;

    /// <summary>XP into this prestige (level 1 is 0).</summary>
    public int Xp { get; init; }

    /// <summary>XP over every prestige.</summary>
    public long TotalXp { get; init; }

    /// <summary>The highest level reached in any prestige.</summary>
    public int BestLevel { get; init; } = 1;

    /// <summary>Unlocked titles (OpenScct.Net.Cosmetics.TitleCatalog ids).</summary>
    public IReadOnlyList<string> Titles { get; init; } = [];

    /// <summary>The last counted round's XP, or null before the first.</summary>
    public XpAward? LastAward { get; init; }
}

/// <summary>The XP one counted round gave, by part.</summary>
public sealed record XpAward
{
    public DateTimeOffset When { get; init; }

    public string Map { get; init; } = string.Empty;

    public int Score { get; init; }

    public int Finish { get; init; }

    public int Win { get; init; }

    public int Objectives { get; init; }

    /// <summary>XP from achievements earned in that round.</summary>
    public int Achievements { get; init; }

    public int Total { get; init; }

    public int LevelBefore { get; init; }

    public int LevelAfter { get; init; }

    public int Prestige { get; init; }

    /// <summary>Titles unlocked by that round.</summary>
    public IReadOnlyList<string> NewTitles { get; init; } = [];
}

public sealed record AccountStats
{
    public int Matches { get; init; }

    public int SpyWins { get; init; }

    public int MercWins { get; init; }

    public int Losses { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }
}

/// <summary>One achievement, earned or not.</summary>
public sealed record AchievementInfo
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>When it was earned, or null while locked.</summary>
    public DateTimeOffset? Earned { get; init; }

    /// <summary>Its group: General, Spy, Merc, Weapons or Map.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Progress towards it (equal to <see cref="Target"/> once earned).</summary>
    public int Current { get; init; }

    public int Target { get; init; } = 1;

    /// <summary>The cosmetics it unlocks (catalogue ids).</summary>
    public IReadOnlyList<string> Rewards { get; init; } = [];
}

public sealed record NameRequest
{
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// The player's settings that follow the account between PCs: <see cref="Data"/> is the game's own JSON (opaque to
/// the master), at most <see cref="MasterApi.MaxProfileBytes"/> bytes.
/// </summary>
public sealed record ProfileData
{
    public string Data { get; init; } = string.Empty;

    public DateTimeOffset Updated { get; init; }
}

/// <summary>A finished round on a trusted server, for stats and achievements.</summary>
public sealed record ResultsReport
{
    public string ServerId { get; init; } = string.Empty;

    public string Map { get; init; } = string.Empty;

    public int Mode { get; init; }

    /// <summary>0 spies, 1 mercs, 255 none (stopped: not counted).</summary>
    public int WinningTeam { get; init; } = 255;

    public int WinningReason { get; init; } = 255;

    public int ObjectivesTaken { get; init; }

    public int ObjectivesNeeded { get; init; }

    /// <summary>How long the round lasted, seconds (0 from servers that do not say).</summary>
    public int DurationSeconds { get; init; }

    public IReadOnlyList<ResultsReportRow> Rows { get; init; } = [];
}

public sealed record ResultsReportRow
{
    /// <summary>The signed-in player's account, or null for guests and bots.</summary>
    public long? AccountId { get; init; }

    /// <summary>The name shown in the round (for the master's match history).</summary>
    public string? Name { get; init; }

    /// <summary>0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    public int Score { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public bool Bot { get; init; }

    /// <summary>The round's achievement feats by name (OpenScct.Core.Engine.FeatKeys); the master ignores unknown names.</summary>
    public IReadOnlyDictionary<string, int> Feats { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// The master's answer to a round report. Masters before demos answered with an empty body, so read it as optional.
/// </summary>
public sealed record EquipmentAccountState(int Rank, byte[] Unlocks);

public sealed record ResultsResponse
{
    public IReadOnlyDictionary<long, EquipmentAccountState> Equipment { get; init; } = new Dictionary<long, EquipmentAccountState>();
    /// <summary>The stored round's id (demos of the round are uploaded under it).</summary>
    public long? MatchId { get; init; }
}

/// <summary>A trusted server asks to upload the demo of a round it reported.</summary>
public sealed record DemoUploadRequest
{
    /// <summary>The round, as the answer to its results gave it.</summary>
    public long MatchId { get; init; }

    /// <summary>The file's length in bytes; the upload link only takes exactly this many.</summary>
    public long Size { get; init; }

    /// <summary>SHA-256 of the file, 64 lower-case hex digits (players check their download against it).</summary>
    public string Sha256 { get; init; } = string.Empty;

    public int DurationSeconds { get; init; }

    /// <summary>The game version that recorded it (<c>BuildInfo.Version</c>).</summary>
    public string GameVersion { get; init; } = string.Empty;

    /// <summary>The net protocol the stream is in (hex), so players see whether their build can play it.</summary>
    public string NetVersion { get; init; } = string.Empty;

    /// <summary>The map's content hash (hex), so players see whether they have the same map.</summary>
    public string ContentHash { get; init; } = string.Empty;
}

/// <summary>Where to PUT a demo: a short-lived S3 link with the size signed in.</summary>
public sealed record DemoUploadResponse
{
    public long DemoId { get; init; }

    public string UploadUrl { get; init; } = string.Empty;

    /// <summary>The Content-Type the PUT must send (it is part of the signature).</summary>
    public string ContentType { get; init; } = "application/octet-stream";

    public DateTimeOffset UrlExpires { get; init; }
}

/// <summary>One page of ready demos, newest first.</summary>
public sealed record DemoListResponse
{
    public IReadOnlyList<DemoEntry> Demos { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Ready demos in all.</summary>
    public int Total { get; init; }
}

/// <summary>A ready demo with its round.</summary>
public sealed record DemoEntry
{
    public long Id { get; init; }

    public long MatchId { get; init; }

    public string Map { get; init; } = string.Empty;

    public int Mode { get; init; }

    public string ServerName { get; init; } = string.Empty;

    /// <summary>When the round ended (was reported).</summary>
    public DateTimeOffset Time { get; init; }

    public int DurationSeconds { get; init; }

    /// <summary>0 spies, 1 mercs, 255 none.</summary>
    public int WinningTeam { get; init; } = 255;

    public long Size { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string GameVersion { get; init; } = string.Empty;

    public string NetVersion { get; init; } = string.Empty;

    public string ContentHash { get; init; } = string.Empty;

    /// <summary>When the demo leaves the list (and the bucket).</summary>
    public DateTimeOffset Expires { get; init; }

    public IReadOnlyList<DemoPlayer> Players { get; init; } = [];
}

/// <summary>A player of a demo's round.</summary>
public sealed record DemoPlayer
{
    public string Name { get; init; } = string.Empty;

    /// <summary>0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    public int Score { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public bool Bot { get; init; }
}

/// <summary>Where to GET a demo: a short-lived S3 link.</summary>
public sealed record DemoDownloadResponse
{
    public string Url { get; init; } = string.Empty;

    public DateTimeOffset UrlExpires { get; init; }

    public long Size { get; init; }

    public string Sha256 { get; init; } = string.Empty;
}
