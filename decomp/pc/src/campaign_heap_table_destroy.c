#include "record_deletion.h"

extern const void *const methods_7b7860[];
extern const void *const methods_7b78b4[];
extern const void *const methods_7b7e68[];
extern const void *const methods_7be874[];
extern const void *const methods_7bf388[];
extern const void *const methods_7c1d78[];
extern const void *const methods_7c1d80[];
extern const void *const methods_7da52c[];
extern const void *const methods_7da538[];
extern const void *const methods_7da644[];
extern const void *const methods_7db4f4[];
extern const void *const methods_7db750[];
extern const void *const methods_7db8a0[];
extern const void *const methods_7db9a4[];
extern const void *const methods_7dc328[];
extern const void *const methods_7defb4[];
extern const void *const methods_7e30ac[];
extern const void *const methods_7e43dc[];
extern const void *const methods_7e5e08[];
extern const void *const methods_7e6f2c[];
extern const void *const methods_7e75d4[];
extern const void *const methods_7e7728[];
extern const void *const methods_810254[];

#define DESTROY(Entry, Methods) \
    struct Record *__fastcall Entry(struct Record *self, struct DeleteFlags flags) \
    { \
        self->methods = Methods; \
        if (flags.value & 1) free_on_heap(self); \
        return self; \
    }

DESTROY(destroy_001510, methods_7b78b4)
DESTROY(destroy_0079d0, methods_7b7860)
DESTROY(destroy_009bf0, methods_7b7e68)
DESTROY(destroy_065a30, methods_7be874)
DESTROY(destroy_065bb0, methods_7be874)
DESTROY(destroy_065cd0, methods_7be874)
DESTROY(destroy_065f20, methods_7be874)
DESTROY(destroy_066310, methods_7be874)
DESTROY(destroy_06a610, methods_7bf388)
DESTROY(destroy_08a580, methods_7c1d78)
DESTROY(destroy_08a5c0, methods_7c1d80)
DESTROY(destroy_0abb90, methods_7bf388)
DESTROY(destroy_0b8730, methods_7bf388)
DESTROY(destroy_0da690, methods_7bf388)
DESTROY(destroy_11ffa0, methods_7bf388)
DESTROY(destroy_1242a0, methods_7bf388)
DESTROY(destroy_1255a0, methods_7bf388)
DESTROY(destroy_14ed00, methods_7bf388)
DESTROY(destroy_154b20, methods_7bf388)
DESTROY(destroy_1729c0, methods_7bf388)
DESTROY(destroy_1a4e80, methods_7bf388)
DESTROY(destroy_1a5d30, methods_7bf388)
DESTROY(destroy_1b6950, methods_7da538)
DESTROY(destroy_1b69c0, methods_7da52c)
DESTROY(destroy_1bf5c0, methods_7da644)
DESTROY(destroy_1c0820, methods_7da644)
DESTROY(destroy_1c1f80, methods_7da644)
DESTROY(destroy_1c2820, methods_7da644)
DESTROY(destroy_1c29c0, methods_7da644)
DESTROY(destroy_1c3100, methods_7da644)
DESTROY(destroy_1c3630, methods_7da644)
DESTROY(destroy_1c3930, methods_7da644)
DESTROY(destroy_1df220, methods_7db4f4)
DESTROY(destroy_1e2af0, methods_7db750)
DESTROY(destroy_1e72f0, methods_7db8a0)
DESTROY(destroy_1e7c30, methods_7db9a4)
DESTROY(destroy_1f0320, methods_7dc328)
DESTROY(destroy_1f0590, methods_7dc328)
DESTROY(destroy_2158e0, methods_7bf388)
DESTROY(destroy_2216d0, methods_7defb4)
DESTROY(destroy_251300, methods_7e30ac)
DESTROY(destroy_258e00, methods_7e43dc)
DESTROY(destroy_264960, methods_7e5e08)
DESTROY(destroy_2722a0, methods_7e6f2c)
DESTROY(destroy_273010, methods_7e6f2c)
DESTROY(destroy_273120, methods_7e6f2c)
DESTROY(destroy_273150, methods_7e6f2c)
DESTROY(destroy_273180, methods_7e6f2c)
DESTROY(destroy_2731b0, methods_7e6f2c)
DESTROY(destroy_2731e0, methods_7e6f2c)
DESTROY(destroy_273330, methods_7e6f2c)
DESTROY(destroy_273360, methods_7e6f2c)
DESTROY(destroy_277dd0, methods_7e75d4)
DESTROY(destroy_2795b0, methods_7e7728)
DESTROY(destroy_2799c0, methods_7e7728)
DESTROY(destroy_381490, methods_810254)
DESTROY(destroy_381630, methods_810254)
DESTROY(destroy_3ca6f0, methods_7b7e68)
DESTROY(destroy_3cdd70, methods_7b7e68)
DESTROY(destroy_3cdda0, methods_7b7e68)
DESTROY(destroy_3d7e60, methods_7b7e68)
DESTROY(destroy_3d7f90, methods_7b7e68)
DESTROY(destroy_3d9510, methods_7b7e68)
DESTROY(destroy_3e4e60, methods_7b7e68)
DESTROY(destroy_3e7d80, methods_7b7e68)
