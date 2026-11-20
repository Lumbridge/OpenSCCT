#include "campaign_class_registration.h"

extern const unsigned short package_Core[];
extern const unsigned short package_Echelon[];
extern const unsigned short package_Engine[];

CLASS_GETTER(AActor, Engine)
CLASS_GETTER(AEGameplayObject, Echelon)
CLASS_GETTER(AEInteractObject, Engine)
CLASS_GETTER(APawn, Engine)
CLASS_GETTER(UClass, Core)
CLASS_GETTER(UField, Core)
CLASS_GETTER(UObject, Core)
CLASS_GETTER(UProperty, Core)
CLASS_GETTER(UStruct, Core)

REGISTER_RUNTIME_CLASS(ABrush, AActor, UObject)
REGISTER_RUNTIME_CLASS(AEObjectiveInteraction, AEInteractObject, UObject)
REGISTER_RUNTIME_CLASS(AEPawn, APawn, UObject)
REGISTER_RUNTIME_CLASS(AESensor, AEGameplayObject, UObject)
REGISTER_RUNTIME_CLASS(UArrayProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UBoolProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UByteProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UConst, UField, UStruct)
REGISTER_RUNTIME_CLASS(UFixedArrayProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UFloatProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UIntProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UMapProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UMaterial, UObject, UObject)
REGISTER_RUNTIME_CLASS(UNameProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UObjectProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UState, UStruct, UObject)
REGISTER_RUNTIME_CLASS(UStrProperty, UProperty, UField)
REGISTER_RUNTIME_CLASS(UStructProperty, UProperty, UField)
