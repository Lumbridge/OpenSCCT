#include "record_deletion.h"

extern const void *const methods_2ff730[];
extern const void *const methods_303304[];
extern const void *const methods_303320[];
extern const void *const methods_3034b4[];
extern const void *const methods_3035fc[];
extern const void *const methods_303dc0[];
extern const void *const methods_3049cc[];
extern const void *const methods_307890[];
extern const void *const methods_30d248[];
extern const void *const methods_30e918[];

#define DESTROY(Entry, Methods) \
    struct Record *__fastcall Entry(struct Record *self, struct DeleteFlags flags) \
    { \
        self->methods = Methods; \
        if (flags.value & 1) free_on_heap(self); \
        return self; \
    }

DESTROY(destroy_004420, methods_30e918)
DESTROY(destroy_011720, methods_30d248)
DESTROY(destroy_0ab140, methods_307890)
DESTROY(destroy_0c3ad0, methods_3049cc)
DESTROY(destroy_0ff2c0, methods_303dc0)
DESTROY(destroy_112c40, methods_3035fc)
DESTROY(destroy_14c070, methods_3034b4)
DESTROY(destroy_1508b0, methods_303320)
DESTROY(destroy_1509e0, methods_303304)
DESTROY(destroy_17d860, methods_2ff730)
