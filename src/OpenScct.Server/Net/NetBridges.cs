using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The objective port's calls to other machines (<see cref="IObjectiveNet"/>) routed through the network driver:
/// the multicasts go to every client with an open channel (the SCCT broadcast flag), owner-client calls go to the
/// owning connection, or run here for a player without one.
/// </summary>
public sealed class NetObjectiveBridge : IObjectiveNet
{
    private static readonly ClassRef GameMessages = new("SBase.SGameMessages");

    private readonly NetDriver _driver;
    private readonly NetCallArgs _args = new();

    public NetObjectiveBridge(NetDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    public void AddRemoveObjectiveOnClientHud(SObjective Objective, bool Add) =>
        _driver.ProcessRemoteFunction(Objective, nameof(SObjective.AddRemoveObjectiveOnClientHud), _args.Clear().Bool(0, Add));

    public void AddRemoveObjectiveForRadar(SObjectiveTrigger Trigger, bool bAdd) =>
        _driver.ProcessRemoteFunction(Trigger, nameof(SObjectiveTrigger.AddRemoveObjectiveForRadar), _args.Clear().Bool(0, bAdd));

    public void AddRemoveActorOnClientHud(STriggerInteraction Interaction, SPlayerController? Instigator, bool Add) =>
        _driver.ProcessRemoteFunction(Interaction, nameof(STriggerInteraction.AddRemoveActorOnClientHud),
            _args.Clear().Object(0, Instigator).Bool(1, Add));

    public void SetFlagCarrier(SFlag Flag, bool taken) =>
        _driver.ProcessRemoteFunction(Flag, nameof(SFlag.SetFlagCarrier), _args.Clear().Bool(0, taken));

    public void UpdateBombInteraction(SBombTargetInteraction Interaction, bool Remove) =>
        _driver.ProcessRemoteFunction(Interaction, nameof(SBombTargetInteraction.Update), _args.Clear().Bool(0, Remove));

    public void AddObjetOnClient(SInteractionManager Manager, SInteractObject? Obj, int i)
    {
        ENetCallResult result = _driver.ProcessRemoteFunction(Manager, nameof(SInteractionManager.AddObjetOnClient),
            _args.Clear().Object(0, Obj).Int(1, i));
        if (result == ENetCallResult.RunLocally)
        {
            Manager.AddObjetOnClient(Obj, i);
        }
    }

    public void RemoveInteractionObjOnClient(SInteractionManager Manager, SInteractObject? Object)
    {
        ENetCallResult result = _driver.ProcessRemoteFunction(Manager, nameof(SInteractionManager.RemoveInteractionObjOnClient),
            _args.Clear().Object(0, Object));
        if (result == ENetCallResult.RunLocally)
        {
            Manager.RemoveInteractionObjOnClient(Object);
        }
    }

    public void ReceiveLocalizedMessage(PlayerController Receiver, int Switch, Actor? OptionalObject)
    {
        _args.Clear().Set(0, new NetValue { O = GameMessages.Path }).Int(1, Switch).Object(4, OptionalObject);
        if (_driver.ProcessRemoteFunction(Receiver, "ReceiveLocalizedMessage", _args) == ENetCallResult.RunLocally)
        {
            ObjectiveNetLocal.Instance.ReceiveLocalizedMessage(Receiver, Switch, OptionalObject);
        }
    }
}

/// <summary>
/// The game rules' calls to a player's machine (<see cref="IGameHost.ClientCall"/> on a server) and to the server
/// (<see cref="IGameHost.ServerCall"/> on a client) sent as the replicated function the <see cref="GameCall"/> names.
/// The call's fields fill the function's parameters by type, in order: floats from Float1 then Float2, names from
/// Name1 then Name2, bools from Bool, ints, bytes and enums from Int, strings from Text, classes from Class then
/// Class2, assets from Asset, rotators from Rotation, vectors from Vector, PlayerReplicationInfo parameters from Pri1
/// then Pri2, other objects from Actor then Object. Calls that are not replicated functions, or that run here, go to the fallback
/// host (the view layer, or nothing on a dedicated server).
/// </summary>
public sealed class NetGameHost : IGameHost
{
    private readonly NetDriver _driver;
    private readonly IGameHost _fallback;
    private readonly NetCallArgs _args = new();

