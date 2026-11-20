#include "record_deletion.h"

extern const void *const methods_7d8d88[];
extern const void *const methods_7d9358[];
extern const void *const methods_7d9540[];
extern const void *const methods_7d9710[];
extern const void *const methods_83c658[];
extern const void *const methods_83cd30[];
extern const void *const methods_83e14c[];
extern const void *const methods_83e7d0[];
extern const void *const methods_83e810[];
extern const void *const methods_83f0f4[];
extern const void *const methods_83f7f0[];
extern const void *const methods_840c28[];
extern const void *const methods_84114c[];
extern const void *const methods_842548[];
extern const void *const methods_842778[];
extern const void *const methods_842b48[];
extern const void *const methods_844a78[];
extern const void *const methods_844b78[];
extern const void *const methods_8463b8[];
extern const void *const methods_8464a8[];
extern const void *const methods_84677c[];
extern const void *const methods_846950[];
extern const void *const methods_847744[];
extern const void *const methods_84890c[];
extern const void *const methods_848d68[];
extern const void *const methods_848da8[];
extern const void *const methods_848ed0[];
extern const void *const methods_849340[];
extern const void *const methods_8713a8[];
extern void __cdecl release_487b90(struct Record *object);
extern void __fastcall cleanup_1fdb50(void *object);
extern void __fastcall cleanup_203fa0(void *object);
extern void __fastcall cleanup_2049d0(void *object);
extern void __fastcall cleanup_2057c0(void *object);
extern void __fastcall cleanup_47cf40(void *object);
extern void __fastcall cleanup_48b280(void *object);
extern void __fastcall cleanup_48edb0(void *object);
extern void __fastcall cleanup_493420(void *object);
extern void __fastcall cleanup_49c6d0(void *object);
extern void __fastcall cleanup_49d570(void *object);
extern void __fastcall cleanup_4a2740(void *object);
extern void __fastcall cleanup_4a2970(void *object);
extern void __fastcall cleanup_4a4690(void *object);
extern void __fastcall cleanup_4a7330(void *object);
extern void __fastcall cleanup_4adea0(void *object);
extern void __fastcall cleanup_4ae3c0(void *object);
extern void __fastcall cleanup_4b4810(void *object);
extern void __fastcall cleanup_4b4c90(void *object);
extern void __fastcall cleanup_4b8c30(void *object);
extern void __fastcall cleanup_4be470(void *object);
extern void __fastcall cleanup_4c13b0(void *object);
extern void __fastcall cleanup_4c7370(void *object);
extern void __fastcall cleanup_4cd690(void *object);
extern void __fastcall cleanup_4d38e0(void *object);
extern void __fastcall cleanup_4d52d0(void *object);
extern void __fastcall cleanup_4d6770(void *object);
extern void __fastcall cleanup_4d6b70(void *object);
extern void __fastcall cleanup_4dac90(void *object);
extern void __fastcall cleanup_4dacc0(void *object);
extern void __fastcall cleanup_4dc1a0(void *object);
extern void __fastcall cleanup_4dd4f0(void *object);
extern void __fastcall cleanup_4dfef0(void *object);
extern void __fastcall cleanup_4e4e20(void *object);
extern void __fastcall cleanup_4e99f0(void *object);
extern void __fastcall cleanup_4e9b80(void *object);
extern void __fastcall cleanup_4f4360(void *object);
extern void __fastcall cleanup_4f4ca0(void *object);
extern void __fastcall cleanup_4f9870(void *object);
extern void __fastcall cleanup_4f9ae0(void *object);
extern void __fastcall cleanup_73b0d0(void *object);

