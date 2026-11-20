struct Allocation { void *object; };
struct Allocator;
struct AllocatorOps {
    void *reserved[2];
    void (__fastcall *free)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct AllocatorOps *ops; };
extern struct Allocator *engine_allocator;
struct DeleteFlags { unsigned value; };
extern int __fastcall conditional_destroy(void *object);

/* These classes destroy an embedded member between the UObject cleanup and
   base destructor. The placement constructor establishes each class vtable. */
#define DEFINE_MEMBER_DESTRUCTION(Type, MemberOffset)                         \
    struct Type {                                                             \
        const void *vtable;                                                   \
        unsigned char before_member[(MemberOffset) - sizeof(void *)];         \
        unsigned char member[1];                                              \
    };                                                                        \
    extern const void *const vtable_##Type[];                                 \
    extern void __fastcall destroy_member_##Type(void *member);               \
    extern void __fastcall destroy_base_##Type(struct Type *self);            \
    void *__fastcall delete_##Type(struct Type *self, struct DeleteFlags flags) \
    {                                                                         \
        self->vtable = vtable_##Type;                                         \
        conditional_destroy(self);                                            \
        destroy_member_##Type(self->member);                                  \
        destroy_base_##Type(self);                                            \
        if (flags.value & 1) {                                                \
            struct Allocation allocation;                                    \
            allocation.object = self;                                         \
            engine_allocator->ops->free(engine_allocator, allocation);       \
        }                                                                     \
        return self;                                                          \
    }

DEFINE_MEMBER_DESTRUCTION(AAntiPortalActor, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(ABasePattern, 0x7b8)
DEFINE_MEMBER_DESTRUCTION(ACubemapSampleInfo, 0x2c0)
DEFINE_MEMBER_DESTRUCTION(AEAIVolume, 0x3d4)
DEFINE_MEMBER_DESTRUCTION(AEChangedActorManager, 0x360)
DEFINE_MEMBER_DESTRUCTION(AECoopHackRope, 0x2f0)
DEFINE_MEMBER_DESTRUCTION(AECoopTeammateShoulders, 0x2e8)
DEFINE_MEMBER_DESTRUCTION(AECryoTankSide, 0x414)
DEFINE_MEMBER_DESTRUCTION(AEElectroChromaticTrigger, 0x418)
DEFINE_MEMBER_DESTRUCTION(AEEmergencyLight, 0x438)
DEFINE_MEMBER_DESTRUCTION(AEFireVolume, 0x404)
DEFINE_MEMBER_DESTRUCTION(AEGOListener, 0x2b8)
DEFINE_MEMBER_DESTRUCTION(AEGameplayObjectLight, 0x3fc)
DEFINE_MEMBER_DESTRUCTION(AEGenerator, 0x40c)
DEFINE_MEMBER_DESTRUCTION(AEGoggle, 0x2c8)
DEFINE_MEMBER_DESTRUCTION(AEHelpNote, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(AEIRSensor, 0x410)
DEFINE_MEMBER_DESTRUCTION(AEInfoPoint, 0x2b8)
DEFINE_MEMBER_DESTRUCTION(AEInventoryItem, 0x3fc)
DEFINE_MEMBER_DESTRUCTION(AEKeyButton, 0x408)
DEFINE_MEMBER_DESTRUCTION(AELaserMicVolume, 0x404)
DEFINE_MEMBER_DESTRUCTION(AEMainGun, 0x53c)
DEFINE_MEMBER_DESTRUCTION(AEOcclusionZone, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(AEPickLockSystem, 0x45c)
DEFINE_MEMBER_DESTRUCTION(AEPlayerInfo, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(AEPowerBox, 0x3fc)
DEFINE_MEMBER_DESTRUCTION(AEProjectorScreen, 0x3fc)
DEFINE_MEMBER_DESTRUCTION(AEPuddle, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(AERetinalScanner, 0x420)
DEFINE_MEMBER_DESTRUCTION(AESprinkler, 0x424)
DEFINE_MEMBER_DESTRUCTION(AETurret, 0x538)
DEFINE_MEMBER_DESTRUCTION(AEWeaponAttachment, 0x410)
DEFINE_MEMBER_DESTRUCTION(AEZoneInfo, 0x3b4)
DEFINE_MEMBER_DESTRUCTION(AEmitter, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(AHUD, 0x2c4)
DEFINE_MEMBER_DESTRUCTION(ANavMeshActor, 0x340)
DEFINE_MEMBER_DESTRUCTION(ANote, 0x2b4)
DEFINE_MEMBER_DESTRUCTION(ATrigger, 0x2b8)
DEFINE_MEMBER_DESTRUCTION(UComputerImage, 0x64)
DEFINE_MEMBER_DESTRUCTION(UComputerMusic, 0x64)
DEFINE_MEMBER_DESTRUCTION(UComputerObjectControl, 0x64)
DEFINE_MEMBER_DESTRUCTION(UComputerTrigger, 0x64)
DEFINE_MEMBER_DESTRUCTION(UConst, 0x34)
DEFINE_MEMBER_DESTRUCTION(UDecalTextureCollection, 0x28)
DEFINE_MEMBER_DESTRUCTION(UEGlobalMissionManager, 0x2c)
DEFINE_MEMBER_DESTRUCTION(UEHkSkeletalSystem, 0x48)
DEFINE_MEMBER_DESTRUCTION(UEInteractionManager, 0x28)
DEFINE_MEMBER_DESTRUCTION(UEInventory, 0x2c)
DEFINE_MEMBER_DESTRUCTION(UERandomSoundEvent, 0x28)
DEFINE_MEMBER_DESTRUCTION(UESRAManager, 0x28)
DEFINE_MEMBER_DESTRUCTION(UETextureManager, 0x28)
DEFINE_MEMBER_DESTRUCTION(UETransitionController, 0x38)
DEFINE_MEMBER_DESTRUCTION(UEnum, 0x34)
DEFINE_MEMBER_DESTRUCTION(UGlobalTempObjects, 0x28)
DEFINE_MEMBER_DESTRUCTION(UIndexBuffer, 0x2c)
DEFINE_MEMBER_DESTRUCTION(UInteractionMaster, 0x34)
DEFINE_MEMBER_DESTRUCTION(UPalette, 0x28)
DEFINE_MEMBER_DESTRUCTION(UPolys, 0x28)
DEFINE_MEMBER_DESTRUCTION(URainDropShader, 0x54)
DEFINE_MEMBER_DESTRUCTION(USCMagmaInteractionMPGame, 0x98)
DEFINE_MEMBER_DESTRUCTION(USCMagmaInteractionPREGame, 0x9c)
DEFINE_MEMBER_DESTRUCTION(USound, 0x44)
DEFINE_MEMBER_DESTRUCTION(USparkEmitter, 0x378)
DEFINE_MEMBER_DESTRUCTION(UUbiAccountManager, 0x34)
DEFINE_MEMBER_DESTRUCTION(UVertexBuffer, 0x38)
DEFINE_MEMBER_DESTRUCTION(UVertexStreamCOLOR, 0x38)
DEFINE_MEMBER_DESTRUCTION(UVertexStreamPosNormTex, 0x38)
DEFINE_MEMBER_DESTRUCTION(UVertexStreamUV, 0x38)
DEFINE_MEMBER_DESTRUCTION(UVertexStreamVECTOR, 0x38)
