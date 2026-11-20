/* Scalar deleting destructors generated for Unreal classes in this build.
   The single-field arguments preserve the x86 thiscall stack layout in C. */
struct Allocation { void *object; };
struct Allocator;
struct AllocatorOps {
    void *reserved[2];
    void (__fastcall *free)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct AllocatorOps *ops; };
extern struct Allocator *engine_allocator;
struct DeleteFlags { unsigned value; };

#define DEFINE_DELETING_DESTRUCTOR(Type)                                  \
    struct Type;                                                         \
    extern void __fastcall destroy_##Type(struct Type *self);            \
    void *__fastcall delete_##Type(struct Type *self, struct DeleteFlags flags) \
    {                                                                    \
        destroy_##Type(self);                                            \
        if (flags.value & 1) {                                           \
            struct Allocation allocation;                               \
            allocation.object = self;                                    \
            engine_allocator->ops->free(engine_allocator, allocation);   \
        }                                                                \
        return self;                                                     \
    }

DEFINE_DELETING_DESTRUCTOR(AActor)
DEFINE_DELETING_DESTRUCTOR(ABaseCivilian)
DEFINE_DELETING_DESTRUCTOR(ABaseSoldier)
DEFINE_DELETING_DESTRUCTOR(AE3DMapSystem)
DEFINE_DELETING_DESTRUCTOR(AEAIController)
DEFINE_DELETING_DESTRUCTOR(AEAIManager)
DEFINE_DELETING_DESTRUCTOR(AEAIZone)
DEFINE_DELETING_DESTRUCTOR(AEAlarmLight)
DEFINE_DELETING_DESTRUCTOR(AEAmbianceSplitter)
DEFINE_DELETING_DESTRUCTOR(AEAnimatedObject)
DEFINE_DELETING_DESTRUCTOR(AEBaseDoor)
DEFINE_DELETING_DESTRUCTOR(AECar)
DEFINE_DELETING_DESTRUCTOR(AEComputerSystem)
DEFINE_DELETING_DESTRUCTOR(AEConveyor)
DEFINE_DELETING_DESTRUCTOR(AECruiseMissileHead)
DEFINE_DELETING_DESTRUCTOR(AECryoTank)
DEFINE_DELETING_DESTRUCTOR(AEDetonator)
DEFINE_DELETING_DESTRUCTOR(AEDoorOpener)
DEFINE_DELETING_DESTRUCTOR(AEElevatorPanel)
DEFINE_DELETING_DESTRUCTOR(AEGameplayObject)
DEFINE_DELETING_DESTRUCTOR(AEHackingGameplay)
DEFINE_DELETING_DESTRUCTOR(AEHkSystem)
DEFINE_DELETING_DESTRUCTOR(AEKeyPad)
DEFINE_DELETING_DESTRUCTOR(AELight)
DEFINE_DELETING_DESTRUCTOR(AELightning)
DEFINE_DELETING_DESTRUCTOR(AEMedKit)
DEFINE_DELETING_DESTRUCTOR(AEMusic)
DEFINE_DELETING_DESTRUCTOR(AEPattern)
DEFINE_DELETING_DESTRUCTOR(AEPawn)
DEFINE_DELETING_DESTRUCTOR(AEPlayerController)
DEFINE_DELETING_DESTRUCTOR(AEScopeAttachment)
DEFINE_DELETING_DESTRUCTOR(AESensor)
DEFINE_DELETING_DESTRUCTOR(AESoftBodyActor)
DEFINE_DELETING_DESTRUCTOR(AESoundVolume)
DEFINE_DELETING_DESTRUCTOR(AESpeechDispatcher)
DEFINE_DELETING_DESTRUCTOR(AESurroundAmbience)
DEFINE_DELETING_DESTRUCTOR(AESwitchObject)
DEFINE_DELETING_DESTRUCTOR(AEWeapon)
DEFINE_DELETING_DESTRUCTOR(AEchelonGameInfo)
DEFINE_DELETING_DESTRUCTOR(AEchelonLevelInfo)
DEFINE_DELETING_DESTRUCTOR(AEvent)
DEFINE_DELETING_DESTRUCTOR(AGameInfo)
DEFINE_DELETING_DESTRUCTOR(ALight)
DEFINE_DELETING_DESTRUCTOR(ANavigationPoint)
DEFINE_DELETING_DESTRUCTOR(APawn)
DEFINE_DELETING_DESTRUCTOR(ASpecialVolume)
DEFINE_DELETING_DESTRUCTOR(AZoneInfo)
DEFINE_DELETING_DESTRUCTOR(UBeamEmitter)
DEFINE_DELETING_DESTRUCTOR(UCleanerShader)
DEFINE_DELETING_DESTRUCTOR(UCommandlet)
DEFINE_DELETING_DESTRUCTOR(UComputerEmail)
DEFINE_DELETING_DESTRUCTOR(UConsole)
DEFINE_DELETING_DESTRUCTOR(UConvexVolume)
DEFINE_DELETING_DESTRUCTOR(UEAdInfoObject)
DEFINE_DELETING_DESTRUCTOR(UEBarkManager)
DEFINE_DELETING_DESTRUCTOR(UECinInfo)
DEFINE_DELETING_DESTRUCTOR(UEEEV)
DEFINE_DELETING_DESTRUCTOR(UEEmotion)
DEFINE_DELETING_DESTRUCTOR(UEHollywoodManager)
DEFINE_DELETING_DESTRUCTOR(UEPatrolInfo)
DEFINE_DELETING_DESTRUCTOR(UESearchManager)
DEFINE_DELETING_DESTRUCTOR(UETrainingData)
DEFINE_DELETING_DESTRUCTOR(UFont)
DEFINE_DELETING_DESTRUCTOR(ULevelSummary)
DEFINE_DELETING_DESTRUCTOR(UMeshAnimation)
DEFINE_DELETING_DESTRUCTOR(UParticleEmitter)
DEFINE_DELETING_DESTRUCTOR(UParticleMaterial)
DEFINE_DELETING_DESTRUCTOR(USCMagmaInteraction)
DEFINE_DELETING_DESTRUCTOR(UStreamer)
DEFINE_DELETING_DESTRUCTOR(UStruct)
DEFINE_DELETING_DESTRUCTOR(USystem)
DEFINE_DELETING_DESTRUCTOR(UTextBuffer)
