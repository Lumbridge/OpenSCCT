using OpenScct.Core.Engine;

namespace OpenScct.Net;

/// <summary>
/// The inputs a replication condition reads from the net driver rather than from properties (the
/// <see cref="OpenScct.Core.Uc.RepNetVar"/> values), for one evaluation: the roles as the replicating side sees them
/// (on a server, <see cref="RemoteRole"/> after the autonomous downgrade), the per-channel flags and the level's
/// net mode. spec/net/model.md rules 3 to 5, spec/native/net-replication.md rules 10, 11 and 13.
/// </summary>
public struct RepFlags
{
    public RepFlags(bool bNetInitial, bool bNetOwner, bool bNetDirty, ENetRole Role, ENetRole RemoteRole,
        ENetMode NetMode = ENetMode.NM_DedicatedServer)
    {
        this.bNetInitial = bNetInitial;
        this.bNetOwner = bNetOwner;
        this.bNetDirty = bNetDirty;
        this.Role = Role;
        this.RemoteRole = RemoteRole;
        this.NetMode = NetMode;
    }

    public bool bNetInitial;
    public bool bNetOwner;
    public bool bNetDirty;
    public ENetRole Role;
    public ENetRole RemoteRole;

    /// <summary><c>Level.NetMode</c> of the evaluating machine.</summary>
    public ENetMode NetMode;

    /// <summary>The same inputs with Role and RemoteRole exchanged (the sender's view of a received call).</summary>
    public readonly RepFlags Swapped() => new(bNetInitial, bNetOwner, bNetDirty, RemoteRole, Role, NetMode);

    public override readonly string ToString() =>
        $"Role={Role} RemoteRole={RemoteRole} bNetInitial={bNetInitial} bNetOwner={bNetOwner} bNetDirty={bNetDirty} NetMode={NetMode}";
}
