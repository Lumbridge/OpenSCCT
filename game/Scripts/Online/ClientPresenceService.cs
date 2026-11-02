using System;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;

namespace OpenScctGame.Online;

/// <summary>One lease per interactive client process, surviving scene changes.</summary>
public partial class ClientPresenceService : Node
{
    private Guid _session = Guid.NewGuid();
    private (string Master, string Invite) _identity;
    private string _token = string.Empty;
    private ulong _next;
    private Task<PresenceResponse>? _pending;

    public static void Get(SceneTree tree)
    {
        if (tree.Root.GetNodeOrNull<ClientPresenceService>(nameof(ClientPresenceService)) is null)
            tree.Root.AddChild(new ClientPresenceService { Name = nameof(ClientPresenceService), ProcessMode = ProcessModeEnum.Always });
    }

    public override void _Process(double delta)
    {
        var identity = (OnlineSettings.Current.MasterUrl.TrimEnd('/'), OnlineSettings.Current.InviteKey);
        string token = AccountSettings.Current.Token;
        if (_identity != identity)
        {
            _identity = identity;
            _session = Guid.NewGuid();
            _next = 0;
        }
        if (_token != token) { _token = token; _next = 0; }
        if (_pending is { IsCompleted: true })
        {
            _ = _pending.Exception; // Observe failure; retry on the normal interval without interrupting play.
            _pending = null;
        }
        if (_pending is not null || AccountSettings.Isolated || identity.Item1.Length == 0 || Time.GetTicksMsec() < _next) return;
        _next = Time.GetTicksMsec() + 30_000;
        Guid session = _session;
        _pending = AccountService.Call(c => c.PresenceAsync(session));
    }
}
