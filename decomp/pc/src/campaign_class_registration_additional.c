#include "campaign_class_registration.h"

extern const unsigned short package_Core[];
extern const unsigned short package_Echelon[];
extern const unsigned short package_Engine[];

CLASS_GETTER(ACollisionMeshActor, Engine)
CLASS_GETTER(AEGameplayObject, Echelon)
CLASS_GETTER(AEHkConstraint, Engine)
CLASS_GETTER(AKeypoint, Engine)
CLASS_GETTER(UClass, Core)
CLASS_GETTER(UExporter, Core)
CLASS_GETTER(UObject, Core)
CLASS_GETTER(UShader, Engine)

/* These owner cells are identified by their retail registration routines. */
REGISTER_RUNTIME_CLASS(Descriptor_08F4A0, UObject, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_097FB0, AKeypoint, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_0980C0, AKeypoint, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_0A1570, AEHkConstraint, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_0C6390, ACollisionMeshActor, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_16E710, UShader, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_2EEE50, AEGameplayObject, UObject)
REGISTER_RUNTIME_CLASS(Descriptor_3E7ED0, UExporter, UObject)
