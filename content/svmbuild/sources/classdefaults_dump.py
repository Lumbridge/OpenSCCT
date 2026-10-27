"""scctpkg's class_defaults.json, made from the player's own install.

scctpkg.level reads tools/scctpkg/scctpkg/class_defaults.json at import: DrawType, bHidden, bHideInNormalVision and
StaticMesh of the actor classes its upstream dump covered. That file is game-derived and not tracked, so an installed
content builder (the launcher's, docs/LAUNCHER.md "Game content") has none. The values are in the classes step's
shared/classes.json (the class default objects of System/_PC_/*.u), so the file is written from there for exactly the
classes the upstream dump lists (CLASSES, names only): a class outside the list keeps scctpkg's own fallback, so the
converted maps are the same as with the upstream file. Checked equal to the upstream dump for all 274 classes.
"""

from __future__ import annotations

import json
from pathlib import Path

# The actor classes of the upstream dump (short names). Class names only: the values come from the player's install.
CLASSES = (
    "AccessControl", "Actor", "AmbientSound", "AntiPortalActor", "BlockingVolume", "BroadcastHandler", "Brush",
    "Camera", "CarriedObject", "Controller", "DecoVolumeObject", "Decoration", "DecorationList", "ERainVolume",
    "ESBChainActor", "ESBPatchActor", "ESBRopeActor", "ESBStripDoorActor", "ESoftBodyActor", "Effects", "Emitter",
    "FileLog", "Fragment", "GameInfo", "GameMessage", "GameReplicationInfo", "GameStats", "HUD", "Info",
    "InternetInfo", "InternetLink", "Inventory", "Keypoint", "LancementMenu", "LevelInfo", "Light",
    "LineOfSightTrigger", "LocalMessage", "MessagingSpectator", "MotionTrackingVision", "Mover", "MoverRotating",
    "Note", "Pawn", "PlayerController", "PlayerReplicationInfo", "PlayerStart", "Projectile", "Projector",
    "ReplicationInfo", "SAlarm", "SAlarmZoneInfo", "SAmbientSoundTrigger", "SAmmoStock", "SAmmoStockInteraction",
    "SAnimatedMesh", "SBackPack", "SBackPackInteraction", "SBackPack_Att", "SBasePawn", "SBasicGamePlayObject",
    "SBloodSplat", "SBombTargetInteraction", "SBombTargetObjectiveTrigger", "SBulletImpact", "SCamNetwork",
    "SCamera", "SChaffeEmitter", "SComputerInteraction", "SComputerObjectiveTrigger", "SCoronaLight",
    "SDamageVolume", "SDeathMessage", "SDisk", "SDouilleEmitter", "SDustEmitter", "SEffectSand",
    "SEffectShockedPawnEmitter", "SEffectSteam", "SEffectWaterPipe", "SExtincteur", "SExtincteurEmitter",
    "SFireTrace", "SFlag", "SFlagDropZone", "SFlagDropZoneInteraction", "SFlaque", "SFlareBurnProjector",
    "SFlareCourseEmitter", "SFlareEmitter", "SFlashBang", "SFlashGen", "SFlashLight", "SFlashLight_FragGrenade",
    "SFlicFlocEmitter", "SFootPrint", "SFootPrintEffect", "SFragLaunchEmitter", "SGETrigger", "SGPOControl",
    "SGadBackPack", "SGadBomb", "SGadCamNetwork", "SGadChaffeGrenade", "SGadCloak", "SGadFlareLauncher",
    "SGadFlashBang", "SGadFragGrenade", "SGadGasMask", "SGadGrenLauncher", "SGadHeartBeatSensor",
    "SGadLaunchEffects", "SGadMultiMinesLauncher", "SGadMultiPresDetectorLauncher", "SGadNoiseEmitter",
    "SGadPhosphoLauncher", "SGadSmokeGrenade", "SGadStickyCam", "SGadTazerLauncher", "SGadTorche",
    "SGadTorcheLaser", "SGadget", "SGameInfo", "SGameMessages", "SGamePlayObject", "SGasMask", "SGoggle",
    "SGoggleBeam", "SGoggleBeam_ATT", "SGoggleBeam_DEF", "SGrenLaunchEffects", "SGrenadeExplosion",
    "SHealMateInteraction", "SHelpPath", "SHud", "SImpactEffectBase", "SImpactEffectBeton", "SImpactEffectFence",
    "SImpactEffectGlass", "SImpactEffectHole", "SImpactEffectHoleBack", "SImpactEffectMetal", "SImpactEffectSand",
    "SImpactEffectShocker", "SImpactEffectShockerMetal", "SImpactEffectSoft", "SImpactEffectWater",
    "SImpactEffectWood", "SImpactManager", "SImpactSteamPipe", "SImpactWaterPipe", "SInfirmary",
    "SInfirmaryInteraction", "SInteractObject", "SInteractionManager", "SInterrupteur", "SLANClientLink",
    "SLANServerLink", "SLaserBeam", "SLaserBeam_MineLaser", "SLaserProjector", "SLaserTrigger", "SLeaveInteraction",
    "SLift", "SLiftDoor", "SLogManager", "SMagicEvent", "SMalfunctionEmitter", "SMateInteraction",
    "SMessageTrigger", "SMetalHotSpot", "SMetalSpark", "SMineLaser", "SMinePiegeSpy", "SMinePoison",
    "SMinePoisonEmitter", "SMineProx", "SMineProxEmitter", "SMineProxInteraction", "SMineTrapInteraction", "SMines",
    "SMission", "SMotionTrackingVision", "SMuzzleFlash", "SMuzzleGenerique", "SMuzzleSticky", "SNoiseEmitter",
    "SNotifyNoise", "SObjective", "SObjectiveTrigger", "SOrientableObject", "SPAWNDEFENSE", "SPawnAttaque",
    "SPawnAttaque_1Mesh", "SPawnDefense_1Mesh", "SPhosphoEmitter", "SPickupInteraction", "SPlayerCam",
    "SPlayerController", "SPoisonEmitter", "SPresDetector", "SPresDetectorInteraction", "SPresenceTrigger",
    "SProjChaffe", "SProjFlare", "SProjFlashBang", "SProjFrag", "SProjGrenade", "SProjMarqueurSpy", "SProjPhospho",
    "SProjSmoke", "SProjoFixe", "SProjoInteraction", "SProjoLight", "SRJ45Checker", "SRemoveSpyBulletInteraction",
    "SSavedMove", "SScoreBoard", "SScoreBoardStory", "SShaker", "SShareObjectInteraction", "SShotGunEmitter",
    "SShotGunProjector", "SSmokeCanonEmitter", "SSmokeEmitter", "SSmokeLaunchEmitter", "SSparkBeton",
    "SSparkShocker", "SSparkShockerMetal", "SSparkWater", "SSparkWood", "SSpawnableEmitter", "SSprinkler",
    "SSprinklerEmitter", "SSpyBulletLauncher", "SStickyCam", "SStickyCamEmitter", "SStickyCamInteraction",
    "SStickyShocker", "STazerEmitter", "STeamAttaquant", "STeamDefenseur", "STeamInfo", "STriggerInteraction",
    "STriggerLight", "STriggerPlayerStart", "SVitre", "SVitreEmitter", "SVolumetricSensor", "SWaterFootStepEffect",
    "SWeapon", "SWeaponAttaque", "SWeaponDefense", "SWeaponDefenseDummy", "SWeaponDefenseDummy_Shotgun",
    "SWeaponDefenseDummy_Uzi", "SWeaponDefense_Shotgun", "SWeaponDefense_Uzi", "SZoneTips", "ScoreBoard",
    "Security", "ShadowProjector", "SkyZoneInfo", "SoundTrigger", "StaticMeshActor", "TcpLink", "TeamInfo",
    "Trigger", "Triggers", "UdpLink", "VoicePack", "Volume", "ZoneInfo", "sMuzzleCanon"
)


