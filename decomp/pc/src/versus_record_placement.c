/* Native record placement callbacks recovered from the Versus class registry. */

extern const void *const vtable_UPackage[];

struct record_UPackage {
    const void * methods;
    unsigned char gap_04[0x2c];
    unsigned int field_30;
};

void place_UPackage(struct record_UPackage *self)
{
    if (self) {
        self->methods = vtable_UPackage;
        self->field_30 = 0;
    }
}

extern const void *const initial_table_USystem_28[];
extern const void *const vtable_USystem[];
extern const void *const secondary_table_USystem_28[];

struct record_USystem {
    const void * methods;
    unsigned char gap_04[0x24];
    const void * volatile field_28;
};

void place_USystem(struct record_USystem *self)
{
    if (self) {
        self->field_28 = initial_table_USystem_28;
        self->methods = vtable_USystem;
        self->field_28 = secondary_table_USystem_28;
    }
}

extern const void *const vtable_UStruct[];

struct record_UStruct {
    const void * methods;
    unsigned char gap_04[0x38];
    unsigned int field_3c;
    unsigned int field_40;
};

void place_UStruct(struct record_UStruct *self)
{
    if (self) {
        self->methods = vtable_UStruct;
        self->field_3c = 0;
        self->field_40 = 0;
    }
}

extern const void *const vtable_UState[];

struct record_UState {
    const void * methods;
    unsigned char gap_04[0x38];
    unsigned int field_3c;
    unsigned int field_40;
    unsigned char gap_44[0x3c];
    unsigned int field_80;
    unsigned int field_84;
};

void place_UState(struct record_UState *self)
{
    if (self) {
        self->field_3c = 0;
        self->field_40 = 0;
        self->methods = vtable_UState;
        self->field_80 = 0;
        self->field_84 = 0;
    }
}

extern const void *const vtable_UFunction[];

struct record_UFunction {
    const void * methods;
    unsigned char gap_04[0x38];
    unsigned int field_3c;
    unsigned int field_40;
};

void place_UFunction(struct record_UFunction *self)
{
    if (self) {
        self->field_3c = 0;
        self->field_40 = 0;
        self->methods = vtable_UFunction;
    }
}

extern const void *const initial_table_UControlChannel_58[];
extern const void *const vtable_UControlChannel[];
extern const void *const secondary_table_UControlChannel_58[];

struct record_UControlChannel {
    const void * methods;
    unsigned char gap_04[0x54];
    const void * volatile field_58;
};

void place_UControlChannel(struct record_UControlChannel *self)
{
    if (self) {
        self->field_58 = initial_table_UControlChannel_58;
        self->methods = vtable_UControlChannel;
        self->field_58 = secondary_table_UControlChannel_58;
    }
}

extern const void *const vtable_UActorChannel[];

struct record_UActorChannel {
    const void * methods;
    unsigned char gap_04[0x7c];
    unsigned int field_80;
    unsigned int field_84;
    unsigned int field_88;
    unsigned int field_8c;
    unsigned int field_90;
    unsigned int field_94;
    unsigned int field_98;
    unsigned int field_9c;
    unsigned int field_a0;
};

void place_UActorChannel(struct record_UActorChannel *self)
{
    if (self) {
        self->methods = vtable_UActorChannel;
        self->field_80 = 0;
        self->field_84 = 0;
        self->field_88 = 0;
        self->field_8c = 0;
        self->field_90 = 0;
        self->field_94 = 0;
        self->field_98 = 0;
        self->field_9c = 0;
        self->field_a0 = 0;
    }
}

extern const void *const vtable_UPalette[];

struct record_UPalette {
    const void * methods;
    unsigned char gap_04[0x24];
    unsigned int field_28;
    unsigned int field_2c;
    unsigned int field_30;
    unsigned int field_34;
    unsigned int field_38;
};

void place_UPalette(struct record_UPalette *self)
{
    if (self) {
        self->methods = vtable_UPalette;
        self->field_28 = 0;
        self->field_2c = 0;
        self->field_30 = 0;
        self->field_34 = 0;
        self->field_38 = 0;
    }
}
