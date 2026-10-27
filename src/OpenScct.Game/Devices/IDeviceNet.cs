using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Devices;

/// <summary>
/// The replicated functions of map devices that are not a player controller's (SMessageTrigger.DisplayMessage,
/// SNotifyNoise.ChangeStateToAll): the call goes out as the function <see cref="GameCall.Function"/> names on
/// <c>Self</c>, its parameters filled from the call's fields by type as <c>NetGameHost</c> fills them. Devices find
/// it as the level service <c>Level.GetService&lt;IDeviceNet&gt;()</c>; without it every call runs here only.
/// </summary>
public interface IDeviceNet
{
    /// <summary>
    /// Sends <paramref name="Call"/> on <paramref name="Self"/> where its replication condition says. True when the
    /// function must also (or only) run on this machine: no network, a broadcast on a listen server, or a call whose
    /// condition does not hold here.
    /// </summary>
    bool Call(Actor Self, in GameCall Call);
}

/// <summary><see cref="IDeviceNet"/> without a network: every call runs here.</summary>
public sealed class DeviceNetLocal : IDeviceNet
{
    public static readonly DeviceNetLocal Instance = new();

    private DeviceNetLocal()
    {
    }

    public bool Call(Actor Self, in GameCall Call) => true;
}
