struct RuntimeClass;
struct Object;
struct Name { unsigned index; };
extern void *allocation_context;
extern struct Object *allocate_object(struct RuntimeClass *, struct Object *, struct Name,
                                      unsigned, struct Object *, void *, void *, void *);

#define DEFINE_CLASS_ALLOCATION(Type, Package) \
    extern struct RuntimeClass *class_##Type; \
    extern struct RuntimeClass *create_##Type(const unsigned short *); \
    extern void register_##Type(void); \
    struct Object *allocate_##Type(unsigned size, struct Object *outer, struct Name name, unsigned flags) \
    { \
        if (!class_##Type) { \
            class_##Type = create_##Type(package_##Package); \
            register_##Type(); \
        } \
        return allocate_object(class_##Type, outer, name, flags, 0, allocation_context, 0, 0); \
    }

extern const unsigned short package_Core[];
extern const unsigned short package_Echelon[];
extern const unsigned short package_Engine[];
extern const unsigned short package_WinDrv[];
extern const unsigned short package_Window[];
DEFINE_CLASS_ALLOCATION(UAnimInfo, Engine)
DEFINE_CLASS_ALLOCATION(UArrayProperty, Core)
DEFINE_CLASS_ALLOCATION(UBoolProperty, Core)
DEFINE_CLASS_ALLOCATION(UByteProperty, Core)
DEFINE_CLASS_ALLOCATION(UEGlobalMissionManager, Echelon)
DEFINE_CLASS_ALLOCATION(UEGoal, Echelon)
DEFINE_CLASS_ALLOCATION(UEGoalList, Echelon)
DEFINE_CLASS_ALLOCATION(UEHkSkeletalSystem, Engine)
DEFINE_CLASS_ALLOCATION(UEListNode, Engine)
DEFINE_CLASS_ALLOCATION(UEOceanPrimitive, Engine)
DEFINE_CLASS_ALLOCATION(UEPCGameOptions, Engine)
DEFINE_CLASS_ALLOCATION(UERope, Engine)
DEFINE_CLASS_ALLOCATION(UESearchManager, Echelon)
DEFINE_CLASS_ALLOCATION(UETransitionController, Echelon)
DEFINE_CLASS_ALLOCATION(UEVirtualWaterPrimitive, Engine)
DEFINE_CLASS_ALLOCATION(UEWaterPrimitive, Engine)
DEFINE_CLASS_ALLOCATION(UEnum, Core)
DEFINE_CLASS_ALLOCATION(UFloatProperty, Core)
DEFINE_CLASS_ALLOCATION(UGlobalInteractionManager, Engine)
DEFINE_CLASS_ALLOCATION(UGlobalTempObjects, Engine)
DEFINE_CLASS_ALLOCATION(UInput, Engine)
DEFINE_CLASS_ALLOCATION(UIntProperty, Core)
DEFINE_CLASS_ALLOCATION(ULinkerLoad, Core)
DEFINE_CLASS_ALLOCATION(ULinkerSave, Core)
DEFINE_CLASS_ALLOCATION(UModel, Engine)
DEFINE_CLASS_ALLOCATION(UNameProperty, Core)
DEFINE_CLASS_ALLOCATION(UObjectProperty, Core)
DEFINE_CLASS_ALLOCATION(UOceanPatchSet, Engine)
DEFINE_CLASS_ALLOCATION(UPackage, Core)
DEFINE_CLASS_ALLOCATION(UPalette, Engine)
DEFINE_CLASS_ALLOCATION(UPrimitive, Engine)
DEFINE_CLASS_ALLOCATION(USMStreamer, Engine)
DEFINE_CLASS_ALLOCATION(UStrProperty, Core)
DEFINE_CLASS_ALLOCATION(UStruct, Core)
DEFINE_CLASS_ALLOCATION(UStructProperty, Core)
DEFINE_CLASS_ALLOCATION(USystem, Core)
DEFINE_CLASS_ALLOCATION(UTextBuffer, Core)
DEFINE_CLASS_ALLOCATION(UTexture, Engine)
DEFINE_CLASS_ALLOCATION(UTextureStreamer, Engine)
DEFINE_CLASS_ALLOCATION(UWinJoystickMgr, WinDrv)
DEFINE_CLASS_ALLOCATION(UWindMaker, Engine)
DEFINE_CLASS_ALLOCATION(UWindowManager, Window)
DEFINE_CLASS_ALLOCATION(UWindowsViewport, WinDrv)
