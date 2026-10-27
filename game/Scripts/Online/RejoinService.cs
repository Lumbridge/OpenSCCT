using System;
using System.Net;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScctGame.Match;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>
/// The round this game was last playing, kept in <c>user://rejoin.cfg</c> from the moment the round loads until the
/// visit is back in a lobby, so that after a crash, a lost connection or a quit the game can offer to rejoin it
/// (docs/ONLINE.md, "Rejoining a round"). Smoke runs never write it.
/// </summary>
public sealed record RejoinRecord
{
    public const string FilePath = "user://rejoin.cfg";

    /// <summary>A record older than this is not offered (the round is long over).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

    public string ServerName { get; init; } = string.Empty;

    /// <summary>The server's master id (rejoin through the master), or null.</summary>
    public string? MasterId { get; init; }

    /// <summary>The typed or LAN address (rejoin directly) when the server has no master id, or null.</summary>
    public string? Address { get; init; }

    public string? Password { get; init; }

    public string Map { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; }

    public bool Fresh => DateTimeOffset.UtcNow - At < MaxAge && (MasterId is { Length: > 0 } || Address is { Length: > 0 });

    public static RejoinRecord? Load()
    {
        var file = new ConfigFile();
        if (OpenScctGame.Options.SettingsStore.Isolated || file.Load(FilePath) != Error.Ok)
        {
            return null;
        }

        string Text(string key) => file.GetValue("round", key, string.Empty).AsString();
        return new RejoinRecord
        {
            ServerName = Text("server"),
            MasterId = Text("master_id") is { Length: > 0 } id ? id : null,
            Address = Text("address") is { Length: > 0 } address ? address : null,
            Password = Text("password") is { Length: > 0 } password ? password : null,
            Map = Text("map"),
            At = DateTimeOffset.FromUnixTimeSeconds(file.GetValue("round", "at", 0L).AsInt64()),
        };
    }

    public void Save()
    {
        if (OpenScctGame.Options.SettingsStore.Isolated)
        {
            return;
        }

        var file = new ConfigFile();
        file.SetValue("round", "server", ServerName);
        file.SetValue("round", "master_id", MasterId ?? string.Empty);
        file.SetValue("round", "address", Address ?? string.Empty);
        file.SetValue("round", "password", Password ?? string.Empty);
        file.SetValue("round", "map", Map);
        file.SetValue("round", "at", At.ToUnixTimeSeconds());
        file.Save(FilePath);
    }

    public static void Delete()
    {
        if (!OpenScctGame.Options.SettingsStore.Isolated && FileAccess.FileExists(FilePath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
        }
    }
}

/// <summary>A round the player may rejoin, as the main menu offers it.</summary>
public sealed record RejoinChoice(string ServerName, string Map, string? MasterId, string? Address, string? Password);

/// <summary>
/// Offers to rejoin a round the player dropped out of (docs/ONLINE.md, "Rejoining a round"): a node under the scene
/// tree's root that follows the online visit (<see cref="Follow"/>: the round is remembered while it is played), and
/// when the main menu shows, checks that the remembered round's server is still playing it (the master's entry, or a
/// server query for a typed address). A signed-in player is also offered the round his account dropped out of on any
/// PC: the master's account answer carries it (<see cref="AccountInfo.Rejoin"/>). Rejoin joins the server again; the
/// server puts the player back on his side with the score and lives he left with.
/// </summary>
public partial class RejoinService : Node
{
    public const string NodeName = "RejoinService";

    private const long QueryTimeoutMs = 4000;

    private RejoinRecord? _record;
    private bool _loaded;
    private bool _checked;
    private bool _leftByChoice;
    private bool _rejoining;
    private string? _declinedMasterId;
    private OnlineClient? _remembered;
    private Task<ServerEntry>? _masterCheck;
    private ServerQuery? _query;
    private ulong _queryUntil;
    private AccountService? _accounts;

    /// <summary>The round the main menu offers to rejoin, or null.</summary>
    public RejoinChoice? Offer { get; private set; }

    /// <summary>Raised when <see cref="Offer"/> changes.</summary>
    public event Action? Changed;

