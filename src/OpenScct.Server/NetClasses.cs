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
        RepExtension controller = NetCombatBridge.CreateExtension();
        controller.Functions.AddRange(OpenScct.Net.Moves.SequencedMoveTransport.CreateExtension().Functions);
        controller.Functions.AddRange(PawnSoundReplication.CreateExtension().Functions);
        controller.Functions.AddRange(NetMovementBridge.OwnerActionsExtension().Functions);
        registry.Extend<SPlayerController>(controller);
        registry.Extend<OpenScct.Core.Engine.PlayerController>(PawnSoundReplication.CreateExtension());
        for (int i = 0; i < registry.Count; i++)
        {
            System.Type type = registry.Classes[i].Type;
            if (typeof(SBasePawn).IsAssignableFrom(type))
            {
                RepExtension pawn = PawnStateReplication.CreateExtension();
                RepExtension equipment = SidearmNet.PawnExtension();
                pawn.Properties.AddRange(equipment.Properties);
                pawn.Functions.AddRange(equipment.Functions);
                registry.Extend(type, pawn);
            }
        }
        SidearmNet.Register(registry);
        registry.Extend<OpenScct.Core.Engine.LevelInfo>(NetDeviceEvents.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.PlayerReplicationInfo>(PlayerLookNet.CreateExtension());
        var cameras = new RepExtension();
        cameras.Properties.Add(new RepExtensionProperty(nameof(SCamNetwork.DisabledSeconds), nameof(SCamNetwork.DisabledSeconds), RepLeafKind.Int));
        registry.Extend<SCamNetwork>(cameras);
        registry.Register<CubeWalker>(CubeWalkerNet.CreateExtension());
        return registry;
    }
}
