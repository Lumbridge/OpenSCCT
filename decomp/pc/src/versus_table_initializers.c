struct Object { const void *const *methods; };

extern void __fastcall initialize_2ac8b0(struct Object *self);

extern const void *const methods_2f0d08[];
extern const void *const methods_2f0d58[];

#define INITIALIZE(Entry, Base, Methods) \
    struct Object *__fastcall Entry(struct Object *self) \
    { \
        Base(self); \
        self->methods = Methods; \
        return self; \
    }

INITIALIZE(init_2a13a0, initialize_2ac8b0, methods_2f0d08)
INITIALIZE(init_2a13c0, initialize_2ac8b0, methods_2f0d58)
