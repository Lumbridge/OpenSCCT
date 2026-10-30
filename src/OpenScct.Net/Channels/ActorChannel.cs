using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Replicates one actor over one connection (UE2 UActorChannel), following spec/native/net-replication.md rules
/// 9 to 18 and 23 to 25.
/// <list type="bullet">
/// <item>The first bunch is reliable (unreliable, and closing the channel, for bNetTemporary actors) and carries
/// the spawn information: a map actor (bStatic or bNoDelete) as its SVMap actor index, a dynamic one as its class,
/// location and rotation. Later property bunches are unreliable.</item>
/// <item>Properties are compared with the channel's shadow (<c>Recent</c>), which starts as the class defaults with
/// config properties zeroed, so a property equal to its default is not sent in the first bunch.</item>
/// <item>When the opening is first seen acknowledged, every property whose last send was unreliable is sent again;
/// a lost unreliable packet re-dirties the properties it carried (property retirement).</item>
/// <item>An object reference the receiver cannot resolve yet is sent as None and kept dirty until it can.</item>
/// <item>A bunch that overflows takes the last value back; the rest waits for the next update.</item>
/// <item>Role and RemoteRole are compared as they are and written into each other's slot: the role swap.</item>
/// </list>
/// On a client the channel spawns or binds its actor and applies what arrives; closing it destroys a dynamic actor
/// (not a bNetTemporary or torn-off one).
/// </summary>
[UcNative("UActorChannel::ReplicateActor", Exe = 0x109e1300, Editor = 0x11142700)]
public sealed class ActorChannel : Channel
{
    internal enum ReplicateResult : byte
    {
        Unchanged,
        Sent,
        Deferred,
    }

    private struct Retirement
    {
        public int PacketSeq;
        public bool Reliable;
    }

    private int[] _recent = [];
    private object?[] _recentObjects = [];
    private bool[] _dirty = [];

    // Map actors: properties the map placed away from the class default, not yet sent on this channel (rule 14a).
    private bool[]? _placedUnsent;
    private Retirement[] _retirement = [];
    private int[] _written = [];
    private bool[] _writtenMapped = [];
    private byte[] _conditionMemo = [];
    private bool _sentInitial;
    private bool _spawnAcked;
    private bool _mustStayDirty;
    private bool _stillInitial;

    public Actor? Actor { get; private set; }

    public NetClassInfo? ClassInfo { get; private set; }

    public RepLayout? Layout => ClassInfo?.Layout;

    /// <summary>Network id of the actor, the same on server and client (the server's actor id).</summary>
    public uint NetGuid { get; private set; }

    /// <summary>True when the channel is bound to a map actor (by SVMap index) rather than a spawned one.</summary>
    public bool IsMapActor { get; private set; }

    /// <summary>Client interpolation state, when the class interpolates its movement.</summary>
    public ProxyMotion? Motion { get; private set; }

    /// <summary>Server tick of the packet that carried the last bunch received on this channel.</summary>
    public long LastBunchTick { get; private set; } = -1;

    /// <summary>Bunches with property data this channel has sent.</summary>
    public int PropertyBunchesSent { get; private set; }

    /// <summary>Server: the actor changed since this channel's last update (UE2 ActorDirty).</summary>
    public bool ActorDirty { get; internal set; }

    /// <summary>Server: when relevancy last said yes (with its 0.5 s jitter), host microseconds.</summary>
    public long RelevantTime { get; internal set; }

    /// <summary>Server: when the last update went out without overflowing, host microseconds.</summary>
    public long LastUpdateTime { get; private set; }

    /// <summary>Server: the first bunch went out.</summary>
    public bool SentInitial => _sentInitial;

