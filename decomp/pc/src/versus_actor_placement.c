/* Unreal's stock AActor base initializer takes the object in EAX and leaves it there. */
struct actor_header {
    const void *methods;
};

struct emitter_header {
    struct actor_header actor;
    unsigned char preceding_fields[0x32c - sizeof(struct actor_header)];
    float field_32c;
    unsigned char middle_fields[0x338 - 0x330];
    unsigned int field_338;
};

struct projector_header {
    struct actor_header actor;
    unsigned char preceding_fields[0x444 - sizeof(struct actor_header)];
    float field_444;
    unsigned char middle_fields[0x450 - 0x448];
    unsigned int field_450;
};

struct soft_body_actor {
    struct actor_header actor;
    unsigned char preceding_fields[0x2d9 - sizeof(struct actor_header)];
    unsigned char field_2d9;
};

extern struct actor_header *__attribute__((regparm(1)))
    initialize_actor(struct actor_header *storage);

#define PLACE_AS_ACTOR(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct actor_header *storage) \
    { \
        if (storage) \
            initialize_actor(storage)->methods = vtable_##type; \
    }

static __inline void initialize_emitter_fields(struct emitter_header *emitter)
{
    emitter->field_32c = 1.0f;
    emitter->field_338 = 0;
}

static __inline void initialize_projector_fields(struct projector_header *projector)
{
    projector->field_444 = 1.0f;
    projector->field_450 = 0;
}

static __inline void initialize_soft_body_field(struct soft_body_actor *actor)
{
    actor->field_2d9 = 13;
}

/* The base subclass installs its table before initializing its own members.
   A derived subclass installs its table after the base member initialization. */
#define PLACE_BASE_ACTOR(type, record, initialize_fields) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct actor_header *storage) \
    { \
        if (storage) { \
            struct record *object = (struct record *)initialize_actor(storage); \
            object->actor.methods = vtable_##type; \
            initialize_fields(object); \
        } \
    }

#define PLACE_DERIVED_ACTOR(type, record, initialize_fields) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct actor_header *storage) \
    { \
        if (storage) { \
            struct record *object = (struct record *)initialize_actor(storage); \
            initialize_fields(object); \
            object->actor.methods = vtable_##type; \
        } \
    }

