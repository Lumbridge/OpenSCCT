struct Object { const void *methods; };

extern const void *const methods_7c1720[];
extern const void *const methods_7c1dd4[];
extern const void *const methods_7d3240[];
extern const void *const methods_7db9b4[];
extern const void *const methods_83acf4[];
extern const void *const methods_83bafc[];
extern const void *const methods_83e6f0[];
extern const void *const methods_83f3c0[];
extern const void *const methods_84aa70[];
extern const void *const methods_84b414[];
extern const void *const methods_8759e0[];
extern const void *const methods_876640[];

#define INIT_1(Entry, Methods, Offset1, Value1) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        self->methods = Methods; \
        *(unsigned char *)((unsigned char *)self + Offset1) = Value1; \
        return self; \
    }

#define INIT_2(Entry, Methods, Offset1, Value1) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        self->methods = Methods; \
        *(unsigned short *)((unsigned char *)self + Offset1) = Value1; \
        return self; \
    }

#define INIT_4(Entry, Methods, Offset1, Value1) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        self->methods = Methods; \
        *(unsigned *)((unsigned char *)self + Offset1) = Value1; \
        return self; \
    }

#define INIT_44(Entry, Methods, Offset1, Value1, Offset2, Value2) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        self->methods = Methods; \
        *(unsigned *)((unsigned char *)self + Offset1) = Value1; \
        *(unsigned *)((unsigned char *)self + Offset2) = Value2; \
        return self; \
    }

INIT_44(init_080000, methods_7c1720, 12, 0x0u, 16, 0xffffffffu)
INIT_2(init_08b090, methods_7c1dd4, 6, 0x1u)
INIT_44(init_11f790, methods_7d3240, 12, 0x0u, 16, 0xffffffffu)
INIT_1(init_1e7b90, methods_7db9b4, 4, 0x0u)
INIT_1(init_44e400, methods_83acf4, 4, 0x0u)
INIT_4(init_467460, methods_83bafc, 4, 0x0u)
INIT_2(init_4a4680, methods_83e6f0, 4, 0x1u)
INIT_4(init_4ad360, methods_83f3c0, 4, 0x0u)
INIT_1(init_5129e0, methods_84aa70, 4, 0x0u)
INIT_1(init_53a200, methods_84b414, 4, 0x0u)
INIT_4(init_75ead0, methods_8759e0, 8, 0x0u)
INIT_4(init_7679f0, methods_876640, 28, 0x0u)
