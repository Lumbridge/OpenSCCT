using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
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
        controller.Functions.Add(MercShockStream.RequestExtension());
        controller.Functions.Add(ResponsiveSmokeReplication.RequestExtension());
        registry.Extend<SPlayerController>(controller);
        registry.Extend<OpenScct.Core.Engine.PlayerController>(PawnSoundReplication.CreateExtension());
        var shockRequests = new RepExtension();
        shockRequests.Functions.Add(MercShockStream.RequestExtension());
        shockRequests.Functions.Add(ResponsiveSmokeReplication.RequestExtension());
        registry.Extend<OpenScct.Core.Engine.PlayerController>(shockRequests);
        for (int i = 0; i < registry.Count; i++)
        {
            System.Type type = registry.Classes[i].Type;
            if (typeof(SProjGrenade).IsAssignableFrom(type))
            {
                // Their authored Projectile default is a disposable one-shot. Losing that datagram makes a
                // damaging grenade invisible. Keep normal channels until authoritative destruction instead.
                registry.Extend(type, new RepExtension { PersistentActorChannel = true });
            }
            if (typeof(OpenScct.Core.Engine.GameReplicationInfo).IsAssignableFrom(type))
            {
                var rules = new RepExtension();
                rules.Properties.Add(new RepExtensionProperty("SidearmsEnabled", "SidearmsEnabled", RepLeafKind.Bool));
                rules.Properties.Add(new RepExtensionProperty("ResponsiveSmokeEnabled", "ResponsiveSmokeEnabled", RepLeafKind.Bool));
                rules.Properties.Add(new RepExtensionProperty("MatchPlayers", "MatchPlayers", RepLeafKind.Int));
                registry.Extend(type, rules);
            }
            if (typeof(OpenScct.Core.Engine.Mover).IsAssignableFrom(type))
            {
                var path = new RepExtension();
                path.Properties.Add(new("PathTarget", "PathTarget", RepLeafKind.Int));
                path.Properties.Add(new("PathPrevious", "PathPrevious", RepLeafKind.Int));
                // A spinning mover's rotation, about once a second (Mover.ClientSync.cs).
                path.Properties.Add(new(nameof(OpenScct.Core.Engine.Mover.SpinSample), nameof(OpenScct.Core.Engine.Mover.SpinSample), RepLeafKind.RotatorFine));
                registry.Extend(type, path);
            }
            if (typeof(SBasePawn).IsAssignableFrom(type))
            {
                RepExtension pawn = PawnStateReplication.CreateExtension();
                RepExtension equipment = SidearmNet.PawnExtension();
                pawn.Properties.AddRange(equipment.Properties);
                pawn.Functions.AddRange(equipment.Functions);
                registry.Extend(type, pawn);
            }
            if (typeof(SVitre).IsAssignableFrom(type))
            {
                var glass = new RepExtension();
                glass.Properties.Add(new(nameof(SGamePlayObject.GlassImpactLocation), nameof(SGamePlayObject.GlassImpactLocation), RepLeafKind.VectorFine));
                glass.Properties.Add(new(nameof(SGamePlayObject.GlassImpactDirection), nameof(SGamePlayObject.GlassImpactDirection), RepLeafKind.VectorFine));
                glass.Properties.Add(new(nameof(SGamePlayObject.GlassImpactSerial), nameof(SGamePlayObject.GlassImpactSerial), RepLeafKind.Int));
                registry.Extend(type, glass);
            }
            if (typeof(SInteractObject).IsAssignableFrom(type))
            {
                // The script property list omits UsableBy. Startup copies the initial team from the
                // owner; map managers can change it later. Our spawned client entries need the
                // authority's live value, including U_NO_ONE, before presenting adjacent team variants.
                var interaction = new RepExtension();
                interaction.Properties.Add(new("InteractionOwner", nameof(SInteractObject.Owner), RepLeafKind.Object));
                interaction.Properties.Add(new(nameof(SInteractObject.UsableBy), nameof(SInteractObject.UsableBy), RepLeafKind.Enum));
                registry.Extend(type, interaction);
            }
        }
        SidearmNet.Register(registry);
        registry.Extend<SSmokeEmitter>(ResponsiveSmokeReplication.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.LevelInfo>(NetDeviceEvents.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.PlayerReplicationInfo>(PlayerLookNet.CreateExtension());
        var cameras = new RepExtension();
        cameras.Properties.Add(new RepExtensionProperty(nameof(SCamNetwork.DisabledSeconds), nameof(SCamNetwork.DisabledSeconds), RepLeafKind.Int));
        registry.Extend<SCamNetwork>(cameras);
        registry.Register<CubeWalker>(CubeWalkerNet.CreateExtension());
        registry.Register<AnimationController>(AnimationController.Replication());
        registry.Register<Conveyor>(ConveyorExtension());
        return registry;
    }

    /// <summary>
    /// Svm.Conveyor: the running state and the phase anchor, so clients carry riders with the server's state and draw
    /// the belt in the same place (spec/additions/conveyors.md#network).
    /// </summary>
    private static RepExtension ConveyorExtension()
    {
        var conveyor = new RepExtension();
        conveyor.Properties.Add(new RepExtensionProperty(nameof(Conveyor.bOn), nameof(Conveyor.bOn), RepLeafKind.Bool));
        conveyor.Properties.Add(new RepExtensionProperty(nameof(Conveyor.Speed), nameof(Conveyor.Speed), RepLeafKind.Float));
        conveyor.Properties.Add(new RepExtensionProperty(nameof(Conveyor.PhaseAtChange), nameof(Conveyor.PhaseAtChange), RepLeafKind.Float));
        conveyor.Properties.Add(new RepExtensionProperty(nameof(Conveyor.ChangeTick), nameof(Conveyor.ChangeTick), RepLeafKind.Int));
        conveyor.Properties.Add(new RepExtensionProperty(nameof(Conveyor.SpeedBeforeChange), nameof(Conveyor.SpeedBeforeChange), RepLeafKind.Float));
        return conveyor;
    }
}
