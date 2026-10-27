using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// The replicated classes of this build: every concrete actor class of the Core, Engine and SBase packages with
/// its generated layout, the net layer's hand-written members of original classes (the combat calls on
/// SPlayerController, the device events on LevelInfo), then the runtime-only classes. Server and client must build
/// the same list.
/// </summary>
public static class NetClasses
{
    /// <summary>
    /// A new registry. It can be shared by every driver of a process (its caches are thread safe), which saves
    /// building the layouts once per driver.
    /// </summary>
    public static NetClassRegistry Create(RepConditionSource conditions = RepConditionSource.Native)
    {
        var registry = new NetClassRegistry(DeviceClasses.Index, conditions);
        registry.Extend<SPlayerController>(NetCombatBridge.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.LevelInfo>(NetDeviceEvents.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.PlayerReplicationInfo>(PlayerLookNet.CreateExtension());
        registry.Register<CubeWalker>(CubeWalkerNet.CreateExtension());
        return registry;
    }
}
