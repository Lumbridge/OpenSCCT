using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Devices;

/// <summary>
/// Class lookups for the device ports: every generated class of the Core, Engine and SBase packages (UnrealScript
/// <c>Spawn(class&lt;Actor&gt;)</c> and <c>ClassIsChildOf</c> resolve through it), and the registration of the ported
/// device classes with a map loader.
/// </summary>
public static class DeviceClasses
{
    /// <summary>Index of every generated class.</summary>
    public static readonly UcClassIndex Index = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);

    /// <summary>
    /// UnrealScript <c>Spawn(Class, SpawnOwner, SpawnTag, SpawnLocation, SpawnRotation)</c> from
    /// <paramref name="spawner"/>: creates the class's generated (or ported) type. Null for None, abstract or
    /// non-actor classes and failed spawns.
    /// </summary>
    public static Actor? Spawn(Actor spawner, ClassRef Class, Actor? SpawnOwner, Name SpawnTag, FVector SpawnLocation,
        FRotator SpawnRotation)
    {
        if (OpenScct.Game.Equipment.SidearmEquipment.Spawn(spawner, Class, SpawnOwner, SpawnLocation, SpawnRotation) is { } addition) return addition;
        UcClassInfo? info = (spawner.XLevel.GetService<UcClassIndex>() ?? Index).Find(Class);
        if (info?.Create() is not Actor actor)
        {
            return null;
        }

        Actor? spawned = spawner.XLevel.SpawnActor(actor, SpawnLocation, SpawnRotation, SpawnOwner, spawner.Instigator);
        if (spawned is not null && !SpawnTag.IsNone)
        {
            spawned.Tag = SpawnTag;
        }

        return spawned;
    }

    /// <summary>Registers the ported device classes with a map loader's class registry.</summary>
    public static void Register(ActorClassRegistry registry)
    {
        registry.Register<Mover>("Engine.Mover");
        registry.Register<MoverRotating>("Engine.MoverRotating");
        registry.Register<SLift>("SBase.SLift");
        registry.Register<SLiftDoor>("SBase.SLiftDoor");
        registry.Register<SBallTrap>("SBase.SBallTrap");
        registry.Register<Trigger>("Engine.Trigger");
        registry.Register<SoundTrigger>("Engine.SoundTrigger");
        registry.Register<MusicTrigger>("Engine.MusicTrigger");
        registry.Register<SPresenceTrigger>("SBase.SPresenceTrigger");
        registry.Register<SAmbientSoundTrigger>("SBase.SAmbientSoundTrigger");
        registry.Register<SAnimatedMesh>("SBase.SAnimatedMesh");
        registry.Register<SShaker>("SBase.SShaker");
        registry.Register<SGETrigger>("SBase.SGETrigger");
        registry.Register<SMagicEvent>("SBase.SMagicEvent");
        registry.Register<SGamePlayObject>("SBase.SGamePlayObject");
        registry.Register<SVitre>("SBase.SVitre");
        registry.Register<SExtincteur>("SBase.SExtincteur");
        registry.Register<SDamageVolume>("SBase.SDamageVolume");
        registry.Register<SInfirmary>("SBase.SInfirmary");
        registry.Register<SAmmoStock>("SBase.SAmmoStock");
        registry.Register<SZoneTips>("SBase.SZoneTips");
        registry.Register<SEndZoneTip>("SBase.SEndZoneTip");
        registry.Register<SCamNetwork>("SBase.SCamNetwork");
        registry.Register<SAlarm>("SBase.SAlarm");
        registry.Register<SMessageTrigger>("SBase.SMessageTrigger");
        registry.Register<SNotifyNoise>("SBase.SNotifyNoise");
        registry.Register<SCamera>("SBase.SCamera");
        registry.Register<STriggerLight>("SBase.STriggerLight");
        registry.Register<SProjoFixe>("SBase.SProjoFixe");
    }
}
