#include "campaign_class_registration.h"

extern const unsigned short package_Core[];

CLASS_GETTER(UClass, Core)
CLASS_GETTER(UPackage, Core)
CLASS_GETTER(UState, Core)

extern int object_system_ready;
#define object_system_initialized() object_system_ready
#undef object_system_initialized
void register_UClass(void) {
    struct RuntimeClass *parent = get_UState();
    if (parent != class_UClass) class_UClass->super_class = get_UState();
    else class_UClass->super_class = 0;
    class_UClass->within = get_UPackage();
    class_UClass->object_class = class_UClass;
    if (object_system_initialized() && class_UClass->object_class == class_UClass)
        class_UClass->methods->register_class(class_UClass);
}
#define object_system_initialized() object_system_ready
