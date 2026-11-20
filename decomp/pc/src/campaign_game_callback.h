#include "campaign_vm_frame.h"

typedef void (__fastcall *ObjectMethod)(Object *);
typedef void (__cdecl *StaticMethod)(void);

#define SCRIPT_COMMAND(name)                                            \
    void __fastcall native_##name(Object *);                            \
    HANDLER(exec_##name, void)                                          \
    {                                                                   \
        finish_arguments(frame);                                        \
        native_##name(self);                                            \
    }

#define SCRIPT_QUERY(name, type)                                        \
    type __fastcall native_##name(Object *);                            \
    HANDLER(exec_##name, type)                                          \
    {                                                                   \
        finish_arguments(frame);                                        \
        *result = native_##name(self);                                  \
    }

#define SCRIPT_VIRTUAL_COMMAND(name, slot)                              \
    HANDLER(exec_##name, void)                                          \
    {                                                                   \
        ObjectMethod *methods;                                          \
        finish_arguments(frame);                                        \
        methods = *(ObjectMethod **)self;                               \
        methods[slot](self);                                            \
    }

#define SCRIPT_EMPTY(name)                                              \
    HANDLER(exec_##name, void)                                          \
    {                                                                   \
        finish_arguments(frame);                                        \
    }

#define SCRIPT_STATIC_COMMAND(name, method)                             \
    void __cdecl method(void);                                          \
    HANDLER(exec_##name, void)                                          \
    {                                                                   \
        finish_arguments(frame);                                        \
        method();                                                       \
    }
