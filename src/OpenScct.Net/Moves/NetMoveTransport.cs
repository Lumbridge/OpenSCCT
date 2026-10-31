using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Moves;

/// <summary>The move protocol objects of one player controller on one machine.</summary>
public sealed class MoveEndpoint
{
    public MoveEndpoint(ServerMoves? server, ClientMoves? client, SequencedMoveTransport? sequenced = null)
    {
        Server = server;
        Client = client;
        Sequenced = sequenced;
    }

    /// <summary>The server half (on the server), or null.</summary>
    public ServerMoves? Server { get; }

    /// <summary>The owning client's half (on that client), or null.</summary>
    public ClientMoves? Client { get; }

    public SequencedMoveTransport? Sequenced { get; }
}

public interface IMoveEndpointSource
{
    MoveEndpoint? EndpointOf(Actor controller);
}

/// <summary>
/// The move protocol's messages as the replicated functions of SBase.SPlayerController, sent through
/// <see cref="NetDriver.ProcessRemoteFunction"/> (so their conditions, reliability and encoding are the generated
/// ones: vectors rounded, floats exact), and the receive handlers that hand them to the
/// <see cref="ServerMoves"/> or <see cref="ClientMoves"/> of the controller.
/// </summary>
public sealed class NetMoveTransport : IMoveTransport
{
    public const string ControllerClass = "SBase.SPlayerController";

    private static readonly string[] Functions =
    [
        "MyServerMove", "MyShortServerMove", "MyServerMoveWithPCRot", "BigServerMove", "UnreliableReplicateBigServerMove",
        "MyShortClientAdjustPosition", "MyClientAdjustPosition", "ClientReinitPawnBecauseOfDivergences", "TellServerIAMReadyToReinit",
    ];

    private readonly NetDriver _driver;
    private readonly Actor _controller;
    private readonly NetCallArgs _args = new();
    private readonly Dictionary<string, RepFunction?> _functions = new(StringComparer.Ordinal);

    public NetMoveTransport(NetDriver driver, Actor controller)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>Reliable bunches waiting for acknowledgement on the connection (the client's batching window).</summary>
    public int OutstandingReliable => (_driver.IsServer ? _driver.OwningConnection(_controller) : _driver.ServerConnection)?.ReliableBunchesInFlight ?? 0;

    /// <summary>Calls whose function the controller's class does not replicate (an unsupported controller class).</summary>
    public long Unroutable { get; private set; }

    private RepFunction? Function(string name)
    {
        if (!_functions.TryGetValue(name, out RepFunction? f))
        {
            f = _driver.Classes.Find(_controller.GetType())?.Layout.FindFunction(name);
            _functions[name] = f;
        }

        return f;
    }

    private void Set(RepFunction f, string param, in NetValue value)
    {
        foreach (RepParam p in f.Parameters)
        {
            if (string.Equals(p.Name, param, StringComparison.OrdinalIgnoreCase))
            {
                _args.Values[p.ValueOffset] = value;
                return;
            }
        }
    }

    /// <summary>Starts a call: the function, with the arguments cleared, or null when the class does not replicate it.</summary>
    private RepFunction? Begin(string name)
    {
        if (Function(name) is not { } f)
        {
            Unroutable++;
            return null;
        }

        _args.Clear();
        return f;
    }

    private void Send(string name) => _driver.ProcessRemoteFunction(_controller, name, _args);

    private void SetOld(RepFunction f, int oldTimeDelta, int oldAccel)
    {
        if (oldTimeDelta >= 0)
        {
            Set(f, "OldTimeDelta", NetValue.FromInt(oldTimeDelta));
            Set(f, "OldAccel", NetValue.FromInt(oldAccel));
        }
    }

    public void MyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        if (Begin("MyServerMove") is { } f)
        {
            Set(f, "TimeStamp", NetValue.FromFloat(TimeStamp));
            Set(f, "InAccel", NetValue.FromVector(InAccel));
            Set(f, "ClientLoc", NetValue.FromVector(ClientLoc));
            Set(f, "View", NetValue.FromInt(View));
            Set(f, "DiversInfos", NetValue.FromInt(DiversInfos));
            SetOld(f, OldTimeDelta, OldAccel);
            Send("MyServerMove");
        }
    }