struct Record *__fastcall delete_members_19cd70(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d8d88;
    cleanup_1fdb50((unsigned char *)self + 72);
    cleanup_4f4ca0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_19f490(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9358;
    cleanup_203fa0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_19fe20(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9540;
    cleanup_2049d0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_1a05e0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9710;
    cleanup_2057c0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_1a0d40(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9358;
    cleanup_203fa0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_204090(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9540;
    cleanup_2049d0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_2056f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d9710;
    cleanup_2057c0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_480820(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83c658;
    cleanup_49d570((unsigned char *)self + 2896);
    cleanup_49d570((unsigned char *)self + 2892);
    cleanup_4b4810((unsigned char *)self + 2628);
    cleanup_4b4c90((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_48b4b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83cd30;
    cleanup_48b280((unsigned char *)self + 52);
    cleanup_4be470((unsigned char *)self + 4);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_49be50(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83e14c;
    cleanup_48edb0((unsigned char *)self + 16);
    cleanup_4ae3c0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4a4ce0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83e810;
    cleanup_4d52d0((unsigned char *)self + 68);
    self->methods = methods_83e7d0;
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4a7ca0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83f0f4;
    cleanup_4d6770((unsigned char *)self + 44);
    cleanup_4a7330((unsigned char *)self + 8);
    cleanup_4d6b70(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4a7d60(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47cf40((unsigned char *)self + 4);
    cleanup_47cf40(self);
    cleanup_49d570(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4b17f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83f7f0;
    cleanup_48edb0((unsigned char *)self + 16);
    cleanup_4ae3c0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4b8070(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_840c28;
    cleanup_4b8c30(self);
    cleanup_4a2970(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4b9150(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_84114c;
    cleanup_48edb0((unsigned char *)self + 44);
    cleanup_48edb0((unsigned char *)self + 32);
    cleanup_4d38e0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4bbcb0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_842548;
    cleanup_48edb0((unsigned char *)self + 32);
    cleanup_4d38e0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4bd500(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_842778;
    cleanup_48edb0((unsigned char *)self + 32);
    cleanup_4d38e0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4c15f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_842b48;
    cleanup_4dd4f0((unsigned char *)self + 36);
    cleanup_47cf40((unsigned char *)self + 16);
    cleanup_4a4690(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4d2e40(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_844a78;
    cleanup_4e99f0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4d3480(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_844b78;
    cleanup_49c6d0((unsigned char *)self + 96);
    cleanup_4e99f0((unsigned char *)self + 84);
    cleanup_4e99f0((unsigned char *)self + 76);
    self->methods = methods_844a78;
    cleanup_4e99f0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4da620(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8463b8;
    cleanup_49c6d0((unsigned char *)self + 32);
    cleanup_48edb0((unsigned char *)self + 8);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4dae80(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8464a8;
    cleanup_4cd690((unsigned char *)self + 48);
    cleanup_4adea0((unsigned char *)self + 8);
    cleanup_4e9b80(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4dba30(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_84677c;
    cleanup_4dacc0(self);
    cleanup_4dac90(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4dca50(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_846950;
    cleanup_4e4e20((unsigned char *)self + 56);
    cleanup_4dc1a0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4e1340(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_846950;
    cleanup_4e4e20((unsigned char *)self + 56);
    cleanup_4dc1a0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4e13f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_846950;
    cleanup_4e4e20((unsigned char *)self + 56);
    cleanup_4dc1a0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4e1f80(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_847744;
    cleanup_49c6d0((unsigned char *)self + 116);
    cleanup_4c13b0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4efaa0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_84890c;
    cleanup_493420((unsigned char *)self + 12);
    cleanup_4dfef0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4f2620(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_848d68;
    cleanup_49d570((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4f2980(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_848da8;
    cleanup_4c7370((unsigned char *)self + 184);
    cleanup_4f4360((unsigned char *)self + 84);
    self->methods = methods_848d68;
    cleanup_49d570((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4f4770(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_848ed0;
    cleanup_49d570((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4f4cc0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_848ed0;
    cleanup_49d570((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_4f6150(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_849340;
    cleanup_4f9ae0((unsigned char *)self + 2116);
    cleanup_4f9870((unsigned char *)self + 72);
    cleanup_4f4ca0(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_73a120(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8713a8;
    cleanup_73b0d0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}

struct Record *__fastcall delete_members_73a6d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8713a8;
    cleanup_73b0d0((unsigned char *)self + 68);
    cleanup_4a2740(self);
    if (flags.value & 1) release_487b90(self);
    return self;
}