def derive(classes_doc: dict) -> dict:
    """class_defaults.json's content from a shared/classes.json document."""
    by_short = {}
    for full in classes_doc["classes"]:
        by_short.setdefault(full.rsplit(".", 1)[-1], full)
    out = {}
    for name in CLASSES:
        full = by_short.get(name)
        if full is None:
            continue
        d = classes_doc["classes"][full].get("defaults", {})
        sm = d.get("StaticMesh")
        sm = sm.get("$asset", "") if isinstance(sm, dict) else (sm or "")
        out[name] = {"dt": d.get("DrawType"), "hidden": int(bool(d.get("bHidden"))),
                     "hideNV": int(bool(d.get("bHideInNormalVision"))), "sm": sm}
    return out


def ensure(scctpkg_dir: Path, content_root: Path) -> bool:
    """Writes scctpkg_dir/class_defaults.json from content_root/shared/classes.json when it is missing. True when
    the file is there afterwards."""
    target = Path(scctpkg_dir) / "class_defaults.json"
    if target.exists():
        return True
    source = Path(content_root) / "shared" / "classes.json"
    if not source.exists():
        return False
    doc = json.loads(source.read_text(encoding="utf-8"))
    tmp = target.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(derive(doc), indent=1, sort_keys=True) + "\n", encoding="utf-8")
    tmp.replace(target)
    return True