PLACE_AS_ACTOR(AAntiPortalActor)
PLACE_AS_ACTOR(ABlockingVolume)
PLACE_AS_ACTOR(ABrush)
PLACE_AS_ACTOR(ACarriedObject)
PLACE_AS_ACTOR(AController)
PLACE_AS_ACTOR(ADecoVolumeObject)
PLACE_AS_ACTOR(ADecoration)
PLACE_AS_ACTOR(ADecorationList)
PLACE_DERIVED_ACTOR(AESBChainActor, soft_body_actor, initialize_soft_body_field)
PLACE_BASE_ACTOR(AESBPatchActor, soft_body_actor, initialize_soft_body_field)
PLACE_BASE_ACTOR(AESBRopeActor, soft_body_actor, initialize_soft_body_field)
PLACE_DERIVED_ACTOR(AESBStripDoorActor, soft_body_actor, initialize_soft_body_field)
PLACE_AS_ACTOR(AESoftBodyActor)
PLACE_AS_ACTOR(AEffects)
PLACE_BASE_ACTOR(AEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(AFileLog)
PLACE_AS_ACTOR(AGameReplicationInfo)
PLACE_AS_ACTOR(AGameStats)
PLACE_AS_ACTOR(AInfo)
PLACE_AS_ACTOR(AInternetInfo)
PLACE_AS_ACTOR(AInventory)
PLACE_AS_ACTOR(AKeypoint)
PLACE_AS_ACTOR(ALevelInfo)
PLACE_AS_ACTOR(ALight)
PLACE_AS_ACTOR(ALineOfSightTrigger)
PLACE_AS_ACTOR(ALocalMessage)
PLACE_AS_ACTOR(AMotionTrackingVision)
PLACE_AS_ACTOR(AMover)
PLACE_AS_ACTOR(AMoverRotating)
PLACE_AS_ACTOR(ANote)
PLACE_AS_ACTOR(APawn)
PLACE_AS_ACTOR(APlayerReplicationInfo)
PLACE_AS_ACTOR(APlayerStart)
PLACE_AS_ACTOR(AProjectile)
PLACE_BASE_ACTOR(AProjector, projector_header, initialize_projector_fields)
PLACE_AS_ACTOR(AReplicationInfo)
PLACE_AS_ACTOR(ASAlarm)
PLACE_AS_ACTOR(ASAmbientSoundTrigger)
PLACE_AS_ACTOR(ASAmmoStock)
PLACE_AS_ACTOR(ASAmmoStockInteraction)
PLACE_AS_ACTOR(ASAnimatedMesh)
PLACE_AS_ACTOR(ASBackPackInteraction)
PLACE_AS_ACTOR(ASBasePawn)
PLACE_AS_ACTOR(ASBasicGamePlayObject)
PLACE_DERIVED_ACTOR(ASBloodSplat, projector_header, initialize_projector_fields)
PLACE_AS_ACTOR(ASBombTargetInteraction)
PLACE_AS_ACTOR(ASBombTargetObjectiveTrigger)
PLACE_DERIVED_ACTOR(ASBulletImpact, projector_header, initialize_projector_fields)
PLACE_AS_ACTOR(ASCamNetwork)
PLACE_AS_ACTOR(ASCamera)
PLACE_DERIVED_ACTOR(ASChaffeEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASComputerInteraction)
PLACE_AS_ACTOR(ASComputerObjectiveTrigger)
PLACE_AS_ACTOR(ASCoronaLight)
PLACE_AS_ACTOR(ASDamageVolume)
PLACE_AS_ACTOR(ASDisk)
PLACE_DERIVED_ACTOR(ASDouilleEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASExtincteur)
PLACE_DERIVED_ACTOR(ASExtincteurEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASFireTrace)
PLACE_AS_ACTOR(ASFlag)
PLACE_AS_ACTOR(ASFlagDropZone)
PLACE_AS_ACTOR(ASFlagDropZoneInteraction)
PLACE_AS_ACTOR(ASFlaque)
PLACE_DERIVED_ACTOR(ASFlareCourseEmitter, emitter_header, initialize_emitter_fields)
PLACE_DERIVED_ACTOR(ASFlareEmitter, emitter_header, initialize_emitter_fields)
PLACE_DERIVED_ACTOR(ASFlashBang, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASFlashGen)
PLACE_AS_ACTOR(ASFlashLight)
PLACE_AS_ACTOR(ASFlashLight_FragGrenade)
PLACE_DERIVED_ACTOR(ASFragLaunchEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASGPOControl)
PLACE_AS_ACTOR(ASGadBackPack)
PLACE_AS_ACTOR(ASGadBomb)
PLACE_AS_ACTOR(ASGadCamNetwork)
PLACE_AS_ACTOR(ASGadChaffeGrenade)
PLACE_AS_ACTOR(ASGadFlareLauncher)
PLACE_AS_ACTOR(ASGadFlashBang)
PLACE_AS_ACTOR(ASGadFragGrenade)
PLACE_AS_ACTOR(ASGadGasMask)
PLACE_AS_ACTOR(ASGadGrenLauncher)
PLACE_AS_ACTOR(ASGadHeartBeatSensor)
PLACE_AS_ACTOR(ASGadNoiseEmitter)
PLACE_AS_ACTOR(ASGadPhosphoLauncher)
PLACE_AS_ACTOR(ASGadSmokeGrenade)
PLACE_AS_ACTOR(ASGadTazerLauncher)
PLACE_AS_ACTOR(ASGadTorche)
PLACE_AS_ACTOR(ASGadTorcheLaser)
PLACE_AS_ACTOR(ASGadget)
PLACE_AS_ACTOR(ASGameMessages)
PLACE_AS_ACTOR(ASGamePlayObject)
PLACE_AS_ACTOR(ASGoggle)
PLACE_AS_ACTOR(ASGoggleBeam)
PLACE_DERIVED_ACTOR(ASGrenadeExplosion, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASHealMateInteraction)
PLACE_AS_ACTOR(ASImpactEffectBase)
PLACE_AS_ACTOR(ASImpactEffectShocker)
PLACE_AS_ACTOR(ASImpactEffectShockerMetal)
PLACE_AS_ACTOR(ASImpactManager)
PLACE_AS_ACTOR(ASInfirmary)
PLACE_AS_ACTOR(ASInteractObject)
PLACE_AS_ACTOR(ASInteractionManager)
PLACE_AS_ACTOR(ASInterrupteur)
PLACE_AS_ACTOR(ASLaserBeam)
PLACE_AS_ACTOR(ASLaserBeam_MineLaser)
PLACE_DERIVED_ACTOR(ASLaserProjector, projector_header, initialize_projector_fields)
PLACE_AS_ACTOR(ASLaserTrigger)
PLACE_AS_ACTOR(ASLeaveInteraction)
PLACE_AS_ACTOR(ASLift)
PLACE_AS_ACTOR(ASLiftDoor)
PLACE_AS_ACTOR(ASLogManager)
PLACE_AS_ACTOR(ASMagicEvent)
PLACE_AS_ACTOR(ASMateInteraction)
PLACE_AS_ACTOR(ASMineLaser)
PLACE_AS_ACTOR(ASMinePiegeSpy)
PLACE_AS_ACTOR(ASMinePoison)
PLACE_AS_ACTOR(ASMineProx)
PLACE_AS_ACTOR(ASMineProxInteraction)
PLACE_AS_ACTOR(ASMines)
PLACE_AS_ACTOR(ASMission)
PLACE_AS_ACTOR(ASMotionTrackingVision)
PLACE_AS_ACTOR(ASMuzzleFlash)
PLACE_AS_ACTOR(ASMuzzleSticky)
PLACE_AS_ACTOR(ASNoiseEmitter)
PLACE_AS_ACTOR(ASNotifyNoise)
PLACE_AS_ACTOR(ASObjective)
PLACE_AS_ACTOR(ASObjectiveTrigger)
PLACE_AS_ACTOR(ASOrientableObject)
PLACE_AS_ACTOR(ASPAWNDEFENSE)
PLACE_AS_ACTOR(ASPawnAttaque)
PLACE_DERIVED_ACTOR(ASPhosphoEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASPickupInteraction)
PLACE_AS_ACTOR(ASPlayerCam)
PLACE_DERIVED_ACTOR(ASPoisonEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASPresDetector)
PLACE_AS_ACTOR(ASProjChaffe)
PLACE_AS_ACTOR(ASProjFlare)
PLACE_AS_ACTOR(ASProjFlashBang)
PLACE_AS_ACTOR(ASProjFrag)
PLACE_AS_ACTOR(ASProjGrenade)
PLACE_AS_ACTOR(ASProjMarqueurSpy)
PLACE_AS_ACTOR(ASProjPhospho)
PLACE_AS_ACTOR(ASProjSmoke)
PLACE_AS_ACTOR(ASProjoFixe)
PLACE_AS_ACTOR(ASRJ45Checker)
PLACE_AS_ACTOR(ASRemoveSpyBulletInteraction)
PLACE_AS_ACTOR(ASSavedMove)
PLACE_AS_ACTOR(ASShareObjectInteraction)
PLACE_DERIVED_ACTOR(ASSmokeEmitter, emitter_header, initialize_emitter_fields)
PLACE_DERIVED_ACTOR(ASSmokeLaunchEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASSprinkler)
PLACE_AS_ACTOR(ASSpyBulletLauncher)
PLACE_AS_ACTOR(ASStickyCam)
PLACE_DERIVED_ACTOR(ASStickyCamEmitter, emitter_header, initialize_emitter_fields)
PLACE_AS_ACTOR(ASStickyCamInteraction)
PLACE_AS_ACTOR(ASTazerEmitter)
PLACE_AS_ACTOR(ASTeamInfo)
PLACE_AS_ACTOR(ASTriggerInteraction)
PLACE_AS_ACTOR(ASTriggerLight)
PLACE_AS_ACTOR(ASVitre)
PLACE_AS_ACTOR(ASVolumetricSensor)
PLACE_AS_ACTOR(ASWeapon)
PLACE_AS_ACTOR(ASWeaponAttaque)
PLACE_AS_ACTOR(ASWeaponDefense)
PLACE_AS_ACTOR(ASWeaponDefenseDummy)
PLACE_AS_ACTOR(ASWeaponDefenseDummy_Shotgun)
PLACE_AS_ACTOR(ASWeaponDefenseDummy_Uzi)
PLACE_AS_ACTOR(ASWeaponDefense_Shotgun)
PLACE_AS_ACTOR(ASWeaponDefense_Uzi)
PLACE_DERIVED_ACTOR(AShadowProjector, projector_header, initialize_projector_fields)
PLACE_AS_ACTOR(ASkyZoneInfo)
PLACE_AS_ACTOR(AStaticMeshActor)
PLACE_AS_ACTOR(ATeamInfo)
PLACE_AS_ACTOR(ATrigger)
PLACE_AS_ACTOR(ATriggers)
PLACE_AS_ACTOR(AVolume)
PLACE_AS_ACTOR(AZoneInfo)
