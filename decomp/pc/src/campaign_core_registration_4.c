#include "campaign_class_registration.h"

extern const unsigned short package_Core[];

CLASS_GETTER(UClass, Core)
CLASS_GETTER(ULinker, Core)
CLASS_GETTER(UObject, Core)

extern int object_system_ready;
#define object_system_initialized() object_system_ready
REGISTER_RUNTIME_CLASS(ULinkerLoad, ULinker, UObject)
REGISTER_RUNTIME_CLASS(ULinkerSave, ULinker, UObject)