    public NetGameHost(NetDriver driver, IGameHost? fallback = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _fallback = fallback ?? NullGameHost.Instance;
    }

    private static readonly Name NAME_resetConnectionPing = new("resetConnectionPing");

    /// <summary>The host that shows calls on this machine (the view layer), or the null host.</summary>
    public IGameHost Fallback => _fallback;

    public void ClientCall(PlayerController target, in GameCall call)
    {
        if (_driver.World?.IsReplaying == true) return;
        // resetConnectionPing is the engine's own (net-driver.md rule 9): it resets the server's adaptive net speed
        // for the player's connection and is never sent.
        if (_driver.IsServer && call.Function == NAME_resetConnectionPing)
        {
            if (_driver.OwningConnection(target) is { } connection)
            {
                _driver.ResetConnectionPing(connection);
            }

            return;
        }

        if (!_driver.IsServer || Route(target, call) == ENetCallResult.RunLocally)
        {
            _fallback.ClientCall(target, call);
        }
    }

    /// <summary>
    /// Client: calls the server sends to this player's controller that no port runs (sounds, messages, menus, HUD)
    /// go to the view layer as the <see cref="GameCall"/> they came from.
    /// </summary>
    public void DeliverUnportedCalls()
    {
        _driver.Functions.Fallback = (self, f, args, _) =>
        {
            if (!_driver.IsServer && self is PlayerController pc)
            {
                _fallback.ClientCall(pc, ToGameCall(f, args));
            }
        };
    }

    /// <summary>The <see cref="GameCall"/> a received call came from: the inverse of <see cref="Fill"/>.</summary>
    public static GameCall ToGameCall(RepFunction function, NetCallArgs args)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(args);
        var call = new GameCall(new Name(function.Name));
        int floats = 0;
        int names = 0;
        int classes = 0;
        int pris = 0;
        int objects = 0;
        foreach (RepParam p in function.Parameters)
        {
            if (p.Kind is not RepLeafKind kind)
            {
                continue;
            }

            NetValue v = args.Values[p.ValueOffset];
            switch (kind)
            {
                case RepLeafKind.Float:
                    call = floats++ == 0 ? call with { Float1 = v.F } : call with { Float2 = v.F };
                    break;
                case RepLeafKind.Name:
                {
                    Name n = v.O is string text && text.Length > 0 ? new Name(text) : Name.None;
                    call = names++ == 0 ? call with { Name1 = n } : call with { Name2 = n };
                    break;
                }

                case RepLeafKind.Bool:
                    call = call with { Bool = v.I != 0 };
                    break;
                case RepLeafKind.Byte:
                case RepLeafKind.Enum:
                case RepLeafKind.Int:
                    call = call with { Int = v.I };
                    break;
                case RepLeafKind.String:
                    call = call with { Text = v.O as string };
                    break;
                case RepLeafKind.Class:
                {
                    var c = new ClassRef(v.O as string ?? string.Empty);
                    call = classes++ == 0 ? call with { Class = c } : call with { Class2 = c };
                    break;
                }

                case RepLeafKind.Asset:
                    call = call with { Asset = new AssetRef(v.O as string ?? string.Empty) };
                    break;
                case RepLeafKind.Rotator:
                    call = call with { Rotation = v.R };
                    break;
                case RepLeafKind.Vector:
                    call = call with { Vector = v.V };
                    break;
                case RepLeafKind.Object:
                    if (p.ClrType is { } t && typeof(PlayerReplicationInfo).IsAssignableFrom(t))
                    {
                        var pri = v.O as PlayerReplicationInfo;
                        call = pris++ == 0 ? call with { Pri1 = pri } : call with { Pri2 = pri };
                    }
                    else if (objects++ == 0)
                    {
                        call = call with { Actor = v.O as Actor, Object = v.O as UObject };
                    }
                    else
                    {
                        call = call with { Object = v.O as UObject };
                    }

                    break;
            }
        }

        return call;
    }

    public void ServerCall(PlayerController sender, in GameCall call)
    {
        if (_driver.IsServer || Route(sender, call) == ENetCallResult.RunLocally)
        {
            _fallback.ServerCall(sender, call);
        }
    }