    /// <summary>Server: properties waiting to be sent again (lost, unmapped or overflowed).</summary>
    public bool HasDirtyProperties
    {
        get
        {
            foreach (bool d in _dirty)
            {
                if (d)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Server: a channel is ready when its connection is and it has fewer than 127 unacknowledged reliable bunches.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 3: RELIABLE_BUFFER - 1 = 127")]
    public bool IsNetReady => Connection.IsNetReady && NumOutRec < K.RELIABLE_BUFFER - 1;

    internal ActorNetState? State { get; private set; }

    // ---- server side ---------------------------------------------------------------------------------------

    [UcNative("UActorChannel::SetChannelActor", Editor = 0x11140b30)]
    internal void SetChannelActor(Actor actor, NetClassInfo info, ActorNetState state)
    {
        Actor = actor;
        ClassInfo = info;
        State = state;
        NetGuid = (uint)actor.ActorId;
        RepLayout layout = info.Layout;
        _recent = new int[layout.WordCount];
        _recentObjects = new object?[layout.ObjectCount];
        layout.CopyDefaults(_recent, _recentObjects);
        _placedUnsent = Connection.Driver.MapBinding.PlacedDiffers(actor)?.Clone() as bool[];
        _dirty = new bool[layout.PropertyCount];
        _retirement = new Retirement[layout.PropertyCount];
        _written = new int[layout.PropertyCount];
        _writtenMapped = new bool[layout.PropertyCount];
        _conditionMemo = new byte[layout.Conditions.Length];
        for (int i = 0; i < _retirement.Length; i++)
        {
            _retirement[i].PacketSeq = -1;
        }

        Connection.ActorChannelByActorId[actor.ActorId] = this;
        state.AddChannel(this);
    }

    /// <summary>
    /// Sends what changed since the last update, under the conditions of this connection. Deferred means part of
    /// the update did not fit (overflow) or the bunch could not be sent.
    /// </summary>
    internal ReplicateResult ReplicateActor(long passStamp)
    {
        Actor? actor = Actor;
        NetClassInfo? info = ClassInfo;
        ActorNetState? state = State;
        if (actor is null || info is null || state is null || Closing)
        {
            return ReplicateResult.Unchanged;
        }

        NetConnection connection = Connection;
        NetDriver driver = connection.Driver;
        RepLayout layout = info.Layout;
        bool first = !_sentInitial;
        bool temporary = actor.bNetTemporary;
        bool reliable = first && !temporary;

        // Rule 9: the first time the opening is seen acknowledged, resend what went out unreliably before it.
        if (!first && OpenAcked && !_spawnAcked)
        {
            _spawnAcked = true;
            for (int p = 0; p < _retirement.Length; p++)
            {
                if (_retirement[p].PacketSeq >= 0 && !_retirement[p].Reliable)
                {
                    _dirty[p] = true;
                }
            }
        }

        // Rules 10, 11, 13: the flags of this update.
        bool owner = driver.IsNetOwner(actor, connection);
        ENetRole remoteRole = actor.RemoteRole;
        if (remoteRole == ENetRole.ROLE_AutonomousProxy && actor.Instigator is { } instigator && !owner &&
            !driver.IsNetOwner(instigator, connection))
        {
            remoteRole = ENetRole.ROLE_SimulatedProxy;
        }

        var flags = new RepFlags(first || _stillInitial, owner, ActorDirty || first, actor.Role, remoteRole, driver.NetMode);
        _mustStayDirty = false;
        _stillInitial = false;
        actor.bNetOwner = owner;
        state.EnsureCaptured(passStamp);

        // Per-connection values of the role and ownership slots, patched into the shared snapshot for this update.
        int[] words = state.Words;
        RepProperty[] props = layout.Properties;
        int roleWord = layout.RoleIndex >= 0 ? props[layout.RoleIndex].WordOffset : -1;
        int remoteWord = layout.RemoteRoleIndex >= 0 ? props[layout.RemoteRoleIndex].WordOffset : -1;
        int savedRole = roleWord >= 0 ? words[roleWord] : 0;
        int savedRemote = remoteWord >= 0 ? words[remoteWord] : 0;
        if (remoteWord >= 0)
        {
            words[remoteWord] = (int)remoteRole;
        }

        RepProperty? ownerProp = layout.FindProperty("bNetOwner");
        int ownerWord = ownerProp is { NetSlot: ENetVarSlot.bNetOwner } ? ownerProp.WordOffset : -1;
        int savedOwner = ownerWord >= 0 ? words[ownerWord] : 0;
        if (ownerWord >= 0)
        {
            words[ownerWord] = owner ? 1 : 0;
        }

        try
        {
            return WriteUpdate(actor, info, layout, flags, first, temporary, reliable, words, state.Objects);
        }
        finally
        {
            if (roleWord >= 0)
            {
                words[roleWord] = savedRole;
            }

            if (remoteWord >= 0)
            {
                words[remoteWord] = savedRemote;
            }

            if (ownerWord >= 0)
            {
                words[ownerWord] = savedOwner;
            }

            actor.bNetOwner = false;
        }
    }

    private ReplicateResult WriteUpdate(Actor actor, NetClassInfo info, RepLayout layout, in RepFlags flags, bool first,
        bool temporary, bool reliable, int[] words, object?[] objects)
    {
        NetConnection connection = Connection;
        NetDriver driver = connection.Driver;
        FOutBunch bunch = connection.BeginBunch(this, reliable);
        bunch.bOpen = first;
        bunch.bClose = first && temporary;
        FBitWriter w = bunch.Writer;
        if (first)
        {
            WriteSpawnInfo(w, actor, info, driver);
        }

        Array.Clear(_conditionMemo);
        RepProperty[] props = layout.Properties;
        RepCondition[] conditions = layout.Conditions;
        int count = 0;
        bool overflow = false;
        for (int i = 0; i < props.Length; i++)
        {
            RepProperty p = props[i];

            // Rule 13: the class's list (conditions met and value changed from Recent), plus every index in Dirty
            // whatever its condition says now: a value lost in a dropped packet or sent unmapped goes again even
            // when, as for Controller.Pawn behind bNetDirty, its condition no longer holds.
            if (!_dirty[i])
            {
                byte memo = _conditionMemo[p.ConditionSlot];
                if (memo == 0)
                {
                    memo = conditions[p.ConditionSlot].Evaluate(actor, flags) ? (byte)2 : (byte)1;
                    _conditionMemo[p.ConditionSlot] = memo;
                }

                if ((memo != 2 && !(driver.PropertyPush is { } push && push(actor, connection, p))) ||
                    (memo == 2 && driver.PropertyWithhold is { } withhold && withhold(actor, connection, p)) ||
                    (RepLayout.Same(p, words, _recent, objects, _recentObjects) && !(_placedUnsent?[i] ?? false)))
                {
                    continue;
                }
            }

            int mark = w.NumBits;
            w.WriteInt((uint)p.WriteIndex, (uint)layout.MaxRepIndex);
            bool mapped = true;
            RepLeaf[] leaves = p.Leaves;
            for (int l = 0; l < leaves.Length; l++)
            {
                RepLeaf leaf = leaves[l];
                mapped &= NetWire.WriteWords(w, leaf.Kind, leaf.EnumCount, words.AsSpan(p.WordOffset + leaf.WordOffset),
                    objects.AsSpan(p.ObjectOffset + leaf.ObjectOffset), connection);
            }

            if (w.IsError)
            {
                w.Rewind(mark);
                overflow = true;
                break;
            }

            _written[count] = i;
            _writtenMapped[count] = mapped;
            count++;
            if (!mapped)
            {
                _mustStayDirty = true;
            }
        }

        if (count == 0 && !first)
        {
            ActorDirty = _mustStayDirty || overflow;
            return overflow ? ReplicateResult.Deferred : ReplicateResult.Unchanged;
        }

        int packetSeq = connection.SendBunch(bunch);
        if (packetSeq < 0)
        {
            return ReplicateResult.Deferred;
        }

        for (int n = 0; n < count; n++)
        {
            int i = _written[n];
            RepProperty p = props[i];
            if (_writtenMapped[n])
            {
                Array.Copy(words, p.WordOffset, _recent, p.WordOffset, p.Words);
                Array.Copy(objects, p.ObjectOffset, _recentObjects, p.ObjectOffset, p.Objects);
                _dirty[i] = false;
                if (_placedUnsent is not null)
                {
                    _placedUnsent[i] = false;
                }
            }
            else
            {
                _dirty[i] = true;
            }

            _retirement[i].PacketSeq = packetSeq;
            _retirement[i].Reliable = reliable;
        }

        _sentInitial = true;
        PropertyBunchesSent++;
        if (!overflow)
        {
            LastUpdateTime = driver.Time;
        }

        _stillInitial = flags.bNetInitial && (overflow || (!temporary && _mustStayDirty));
        ActorDirty = _mustStayDirty || overflow;
        if (temporary)
        {
            connection.SentTemporaries.Add(actor.ActorId);
            connection.ReleaseChannel(this);
        }

        return overflow ? ReplicateResult.Deferred : ReplicateResult.Sent;
    }

    /// <summary>Rule 12: a map actor by its SVMap index, a dynamic actor by class, location and (when set) rotation.</summary>
    private void WriteSpawnInfo(FBitWriter w, Actor actor, NetClassInfo info, NetDriver driver)
    {
        int mapIndex = (actor.bStatic || actor.bNoDelete) ? driver.MapBinding.IndexOf(actor) : -1;
        w.WriteBit(mapIndex >= 0);
        if (mapIndex >= 0)
        {
            IsMapActor = true;
            w.WriteIntPacked((uint)mapIndex);
            w.WriteIntPacked(NetGuid);
            return;
        }

        w.WriteIntPacked((uint)info.ClassIndex);
        w.WriteIntPacked(NetGuid);
        w.WriteFloat(actor.Location.X);
        w.WriteFloat(actor.Location.Y);
        w.WriteFloat(actor.Location.Z);
        bool rotation = !actor.Rotation.IsZero();
        w.WriteBit(rotation);
        if (rotation)
        {
            w.WriteCompressedRotator(actor.Rotation);
        }
    }

    protected internal override void ReceivedNak(ushort packetSeq)
    {
        for (int p = 0; p < _retirement.Length; p++)
        {
            if (_retirement[p].PacketSeq == packetSeq && !_retirement[p].Reliable)
            {
                _dirty[p] = true;
            }
        }
    }

    // ---- receiving -----------------------------------------------------------------------------------------

    protected internal override void ReceivedBunch(FInBunch bunch)
    {
        NetDriver driver = Connection.Driver;
        if (driver.IsServer)
        {
            ReceivedFromClient(bunch);
            return;
        }

        FBitReader r = bunch.Reader;
        bool spawned = false;
        if (Actor is null)
        {
            if (!bunch.bOpen)
            {
                return;
            }

            if (!OpenFromBunch(r, driver, out spawned))
            {
                return;
            }
        }

        Actor target = Actor!;
        RepLayout layout = ClassInfo!.Layout;
        target.bNetOwner = driver.IsLocallyOwned(target);
        Motion?.BeginReceive(target);
        driver.ReceivingTick = bunch.PacketTick;
        driver.PreNetReceive?.Invoke(target);
        NetValue value;
        while (!r.AtEnd)
        {
            int repIndex = (int)r.ReadInt((uint)layout.MaxRepIndex);
            if (r.IsError)
            {
                break;
            }

            if (repIndex < layout.PropertyCount)
            {
                RepProperty p = layout.Properties[repIndex];
                foreach (RepLeaf leaf in p.Leaves)
                {
                    value = default;
                    NetWire.Read(r, leaf.Kind, leaf.EnumCount, ref value, Connection);
                    if (r.IsError)
                    {
                        break;
                    }

                    leaf.Apply(target, value);
                }
            }
            else
            {
                driver.ReceivedFunction(this, layout.Functions[repIndex - layout.PropertyCount], r);
            }

            if (r.IsError || Actor is null)
            {
                break;
            }
        }

        if (Actor is not null && !r.IsError)
        {
            driver.PostNetReceive?.Invoke(target);
        }

        Motion?.EndReceive(target, bunch.PacketTick);
        LastBunchTick = bunch.PacketTick;
        if (r.IsError)
        {
            Connection.Close("Malformed actor bunch");
            return;
        }

        if (spawned)
        {
            target.eventPostNetBeginPlay();
        }

        if (Actor is not null && bunch.bOpen)
        {
            driver.OnClientChannelOpened(this);
        }

        if (Actor is { bTearOff: true } torn && torn.Role != ENetRole.ROLE_Authority)
        {
            TearOff(torn);
        }
    }

    private bool OpenFromBunch(FBitReader r, NetDriver driver, out bool spawned)
    {
        spawned = false;
        bool map = r.ReadBit();
        if (map)
        {
            uint mapIndex = r.ReadIntPacked();
            uint guid = r.ReadIntPacked();
            Actor? bound = r.IsError ? null : driver.MapBinding.Get((int)mapIndex);
            NetClassInfo? boundInfo = bound is null ? null : driver.Classes.Find(bound.GetType());
            if (bound is null || boundInfo is null)
            {
                driver.Stats.UnboundMapActors++;
                return false;
            }

            Bind(bound, boundInfo, guid, isMap: true);
            return true;
        }

        uint classIndex = r.ReadIntPacked();
        uint netGuid = r.ReadIntPacked();
        var location = new FVector(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        FRotator rotation = r.ReadBit() ? r.ReadCompressedRotator() : default;
        NetClassInfo? info = r.IsError ? null : driver.Classes.Get((int)classIndex);
        if (info is null)
        {
            Connection.Close(r.IsError ? "Malformed actor open" : "Received invalid actor class");
            return false;
        }

        World world = driver.World ?? throw new InvalidOperationException("The client driver has no World to spawn into.");
        Actor actor = info.Factory();

        // ULevel::SpawnActor with bRemoteOwned: the client copy starts with the class roles exchanged.
        (actor.Role, actor.RemoteRole) = (actor.RemoteRole, actor.Role);
        Actor? live = world.Level.SpawnActor(actor, location, rotation, bRemoteOwned: true);
        if (live is null)
        {
            driver.Stats.FailedSpawns++;
            return false;
        }

        Bind(live, info, netGuid, isMap: false);
        spawned = true;
        return true;
    }

    private void Bind(Actor actor, NetClassInfo info, uint netGuid, bool isMap)
    {
        Actor = actor;
        ClassInfo = info;
        NetGuid = netGuid;
        IsMapActor = isMap;
        NetDriver driver = Connection.Driver;
        driver.AddNetGuid(netGuid, actor);
        Connection.ActorChannelByActorId[actor.ActorId] = this;
        if (driver.Settings.ProxyMode == ProxyMotionMode.Interpolate && info.Layout.InterpolateMovement)
        {
            Motion = new ProxyMotion();
        }
    }

    /// <summary>Rule 25: the actor leaves the channel and becomes the client's own (ROLE_Authority).</summary>
    private void TearOff(Actor actor)
    {
        actor.Role = ENetRole.ROLE_Authority;
        actor.RemoteRole = ENetRole.ROLE_None;
        Connection.Driver.RemoveNetGuid(NetGuid);
        if (Connection.ActorChannelByActorId.TryGetValue(actor.ActorId, out ActorChannel? mapped) && ReferenceEquals(mapped, this))
        {
            Connection.ActorChannelByActorId.Remove(actor.ActorId);
        }

        Actor = null;
        Motion = null;
    }

    private void ReceivedFromClient(FInBunch bunch)
    {
        Actor? actor = Actor;
        RepLayout? layout = Layout;
        if (actor is null || layout is null || actor.bDeleteMe)
        {
            return;
        }

        NetDriver driver = Connection.Driver;
        FBitReader r = bunch.Reader;
        while (!r.AtEnd)
        {
            int repIndex = (int)r.ReadInt((uint)layout.MaxRepIndex);
            int function = repIndex - layout.PropertyCount;
            if (r.IsError || function < 0)
            {
                // Clients never send properties.
                Connection.Close("Client sent actor properties");
                return;
            }

            driver.ReceivedFunction(this, layout.Functions[function], r);
            if (r.IsError)
            {
                Connection.Close("Malformed function call");
                return;
            }
        }
    }

    // ---- function calls ------------------------------------------------------------------------------------

    /// <summary>Starts a bunch calling <paramref name="function"/>; write the arguments, then <see cref="SendFunction"/>.</summary>
    public FOutBunch BeginFunction(RepFunction function, bool reliable)
    {
        ArgumentNullException.ThrowIfNull(function);
        RepLayout layout = Layout ?? throw new InvalidOperationException("The channel has no actor.");
        FOutBunch bunch = Connection.BeginBunch(this, reliable);
        bunch.Writer.WriteInt((uint)(layout.PropertyCount + function.Index), (uint)layout.MaxRepIndex);
        return bunch;
    }

    /// <summary>Starts a bunch calling the function at <paramref name="functionIndex"/> of the layout.</summary>
    public FOutBunch BeginFunction(int functionIndex, bool reliable) =>
        BeginFunction(Layout?.Functions[functionIndex] ?? throw new InvalidOperationException("The channel has no actor."), reliable);

    public int SendFunction(FOutBunch bunch) => Connection.SendBunch(bunch);

    /// <summary>Writes and sends one call of <paramref name="function"/> (rule 19.7: non-bool parameters lead with a non-zero bit).</summary>
    internal int SendCall(RepFunction function, NetCallArgs? args)
    {
        FOutBunch bunch = BeginFunction(function, function.IsReliable);
        NetWire.WriteArguments(bunch.Writer, function, args, Connection);
        return Connection.SendBunch(bunch);
    }

    // ---- flush complete and clean up -----------------------------------------------------------------------

    internal void OnFlushComplete(long tick)
    {
        // Reliable bunches waiting behind a gap carry newer state: not a "no change".
        if (Motion is not null && LastBunchTick != tick && NumInRec == 0)
        {
            Motion.Hold(tick);
        }
    }

    protected internal override void CleanUp()
    {
        NetDriver driver = Connection.Driver;
        Actor? actor = Actor;
        if (actor is null)
        {
            return;
        }

        if (Connection.ActorChannelByActorId.TryGetValue(actor.ActorId, out ActorChannel? mapped) && ReferenceEquals(mapped, this))
        {
            Connection.ActorChannelByActorId.Remove(actor.ActorId);
        }

        if (driver.IsServer)
        {
            State?.RemoveChannel(this);
        }
        else
        {
            driver.RemoveNetGuid(NetGuid);
            if (!actor.bDeleteMe && !actor.bNetTemporary && !actor.bStatic && !actor.bNoDelete)
            {
                actor.XLevel.Destroy(actor);
            }
        }
    }
}