    public void MyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        if (Begin("MyShortServerMove") is { } f)
        {
            FillShort(f, TimeStamp, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel);
            Send("MyShortServerMove");
        }
    }

    public void MyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        if (Begin("MyServerMoveWithPCRot") is { } f)
        {
            FillShort(f, TimeStamp, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel);
            Send("MyServerMoveWithPCRot");
        }
    }

    private void FillShort(RepFunction f, float timeStamp, FVector clientLoc, int view, byte divers, int oldTimeDelta, int oldAccel)
    {
        Set(f, "TimeStamp", NetValue.FromFloat(timeStamp));
        Set(f, "ClientLoc", NetValue.FromVector(clientLoc));
        Set(f, "View", NetValue.FromInt(view));
        Set(f, "DiversInfos", NetValue.FromInt(divers));
        SetOld(f, oldTimeDelta, oldAccel);
    }

    public void BigServerMove(float MSATime, float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos,
        int Val0, int Val1)
    {
        if (Begin("BigServerMove") is { } f)
        {
            Set(f, "MSATime", NetValue.FromFloat(MSATime));
            Set(f, "TimeStamp", NetValue.FromFloat(TimeStamp));
            Set(f, "InAccel", NetValue.FromVector(InAccel));
            Set(f, "ClientLoc", NetValue.FromVector(ClientLoc));
            Set(f, "Vel", NetValue.FromVector(Vel));
            Set(f, "View", NetValue.FromInt(View));
            Set(f, "DiversInfos", NetValue.FromInt(DiversInfos));
            Set(f, "Val0", NetValue.FromInt(Val0));
            Set(f, "Val1", NetValue.FromInt(Val1));
            Send("BigServerMove");
        }
    }

    public void UnreliableReplicateBigServerMove(in BigServerMoveData move)
    {
        if (Begin("UnreliableReplicateBigServerMove") is { } f)
        {
            FillStruct(f, move);
            Send("UnreliableReplicateBigServerMove");
        }
    }

    private void FillStruct(RepFunction f, in BigServerMoveData m)
    {
        if (f.Parameters.Length == 0)
        {
            return;
        }

        RepParam p = f.Parameters[0];
        for (int l = 0; l < p.Leaves.Length; l++)
        {
            string member = p.Leaves[l].Fields[^1].Name;
            _args.Values[p.ValueOffset + l] = member switch
            {
                "MSATime" => NetValue.FromFloat(m.MSATime),
                "TimeStamp" => NetValue.FromFloat(m.TimeStamp),
                "View" => NetValue.FromInt(m.View),
                "Val0" => NetValue.FromInt(m.Val0),
                "Val1" => NetValue.FromInt(m.Val1),
                "iDiversInfos" => NetValue.FromInt(m.iDiversInfos),
                "InAccel" => NetValue.FromVector(m.InAccel),
                "ClientLoc" => NetValue.FromVector(m.ClientLoc),
                "Vel" => NetValue.FromVector(m.Vel),
                "ZipP1" => NetValue.FromVector(m.ZipP1),
                "DifZipP2" => NetValue.FromVector(m.DifZipP2),
                "vect3" => NetValue.FromVector(m.vect3),
                "vectUnit" => NetValue.FromVector(m.vectUnit),
                _ => default,
            };
        }
    }

    public void MyShortClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, Actor? NewBase)
    {
        if (Begin("MyShortClientAdjustPosition") is { } f)
        {
            Set(f, "TimeStamp", NetValue.FromFloat(TimeStamp));
            Set(f, "NewLocX", NetValue.FromFloat(NewLocX));
            Set(f, "NewLocY", NetValue.FromFloat(NewLocY));
            Set(f, "NewLocZ", NetValue.FromFloat(NewLocZ));
            Set(f, "NewBase", NetValue.FromObject(NewBase));
            Send("MyShortClientAdjustPosition");
        }
    }

    public void MyClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY, float NewVelZ,
        Actor? NewBase)
    {
        if (Begin("MyClientAdjustPosition") is { } f)
        {
            Set(f, "TimeStamp", NetValue.FromFloat(TimeStamp));
            Set(f, "NewLocX", NetValue.FromFloat(NewLocX));
            Set(f, "NewLocY", NetValue.FromFloat(NewLocY));
            Set(f, "NewLocZ", NetValue.FromFloat(NewLocZ));
            Set(f, "NewVelX", NetValue.FromFloat(NewVelX));
            Set(f, "NewVelY", NetValue.FromFloat(NewVelY));
            Set(f, "NewVelZ", NetValue.FromFloat(NewVelZ));
            Set(f, "NewBase", NetValue.FromObject(NewBase));
            Send("MyClientAdjustPosition");
        }
    }

    public void ClientReinitPawnBecauseOfDivergences(byte RT, float MSATime, FVector vPos, FRotator rRot, Actor? aBase)
    {
        if (Begin("ClientReinitPawnBecauseOfDivergences") is { } f)
        {
            Set(f, "RT", NetValue.FromInt(RT));
            Set(f, "MSATime", NetValue.FromFloat(MSATime));
            Set(f, "vPos", NetValue.FromVector(vPos));
            Set(f, "rRot", NetValue.FromRotator(rRot));
            Set(f, "aBase", NetValue.FromObject(aBase));
            Send("ClientReinitPawnBecauseOfDivergences");
        }
    }

    public void TellServerIAMReadyToReinit()
    {
        if (Begin("TellServerIAMReadyToReinit") is not null)
        {
            Send("TellServerIAMReadyToReinit");
        }
    }

    // ---- receiving ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Routes the move functions received on <paramref name="driver"/> for SPlayerController (and subclasses) to
    /// the endpoint <paramref name="resolve"/> gives for the controller; calls for a controller without one are
    /// dropped.
    /// </summary>
    public static void Register(NetDriver driver, Func<Actor, MoveEndpoint?> resolve, string controllerClass = ControllerClass)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(resolve);
        foreach (string name in Functions)
        {
            string function = name;
            driver.Functions.Register(controllerClass, function, (self, f, args, from) =>
            {
                if (resolve(self) is { } endpoint)
                {
                    Dispatch(endpoint, function, f, args);
                }
            });
        }
    }

    private static NetValue Arg(RepFunction f, NetCallArgs args, string name)
    {
        foreach (RepParam p in f.Parameters)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return args.Values[p.ValueOffset];
            }
        }

        return default;
    }

    private static int OldDelta(RepFunction f, NetCallArgs args)
    {
        int delta = Arg(f, args, "OldTimeDelta").I;
        return delta == 0 ? -1 : delta;
    }

    private static void Dispatch(MoveEndpoint endpoint, string name, RepFunction f, NetCallArgs args)
    {
        ServerMoves? s = endpoint.Server;
        ClientMoves? c = endpoint.Client;
        switch (name)
        {
            case "MyServerMove":
                s?.MyServerMove(Arg(f, args, "TimeStamp").F, Arg(f, args, "InAccel").V, Arg(f, args, "ClientLoc").V, Arg(f, args, "View").I,
                    (byte)Arg(f, args, "DiversInfos").I, OldDelta(f, args), Arg(f, args, "OldAccel").I);
                break;
            case "MyShortServerMove":
                s?.MyShortServerMove(Arg(f, args, "TimeStamp").F, Arg(f, args, "ClientLoc").V, Arg(f, args, "View").I,
                    (byte)Arg(f, args, "DiversInfos").I, Arg(f, args, "OldTimeDelta").I, Arg(f, args, "OldAccel").I);
                break;
            case "MyServerMoveWithPCRot":
                s?.MyServerMoveWithPCRot(Arg(f, args, "TimeStamp").F, Arg(f, args, "ClientLoc").V, Arg(f, args, "View").I,
                    (byte)Arg(f, args, "DiversInfos").I, OldDelta(f, args), Arg(f, args, "OldAccel").I);
                break;
            case "BigServerMove":
                s?.BigServerMove(Arg(f, args, "MSATime").F, Arg(f, args, "TimeStamp").F, Arg(f, args, "InAccel").V, Arg(f, args, "ClientLoc").V,
                    Arg(f, args, "Vel").V, Arg(f, args, "View").I, (byte)Arg(f, args, "DiversInfos").I, Arg(f, args, "Val0").I,
                    Arg(f, args, "Val1").I);
                break;
            case "UnreliableReplicateBigServerMove":
                s?.UnreliableReplicateBigServerMove(ReadStruct(f, args));
                break;
            case "MyShortClientAdjustPosition":
                c?.MyShortClientAdjustPosition(Arg(f, args, "TimeStamp").F, Arg(f, args, "NewLocX").F, Arg(f, args, "NewLocY").F,
                    Arg(f, args, "NewLocZ").F, Arg(f, args, "NewBase").O as Actor);
                break;
            case "MyClientAdjustPosition":
                c?.MyClientAdjustPosition(Arg(f, args, "TimeStamp").F, Arg(f, args, "NewLocX").F, Arg(f, args, "NewLocY").F,
                    Arg(f, args, "NewLocZ").F, Arg(f, args, "NewVelX").F, Arg(f, args, "NewVelY").F, Arg(f, args, "NewVelZ").F,
                    Arg(f, args, "NewBase").O as Actor);
                break;
            case "ClientReinitPawnBecauseOfDivergences":
                c?.ClientReinitPawnBecauseOfDivergences((byte)Arg(f, args, "RT").I, Arg(f, args, "MSATime").F, Arg(f, args, "vPos").V,
                    Arg(f, args, "rRot").R, Arg(f, args, "aBase").O as Actor);
                break;
            case "TellServerIAMReadyToReinit":
                s?.TellServerIAMReadyToReinit();
                break;
        }
    }

    private static BigServerMoveData ReadStruct(RepFunction f, NetCallArgs args)
    {
        var m = new BigServerMoveData();
        if (f.Parameters.Length == 0)
        {
            return m;
        }

        RepParam p = f.Parameters[0];
        for (int l = 0; l < p.Leaves.Length; l++)
        {
            NetValue v = args.Values[p.ValueOffset + l];
            switch (p.Leaves[l].Fields[^1].Name)
            {
                case "MSATime":
                    m.MSATime = v.F;
                    break;
                case "TimeStamp":
                    m.TimeStamp = v.F;
                    break;
                case "View":
                    m.View = v.I;
                    break;
                case "Val0":
                    m.Val0 = v.I;
                    break;
                case "Val1":
                    m.Val1 = v.I;
                    break;
                case "iDiversInfos":
                    m.iDiversInfos = v.I;
                    break;
                case "InAccel":
                    m.InAccel = v.V;
                    break;
                case "ClientLoc":
                    m.ClientLoc = v.V;
                    break;
                case "Vel":
                    m.Vel = v.V;
                    break;
                case "ZipP1":
                    m.ZipP1 = v.V;
                    break;
                case "DifZipP2":
                    m.DifZipP2 = v.V;
                    break;
                case "vect3":
                    m.vect3 = v.V;
                    break;
                case "vectUnit":
                    m.vectUnit = v.V;
                    break;
            }
        }

        return m;
    }
}
