struct Object { const void *methods; };

extern const void *const methods_2f1c94[];

#define INIT_1(Entry, Methods, Offset1, Value1) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        self->methods = Methods; \
        *(unsigned char *)((unsigned char *)self + Offset1) = Value1; \
        return self; \
    }

INIT_1(init_2b7750, methods_2f1c94, 4, 0x0u)