    public static RejoinService Get(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Root.GetNodeOrNull<RejoinService>(NodeName) is { } existing)
        {
            return existing;
        }

        var service = new RejoinService { Name = NodeName };
        tree.Root.AddChild(service);
        return service;
    }

    public override void _Ready()
    {
        _accounts = AccountService.Get(GetTree());
        _accounts.Changed += OnAccountChanged;
    }

    public override void _ExitTree()
    {
        if (_accounts is not null)
        {
            _accounts.Changed -= OnAccountChanged;
        }

        _query?.Dispose();
        _query = null;
    }

    private RejoinRecord? Record
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _record = RejoinRecord.Load();
            }

            return _record;
        }
    }

    /// <summary>
    /// Follows the visit's state: a round (loading, match, results) is remembered with how to reach its server; the
    /// lobby forgets it (the round is over). A kick forgets it, and so does a rejoin that failed.
    /// </summary>
    public void Follow(OnlineClient client, OnlineClientState state, string? masterId, string? password)
    {
        ArgumentNullException.ThrowIfNull(client);
        switch (state)
        {
            case OnlineClientState.Loading or OnlineClientState.Match or OnlineClientState.Results:
                _rejoining = false;
                _leftByChoice = false;
                // The account's offer from the master (made before this round) is used up.
                _declinedMasterId = AccountService.Get(GetTree()).Account?.Rejoin?.ServerId ?? _declinedMasterId;
                SetOffer(null);
                if (!ReferenceEquals(_remembered, client) || Record?.Map != client.CurrentMap)
                {
                    _remembered = client;
                    Remember(new RejoinRecord
                    {
                        ServerName = client.Lobby.ServerName.Length > 0 ? client.Lobby.ServerName : client.Target.Name,
                        MasterId = masterId,
                        Address = masterId is null && client.Target.Attempts is [{ Kind: ConnectKind.Direct } only] ? only.Address : null,
                        Password = password,
                        Map = client.CurrentMap ?? client.Lobby.Setup.Map,
                        At = DateTimeOffset.UtcNow,
                    });
                }

                break;
            case OnlineClientState.Lobby:
                _rejoining = false;
                _remembered = null;
                Forget();
                break;
            case OnlineClientState.Failed when client.Kicked || _rejoining:
                GD.Print("SVM_REJOIN " + (client.Kicked ? "kicked: the round is not offered again" : "the rejoin failed: " + client.Error));
                _rejoining = false;
                Forget();
                break;
        }
    }

    /// <summary>The player left the visit himself: the round is not offered again until the game starts next.</summary>
    public void LeftByChoice()
    {
        if (Record is not null)
        {
            _leftByChoice = true;
        }
    }

    /// <summary>The main menu is up: checks the remembered round once per drop (and shows the account's offer).</summary>
    public void Check()
    {
        if (Offer is not null || _masterCheck is not null || _query is not null)
        {
            return;
        }

        if (Record is { } record && !_leftByChoice && !_checked)
        {
            _checked = true;
            if (!record.Fresh)
            {
                Forget();
            }
            else if (record.MasterId is { } id && AccountService.HasMaster)
            {
                GD.Print($"SVM_REJOIN asking the master whether {record.ServerName} is still playing");
                _masterCheck = AccountService.Call(c => c.ServerAsync(id), withToken: false);
                return;
            }
            else if (record.Address is { } address && StartQuery(address))
            {
                return;
            }
        }

        OnAccountChanged();
    }

    /// <summary>Rejoin pressed: joins the round's server again (a listed one through the master, with the ticket).</summary>
    public void Rejoin()
    {
        if (Offer is not { } offer)
        {
            return;
        }

        SetOffer(null);
        _rejoining = true;
        _checked = false;
        GD.Print($"SVM_REJOIN rejoining {offer.ServerName}");
        SceneTree tree = GetTree();
        if (offer.MasterId is { } id)
        {
            FrontEnd.ShowServerBrowser(tree, id, offer.ServerName, offer.Password);
        }
        else if (offer.Address is { } address)
        {
            OnlineService.Get(tree).Join(JoinTarget.FromAddress(address), offer.Password);
        }
    }

    /// <summary>Not now: the round is not offered again.</summary>
    public void Decline()
    {
        if (Offer is { MasterId: { } id })
        {
            _declinedMasterId = id;
        }

        GD.Print("SVM_REJOIN declined");
        SetOffer(null);
        Forget();
    }

    public override void _Process(double delta)
    {
        if (_masterCheck is { IsCompleted: true } check)
        {
            _masterCheck = null;
            if (check.IsCompletedSuccessfully && InProgress(check.Result.Phase) && check.Result.Kind == ServerKind.Game)
            {
                OfferRecord(check.Result.Name, check.Result.Map);
            }
            else
            {
                GD.Print("SVM_REJOIN the remembered round is over: " + (check.IsCompletedSuccessfully
                    ? check.Result.Phase.ToString()
                    : AccountService.ErrorOf(check, "the master does not list the server")));
                Forget();
                OnAccountChanged();
            }
        }

        if (_query is { } query)
        {
            query.Poll();
            if (query.Results.Count > 0 || Time.GetTicksMsec() >= _queryUntil)
            {
                ServerQueryResult? answer = query.Results.Count > 0 ? query.Results[^1] : null;
                query.Dispose();
                _query = null;
                if (answer is not null && InProgress(answer.Info.Phase))
                {
                    OfferRecord(answer.Info.Name, answer.Info.Map);
                }
                else
                {
                    GD.Print("SVM_REJOIN the remembered round is over: " + (answer?.Info.Phase.ToString() ?? "no answer from the server"));
                    Forget();
                    OnAccountChanged();
                }
            }
        }
    }

    /// <summary>A round is being played (the results count as over: a series's next game shows as loading).</summary>
    private static bool InProgress(SessionPhase phase) => phase is SessionPhase.Loading or SessionPhase.Match;

    private bool StartQuery(string address)
    {
        try
        {
            IPEndPoint? endpoint = LiteNetLibTransport.Resolve(address);
            if (endpoint is null)
            {
                return false;
            }

            _query = new ServerQuery(new StopwatchClock());
            _query.QueryServer(endpoint);
            _queryUntil = Time.GetTicksMsec() + QueryTimeoutMs;
            GD.Print($"SVM_REJOIN asking {address} whether its round is still being played");
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Net.Sockets.SocketException or ArgumentException)
        {
            GD.Print("SVM_REJOIN cannot ask the server: " + e.Message);
            _query?.Dispose();
            _query = null;
            return false;
        }
    }

    private void OfferRecord(string serverName, string map)
    {
        if (Record is not { } record)
        {
            return;
        }

        GD.Print($"SVM_REJOIN offering {serverName} ({map})");
        SetOffer(new RejoinChoice(serverName.Length > 0 ? serverName : record.ServerName, map, record.MasterId, record.Address, record.Password));
    }

    /// <summary>The master's account answer names a round the account dropped out of (any PC): offered when nothing else is.</summary>
    private void OnAccountChanged()
    {
        if (Offer is not null || _masterCheck is not null || _query is not null || _leftByChoice ||
            AccountService.Get(GetTree()).Account?.Rejoin is not { ServerId.Length: > 0 } rejoin ||
            rejoin.ServerId == _declinedMasterId || OnlineService.Get(GetTree()).Client is not null)
        {
            return;
        }

        GD.Print($"SVM_REJOIN the master says this account may rejoin {rejoin.ServerName} ({rejoin.Map})");
        string? password = Record is { MasterId: { } id } r && id == rejoin.ServerId ? r.Password : null;
        SetOffer(new RejoinChoice(rejoin.ServerName, rejoin.Map, rejoin.ServerId, null, password));
    }

    private void Remember(RejoinRecord record)
    {
        _record = record;
        _loaded = true;
        _checked = false;
        record.Save();
    }

    private void Forget()
    {
        if (Record is null)
        {
            return;
        }

        _record = null;
        _checked = false;
        RejoinRecord.Delete();
    }

    private void SetOffer(RejoinChoice? offer)
    {
        if (Offer == offer)
        {
            return;
        }

        Offer = offer;
        Changed?.Invoke();
    }
}
