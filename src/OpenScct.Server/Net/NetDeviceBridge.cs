using System;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// The device port's replicated functions (<see cref="IDeviceNet"/>) through the network driver: the call becomes
/// the replicated function it names on the device's channel, its fields filling the parameters as
/// <see cref="NetGameHost.Fill"/> does. A broadcast function (SMessageTrigger.DisplayMessage,
/// SNotifyNoise.ChangeStateToAll) reaches every client with an open channel for the device and also runs on a listen
/// server; a function that is not replicated, or whose condition does not hold here, runs here.
/// </summary>
public sealed class NetDeviceBridge : IDeviceNet
{
    private readonly NetDriver _driver;
    private readonly NetCallArgs _args = new();

    public NetDeviceBridge(NetDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    public bool Call(Actor Self, in GameCall Call)
    {
        ArgumentNullException.ThrowIfNull(Self);
        string name = Call.Function.ToString();
        RepFunction? f = _driver.Classes.Find(Self.GetType())?.Layout.FindFunction(name);
        if (f is null)
        {
            return true;
        }

        NetGameHost.Fill(f, Call, _args.Clear());
        return _driver.ProcessRemoteFunction(Self, name, _args) is ENetCallResult.RunLocally or ENetCallResult.SentAndRunLocally;
    }
}
