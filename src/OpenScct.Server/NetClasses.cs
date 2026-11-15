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
            if (typeof(SObjectiveTrigger).IsAssignableFrom(type))
            {
                var ps2Objective = new RepExtension();
                ps2Objective.Properties.Add(new(nameof(SObjectiveTrigger.bPS2Objective), nameof(SObjectiveTrigger.bPS2Objective), RepLeafKind.Bool));
                ps2Objective.Properties.Add(new(nameof(SObjectiveTrigger.Ps2Computer), nameof(SObjectiveTrigger.Ps2Computer), RepLeafKind.Object));
                // A PS2 map instance can be replaced by a dynamic channel. Its
                // localized label must follow the authored instance, not the PC factory.
                var ps2Initial = RepCondition.Bind(RepConditionParser.Parse("bNetInitial && bPS2Objective"),
                    SObjectiveTrigger.UcMeta.Class, registry.Readers);
                ps2Objective.Properties.Add(new("Ps2TriggerName", nameof(SObjectiveTrigger.TriggerName), RepLeafKind.String, ps2Initial));
                if (typeof(SFlag).IsAssignableFrom(type))
                    ps2Objective.Properties.Add(new(nameof(SFlag.Ps2SourceStaticMesh), nameof(SFlag.Ps2SourceStaticMesh), RepLeafKind.Asset, ps2Initial));
                registry.Extend(type, ps2Objective);
            }
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
                rules.Properties.Add(new RepExtensionProperty("RiflesEnabled", "RiflesEnabled", RepLeafKind.Bool));
                rules.Properties.Add(new RepExtensionProperty("ResponsiveSmokeEnabled", "ResponsiveSmokeEnabled", RepLeafKind.Bool));
                rules.Properties.Add(new RepExtensionProperty("PracticeFiniteResources", "PracticeFiniteResources", RepLeafKind.Bool));
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
            if (typeof(MercRifle).IsAssignableFrom(type))
            {
                var aim = new RepExtension();
                aim.Properties.Add(new(nameof(MercRifle.AimRequested), nameof(MercRifle.AimRequested), RepLeafKind.Bool));
                registry.Extend(type, aim);
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
        var spyTeam = new RepExtension();
        spyTeam.Properties.Add(new(nameof(STeamAttaquant.AdditionalGadgets), nameof(STeamAttaquant.AdditionalGadgets), RepLeafKind.Byte));
        registry.Extend<STeamAttaquant>(spyTeam);
        var hack = new RepExtension();
        hack.Properties.Add(new(nameof(MineHackGadget.HackPercent), nameof(MineHackGadget.HackPercent), RepLeafKind.Byte));
        registry.Extend<MineHackGadget>(hack);
        var cloneRecharge = new RepExtension();
        cloneRecharge.Properties.Add(new(nameof(CloneGadget.RechargeLeft), nameof(CloneGadget.RechargeLeft), RepLeafKind.Float));
        registry.Extend<CloneGadget>(cloneRecharge);
        var mineState = new RepExtension();
        mineState.Properties.Add(new(nameof(SMines.IsHacked), nameof(SMines.IsHacked), RepLeafKind.Bool));
        registry.Extend<SMineProx>(mineState);
        registry.Extend<SMineLaser>(mineState);
        registry.Extend<SMinePoison>(mineState);
        SidearmNet.Register(registry);
        registry.Extend<SSmokeEmitter>(ResponsiveSmokeReplication.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.LevelInfo>(NetDeviceEvents.CreateExtension());
        registry.Extend<OpenScct.Core.Engine.PlayerReplicationInfo>(PlayerLookNet.CreateExtension());
        var cameras = new RepExtension();
        cameras.Properties.Add(new RepExtensionProperty(nameof(SCamNetwork.DisabledSeconds), nameof(SCamNetwork.DisabledSeconds), RepLeafKind.Int));
        registry.Extend<SCamNetwork>(cameras);
        registry.Extend<SComputerObjectiveTrigger>(Ps2ComputerExtension(registry));
        var ps2Target = new RepExtension();
        ps2Target.Properties.Add(new(nameof(SBombTargetObjectiveTrigger.Ps2TargetRadius), nameof(SBombTargetObjectiveTrigger.Ps2TargetRadius), RepLeafKind.Float));
        ps2Target.Properties.Add(new(nameof(SBombTargetObjectiveTrigger.Ps2TargetHeight), nameof(SBombTargetObjectiveTrigger.Ps2TargetHeight), RepLeafKind.Float));
        registry.Extend<SBombTargetObjectiveTrigger>(ps2Target);
        var ps2Gadget = new RepExtension();
        ps2Gadget.Properties.Add(new(nameof(SGadBomb.Ps2ModemProfile), nameof(SGadBomb.Ps2ModemProfile), RepLeafKind.Bool));
        registry.Extend<SGadBomb>(ps2Gadget);
        var clone = new RepExtension { InterpolateMovement = true };
        clone.Properties.Add(new("CloneLocation", nameof(SpyClone.Location), RepLeafKind.VectorFine));
        clone.Properties.Add(new("CloneVelocity", nameof(SpyClone.Velocity), RepLeafKind.VectorFine));
        clone.Properties.Add(new(nameof(SpyClone.Stopped), nameof(SpyClone.Stopped), RepLeafKind.Bool));
        clone.Properties.Add(new(nameof(SpyClone.GlowMode), nameof(SpyClone.GlowMode), RepLeafKind.Int));
        clone.Properties.Add(new(nameof(SpyClone.WetLook), nameof(SpyClone.WetLook), RepLeafKind.Bool));
        clone.Properties.Add(new(nameof(SpyClone.CloakLook), nameof(SpyClone.CloakLook), RepLeafKind.Byte));
        clone.Properties.Add(new(nameof(SpyClone.PistolDrawn), nameof(SpyClone.PistolDrawn), RepLeafKind.Bool));
        registry.Register<SpyClone>(clone);
        registry.Register<CubeWalker>(CubeWalkerNet.CreateExtension());
        registry.Register<AnimationController>(AnimationController.Replication());
        registry.Register<Conveyor>(ConveyorExtension());
        registry.Register<Ps2Modem>();
        var ps2Effect = new RepExtension();
        ps2Effect.Properties.Add(new(nameof(Ps2ModemEffect.SmokeTexture), nameof(Ps2ModemEffect.SmokeTexture), RepLeafKind.Asset));
        ps2Effect.Properties.Add(new(nameof(Ps2ModemEffect.FireTexture), nameof(Ps2ModemEffect.FireTexture), RepLeafKind.Asset));
        registry.Register<Ps2ModemEffect>(ps2Effect);
        return registry;
    }

    private static RepExtension Ps2ComputerExtension(NetClassRegistry registry)
    {
        var ps2 = new RepExtension();
        void Add(string field, RepLeafKind kind) => ps2.Properties.Add(new(field, field, kind));
        Add(nameof(SComputerObjectiveTrigger.Ps2SkinActive), RepLeafKind.Asset);
        Add(nameof(SComputerObjectiveTrigger.Ps2SkinInactive), RepLeafKind.Asset);
        Add(nameof(SComputerObjectiveTrigger.Ps2SkinHacked), RepLeafKind.Asset);
        Add(nameof(SComputerObjectiveTrigger.Ps2SkinKeyboard), RepLeafKind.Asset);
        Add(nameof(SComputerObjectiveTrigger.Ps2SkinKeyboardHacked), RepLeafKind.Asset);
        Add(nameof(SComputerObjectiveTrigger.Ps2HackAnimation), RepLeafKind.Name);
        Add(nameof(SComputerObjectiveTrigger.Ps2HackedAnimation), RepLeafKind.Name);
        Add(nameof(SComputerObjectiveTrigger.Ps2AlarmAnimation), RepLeafKind.Name);
        Add(nameof(SComputerObjectiveTrigger.Ps2CtfAnimation), RepLeafKind.Name);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetVisualState), RepLeafKind.Byte);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetSequence), RepLeafKind.Name);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetRate), RepLeafKind.Float);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetFrame), RepLeafKind.Float);
        var initial = RepCondition.Bind(RepConditionParser.Parse("bNetInitial"), SComputerObjectiveTrigger.UcMeta.Class, registry.Readers);
        ps2.Properties.Add(new(nameof(SComputerObjectiveTrigger.Ps2NetInitialFrame), nameof(SComputerObjectiveTrigger.Ps2NetInitialFrame), RepLeafKind.Float, initial));
        Add(nameof(SComputerObjectiveTrigger.Ps2NetBackward), RepLeafKind.Bool);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetContinue), RepLeafKind.Bool);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetProxy), RepLeafKind.Bool);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetAnimationSerial), RepLeafKind.Int);
        Add(nameof(SComputerObjectiveTrigger.Ps2NetCommandSerial), RepLeafKind.Int);
        return ps2;
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