    private ENetCallResult Route(Actor actor, in GameCall call)
    {
        string name = call.Function.ToString();
        RepFunction? f = _driver.Classes.Find(actor.GetType())?.Layout.FindFunction(name);
        if (f is null)
        {
            return ENetCallResult.RunLocally;
        }

        Fill(f, call, _args.Clear());
        return _driver.ProcessRemoteFunction(actor, name, _args);
    }

    /// <summary>Fills <paramref name="args"/> from <paramref name="call"/> by parameter type, in order.</summary>
    public static void Fill(RepFunction function, in GameCall call, NetCallArgs args)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(args);
        int floats = 0;
        int names = 0;
        int classes = 0;
        int pris = 0;
        int objects = 0;
        foreach (RepParam p in function.Parameters)
        {
            if (p.Kind is not RepLeafKind kind)
            {
                continue;
            }

            ref NetValue v = ref args.Values[p.ValueOffset];
            switch (kind)
            {
                case RepLeafKind.Float:
                    v.F = floats++ == 0 ? call.Float1 : call.Float2;
                    break;
                case RepLeafKind.Name:
                {
                    Name n = names++ == 0 ? call.Name1 : call.Name2;
                    v.O = n.IsNone ? null : n.ToString();
                    break;
                }

                case RepLeafKind.Bool:
                    v.I = call.Bool ? 1 : 0;
                    break;
                case RepLeafKind.Byte:
                case RepLeafKind.Enum:
                case RepLeafKind.Int:
                    v.I = call.Int;
                    break;
                case RepLeafKind.String:
                    v.O = call.Text;
                    break;
                case RepLeafKind.Class:
                    v.O = (classes++ == 0 ? call.Class : call.Class2).Path;
                    break;
                case RepLeafKind.Asset:
                    v.O = call.Asset.Path;
                    break;
                case RepLeafKind.Rotator:
                    v.R = call.Rotation;
                    break;
                case RepLeafKind.Vector:
                    v.V = call.Vector;
                    break;
                case RepLeafKind.Object:
                    if (p.ClrType is { } t && typeof(PlayerReplicationInfo).IsAssignableFrom(t))
                    {
                        v.O = pris++ == 0 ? call.Pri1 : call.Pri2;
                    }
                    else
                    {
                        v.O = objects++ == 0 ? (object?)call.Actor ?? call.Object : call.Object;
                    }

                    break;
            }
        }
    }

    public bool IsGUIInGame(PlayerController? player) => _fallback.IsGUIInGame(player);

    public int NbPendingClientOnServer()
    {
        int pending = 0;
        foreach (NetConnection c in _driver.ClientConnections)
        {
            if (c.State == EConnectionState.USOCK_Pending || (c.State == EConnectionState.USOCK_Open && !c.bJoined))
            {
                pending++;
            }
        }

        return pending;
    }

    public string GetURLMap() => _fallback.GetURLMap();

    public bool IsPsx2() => false;

    public bool IsPBClientEnabled() => false;

    public void OnScriptEvent(Actor source, int id) => _fallback.OnScriptEvent(source, id);

    public int GetPing(PlayerController player)
    {
        NetConnection? c = _driver.IsServer ? _driver.OwningConnection(player) : _driver.ServerConnection;
        return c is { Stats.HasRtt: true } ? (int)(c.Stats.RttMicros / 1000) : 0;
    }

    public NetworkType NetType() => _driver.Settings.IsLan ? NetworkType.NET_LAN : _fallback.NetType();

    public bool IsDedicatedServerMode() => false;
}

/// <summary>The map's world BSP as the relevancy line check (actors, movers and static meshes do not block).</summary>
[UcNative("AActor::IsNetRelevantFor", Exe = 0x10a8f760)]
public sealed class PhysicsLineOfSight : INetLineOfSight
{
    private readonly CollisionWorld _collision;

    public PhysicsLineOfSight(PhysicsWorld physics)
    {
        ArgumentNullException.ThrowIfNull(physics);
        _collision = physics.Collision;
    }

    public bool IsBlockedByWorld(FVector start, FVector end) =>
        _collision.LineCheck(out _, null, end, start, default, ETraceFlags.TRACE_Level | ETraceFlags.TRACE_StopAtFirstHit);
}
