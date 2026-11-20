/* The retail class casts are template instantiations. Their class singletons
   are initialized on first use and then tested against the superclass chain. */
struct RuntimeClass {
    unsigned char object_fields[0x28];
    struct RuntimeClass *super_class;
};

struct EngineObject {
    unsigned char object_fields[0x24];
    struct RuntimeClass *type;
};

static int has_class(const struct EngineObject *object, const struct RuntimeClass *wanted)
{
    const struct RuntimeClass *type;
    for (type = object->type; type; type = type->super_class) {
        if (type == wanted)
            return 1;
    }
    return wanted == 0;
}

#define DEFINE_CLASS_CAST(Type, Package)                                      \
    struct Type;                                                              \
    extern struct RuntimeClass *class_##Type;                                 \
    extern struct RuntimeClass *create_##Type(const unsigned short *package); \
    extern void register_##Type(void);                                        \
    struct Type *cast_##Type(struct EngineObject *object)                      \
    {                                                                         \
        if (object) {                                                         \
            if (!class_##Type) {                                              \
                class_##Type = create_##Type(package_##Package);              \
                register_##Type();                                            \
            }                                                                 \
            if (has_class(object, class_##Type))                              \
                return (struct Type *)object;                                 \
        }                                                                     \
        return 0;                                                             \
    }

extern const unsigned short package_Core[];
extern const unsigned short package_Echelon[];
extern const unsigned short package_EchelonIngredient[];
extern const unsigned short package_Engine[];

DEFINE_CLASS_CAST(UArrayProperty, Core)
DEFINE_CLASS_CAST(UBoolProperty, Core)
DEFINE_CLASS_CAST(UByteProperty, Core)
DEFINE_CLASS_CAST(UClass, Core)
DEFINE_CLASS_CAST(UClassProperty, Core)
DEFINE_CLASS_CAST(UEnum, Core)
DEFINE_CLASS_CAST(UFloatProperty, Core)
DEFINE_CLASS_CAST(UFunction, Core)
DEFINE_CLASS_CAST(UMapProperty, Core)
DEFINE_CLASS_CAST(UObjectProperty, Core)
DEFINE_CLASS_CAST(UPackage, Core)
DEFINE_CLASS_CAST(UProperty, Core)
DEFINE_CLASS_CAST(UState, Core)
DEFINE_CLASS_CAST(UStructProperty, Core)

DEFINE_CLASS_CAST(AEAIController, Echelon)
DEFINE_CLASS_CAST(AEAIPawn, Echelon)
DEFINE_CLASS_CAST(AEAccessory, Echelon)
DEFINE_CLASS_CAST(AEAmbianceSplitter, Echelon)
DEFINE_CLASS_CAST(AEBaseDoor, Echelon)
DEFINE_CLASS_CAST(AECoopPlayerController, Echelon)
DEFINE_CLASS_CAST(AEDynamicLight, Echelon)
DEFINE_CLASS_CAST(AEFlashLight, Echelon)
DEFINE_CLASS_CAST(AEGameplayObject, Echelon)
DEFINE_CLASS_CAST(AEHackingGameplay, Echelon)
DEFINE_CLASS_CAST(AEHandGun, Echelon)
DEFINE_CLASS_CAST(AEInventoryItem, Echelon)
DEFINE_CLASS_CAST(AEMainGun, Echelon)
DEFINE_CLASS_CAST(AEMuzzleFlash, Echelon)
DEFINE_CLASS_CAST(AEOcclusionZone, Echelon)
DEFINE_CLASS_CAST(AEPattern, Echelon)
DEFINE_CLASS_CAST(AEPawn, Echelon)
DEFINE_CLASS_CAST(AEPlayerController, Echelon)
DEFINE_CLASS_CAST(AESoundVolume, Echelon)
DEFINE_CLASS_CAST(AETrapMover, Echelon)
DEFINE_CLASS_CAST(AEVolume, Echelon)
DEFINE_CLASS_CAST(AEVolumeTrigger, Echelon)
DEFINE_CLASS_CAST(AEWallHit, Echelon)
DEFINE_CLASS_CAST(AEWeapon, Echelon)
DEFINE_CLASS_CAST(AEWeaponAttachment, Echelon)
DEFINE_CLASS_CAST(AEZoneInfo, Echelon)

DEFINE_CLASS_CAST(AEComputerInteraction, EchelonIngredient)
DEFINE_CLASS_CAST(AEComputerSystem, EchelonIngredient)
DEFINE_CLASS_CAST(AECraneController, EchelonIngredient)
DEFINE_CLASS_CAST(AEDetonator, EchelonIngredient)
DEFINE_CLASS_CAST(AEDoorInteraction, EchelonIngredient)
DEFINE_CLASS_CAST(AEDualLock, EchelonIngredient)
DEFINE_CLASS_CAST(AEElevatorInteraction, EchelonIngredient)
DEFINE_CLASS_CAST(AEElevatorPanel, EchelonIngredient)
DEFINE_CLASS_CAST(AEFireVolume, EchelonIngredient)
DEFINE_CLASS_CAST(AEGenerator, EchelonIngredient)
DEFINE_CLASS_CAST(AEKeyButton, EchelonIngredient)
DEFINE_CLASS_CAST(AEKeyPad, EchelonIngredient)
DEFINE_CLASS_CAST(AEMedKit, EchelonIngredient)
DEFINE_CLASS_CAST(AEMissileInteraction, EchelonIngredient)
DEFINE_CLASS_CAST(AEPickLockInteraction, EchelonIngredient)
DEFINE_CLASS_CAST(AEPickLockSystem, EchelonIngredient)
DEFINE_CLASS_CAST(AEPlaceableCharge, EchelonIngredient)
DEFINE_CLASS_CAST(AERetinalScanner, EchelonIngredient)
DEFINE_CLASS_CAST(AEScalingActor, EchelonIngredient)
DEFINE_CLASS_CAST(AESensor, EchelonIngredient)
DEFINE_CLASS_CAST(AESlidingDoor, EchelonIngredient)
DEFINE_CLASS_CAST(AEStickyCamera, EchelonIngredient)
DEFINE_CLASS_CAST(AESwingingDoor, EchelonIngredient)
DEFINE_CLASS_CAST(AESwitchObject, EchelonIngredient)
DEFINE_CLASS_CAST(AETelemetricDevice, EchelonIngredient)
DEFINE_CLASS_CAST(AETurretConsole, EchelonIngredient)
DEFINE_CLASS_CAST(UComputerAccessComponent, EchelonIngredient)
DEFINE_CLASS_CAST(UComputerEmail, EchelonIngredient)
DEFINE_CLASS_CAST(UComputerImage, EchelonIngredient)
DEFINE_CLASS_CAST(UComputerMusic, EchelonIngredient)

DEFINE_CLASS_CAST(AActor, Engine)
DEFINE_CLASS_CAST(AAntiPortalActor, Engine)
DEFINE_CLASS_CAST(ABrush, Engine)
DEFINE_CLASS_CAST(ACamera, Engine)
DEFINE_CLASS_CAST(ACubemapSampleInfo, Engine)
DEFINE_CLASS_CAST(AEOceanInfo, Engine)
DEFINE_CLASS_CAST(AEOceanWaterInfo, Engine)
DEFINE_CLASS_CAST(AERainVolume, Engine)
DEFINE_CLASS_CAST(AEmitter, Engine)
DEFINE_CLASS_CAST(AInfo, Engine)
DEFINE_CLASS_CAST(AKeypoint, Engine)
DEFINE_CLASS_CAST(ALight, Engine)
DEFINE_CLASS_CAST(ANavigationPoint, Engine)
DEFINE_CLASS_CAST(APawn, Engine)
DEFINE_CLASS_CAST(APhysicsVolume, Engine)
DEFINE_CLASS_CAST(APlayerController, Engine)
DEFINE_CLASS_CAST(APlayerStart, Engine)
DEFINE_CLASS_CAST(AProjector, Engine)
DEFINE_CLASS_CAST(AScout, Engine)
DEFINE_CLASS_CAST(ASkyZoneInfo, Engine)
DEFINE_CLASS_CAST(AStaticMeshActor, Engine)
DEFINE_CLASS_CAST(ATerrainInfo, Engine)
DEFINE_CLASS_CAST(AVolume, Engine)
DEFINE_CLASS_CAST(UBitmapMaterial, Engine)
DEFINE_CLASS_CAST(UEGlow, Engine)
DEFINE_CLASS_CAST(UEHkRigidBody, Engine)
DEFINE_CLASS_CAST(UEHkSkeletalSystem, Engine)
DEFINE_CLASS_CAST(UMaterial, Engine)
DEFINE_CLASS_CAST(UModifier, Engine)
DEFINE_CLASS_CAST(UPlayerInput, Engine)
DEFINE_CLASS_CAST(USCX_basic_material, Engine)
DEFINE_CLASS_CAST(USCX_particle_material, Engine)
DEFINE_CLASS_CAST(USCX_reflection_material, Engine)
DEFINE_CLASS_CAST(UShader, Engine)
DEFINE_CLASS_CAST(USkeletalMesh, Engine)
DEFINE_CLASS_CAST(USkeletalMeshInstance, Engine)
DEFINE_CLASS_CAST(USound, Engine)
DEFINE_CLASS_CAST(UStaticMesh, Engine)
DEFINE_CLASS_CAST(UTexture, Engine)
DEFINE_CLASS_CAST(UViewport, Engine)
