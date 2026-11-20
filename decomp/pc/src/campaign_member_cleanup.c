#include "record_deletion.h"

extern const void *const methods_7bee58[];
extern const void *const methods_7beeb8[];
extern const void *const methods_7bf2a8[];
extern const void *const methods_7bf398[];
extern const void *const methods_7bf470[];
extern const void *const methods_7bf700[];
extern const void *const methods_7bfb38[];
extern const void *const methods_7c0158[];
extern const void *const methods_7c0b98[];
extern const void *const methods_7c1270[];
extern const void *const methods_7c13c0[];
extern const void *const methods_7c1508[];
extern const void *const methods_7c3e90[];
extern const void *const methods_7c4470[];
extern const void *const methods_7c4728[];
extern const void *const methods_7c49c8[];
extern const void *const methods_7c51a8[];
extern const void *const methods_7c5730[];
extern const void *const methods_7c71d8[];
extern const void *const methods_7c7470[];
extern const void *const methods_7c9c08[];
extern const void *const methods_7ca1f8[];
extern const void *const methods_7ccb80[];
extern const void *const methods_7ccd68[];
extern const void *const methods_7cceb8[];
extern const void *const methods_7cd150[];
extern const void *const methods_7cd3e8[];
extern const void *const methods_7cddd8[];
extern const void *const methods_7cde48[];
extern const void *const methods_7ce3a8[];
extern const void *const methods_7ce640[];
extern const void *const methods_7ce8d8[];
extern const void *const methods_7cf500[];
extern const void *const methods_7cf588[];
extern const void *const methods_7cf6c8[];
extern const void *const methods_7d0a60[];
extern const void *const methods_7d0c78[];
extern const void *const methods_7d28a0[];
extern const void *const methods_7d38e8[];
extern const void *const methods_7d3950[];
extern const void *const methods_7d6ba0[];
extern const void *const methods_7d79f0[];
extern const void *const methods_7d82a0[];
extern const void *const methods_7d8340[];
extern const void *const methods_7ddec0[];
extern const void *const methods_7ed940[];
extern const void *const methods_7f4428[];
extern const void *const methods_7f8f70[];
extern const void *const methods_7fed70[];
extern const void *const methods_8090d0[];
extern const void *const methods_80ad88[];
extern const void *const methods_80c010[];
extern const void *const methods_810c38[];
extern const void *const methods_830548[];
extern const void *const methods_8336b0[];
extern void __fastcall cleanup_00ba70(void *object);
extern void __fastcall cleanup_019210(void *object);
extern void __fastcall cleanup_02ab20(void *object);
extern void __fastcall cleanup_02ad20(void *object);
extern void __fastcall cleanup_0671a0(void *object);
extern void __fastcall cleanup_06a850(void *object);
extern void __fastcall cleanup_0734b0(void *object);
extern void __fastcall cleanup_07b5c0(void *object);
extern void __fastcall cleanup_07c520(void *object);
extern void __fastcall cleanup_082650(void *object);
extern void __fastcall cleanup_094a00(void *object);
extern void __fastcall cleanup_094e60(void *object);
extern void __fastcall cleanup_0a1310(void *object);
extern void __fastcall cleanup_0a9f10(void *object);
extern void __fastcall cleanup_0ae280(void *object);
extern void __fastcall cleanup_0c41e0(void *object);
extern void __fastcall cleanup_106b00(void *object);
extern void __fastcall cleanup_12aac0(void *object);
extern void __fastcall cleanup_1487b0(void *object);
extern void __fastcall cleanup_16df60(void *object);
extern void __fastcall cleanup_174c50(void *object);
extern void __fastcall cleanup_174e90(void *object);
extern void __fastcall cleanup_2ee4f0(void *object);
extern void __fastcall cleanup_38c4b0(void *object);
extern void __fastcall cleanup_38c9f0(void *object);
extern void __fastcall cleanup_3c8540(void *object);
extern void __fastcall cleanup_3d7a30(void *object);
extern void __fastcall cleanup_3dc460(void *object);
extern void __fastcall cleanup_3e8560(void *object);
extern void __fastcall cleanup_47cf40(void *object);
extern void __fastcall cleanup_48acd0(void *object);

struct Record *__fastcall delete_members_05e6d0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_48acd0((unsigned char *)self + 8);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_05e8d0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47cf40((unsigned char *)self + 8);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0674c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_0671a0((unsigned char *)self + 16);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_067ea0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bee58;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_067f00(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7beeb8;
    cleanup_3d7a30(self);
    self->methods = methods_7bee58;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06a820(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06a880(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bf398;
    cleanup_3d7a30(self);
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06a930(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bf470;
    cleanup_3d7a30(self);
    self->methods = methods_7bf398;
    cleanup_3d7a30(self);
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06ac00(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bf700;
    cleanup_3d7a30(self);
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06ae80(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7bfb38;
    cleanup_3d7a30(self);
    self->methods = methods_7bf700;
    cleanup_3d7a30(self);
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_06b230(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c0158;
    cleanup_3d7a30(self);
    self->methods = methods_7bf398;
    cleanup_3d7a30(self);
    self->methods = methods_7bf2a8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_073320(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c0b98;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_078810(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_07c180(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c1270;
    cleanup_3d7a30(self);
    cleanup_07b5c0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_07c310(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c13c0;
    cleanup_3d7a30(self);
    cleanup_07b5c0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_07c3b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c1508;
    cleanup_3d7a30(self);
    cleanup_07b5c0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_08c700(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_08e940(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c3e90;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_094390(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c4728;
    cleanup_3d7a30(self);
    cleanup_00ba70((unsigned char *)self + 844);
    self->methods = methods_7c4470;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_094450(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c49c8;
    cleanup_3d7a30(self);
    self->methods = methods_7c4728;
    cleanup_3d7a30(self);
    cleanup_00ba70((unsigned char *)self + 844);
    self->methods = methods_7c4470;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_094860(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c5730;
    cleanup_3d7a30(self);
    self->methods = methods_7c51a8;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_094f10(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c71d8;
    cleanup_3d7a30(self);
    cleanup_094e60(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_094f70(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7c7470;
    cleanup_3d7a30(self);
    cleanup_094e60(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0a13e0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ca1f8;
    cleanup_3d7a30(self);
    cleanup_0a1310(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0aad80(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ccb80;
    cleanup_0a9f10(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0ad8d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ccd68;
    cleanup_082650((unsigned char *)self + 132);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0aed50(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cceb8;
    cleanup_3d7a30(self);
    cleanup_02ab20((unsigned char *)self + 828);
    cleanup_094a00(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0aee10(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cd150;
    cleanup_3d7a30(self);
    cleanup_0ae280((unsigned char *)self + 744);
    cleanup_094a00(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0aef10(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cd3e8;
    cleanup_3d7a30(self);
    cleanup_0ae280((unsigned char *)self + 780);
    cleanup_094a00(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0b58d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cddd8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0b59c0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cde48;
    cleanup_3d7a30(self);
    self->methods = methods_7cddd8;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0c4240(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ce640;
    cleanup_3d7a30(self);
    self->methods = methods_7ce3a8;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0c42b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ce8d8;
    cleanup_3d7a30(self);
    cleanup_0c41e0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0cd060(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cf500;
    cleanup_3d7a30(self);
    self->methods = methods_7c0b98;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0cd500(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cf588;
    cleanup_3d7a30(self);
    self->methods = methods_7c0b98;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_0cd7d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7cf6c8;
    cleanup_3d7a30(self);
    self->methods = methods_7cf588;
    cleanup_3d7a30(self);
    self->methods = methods_7c0b98;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_10c930(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d0c78;
    cleanup_3d7a30(self);
    self->methods = methods_7d0a60;
    cleanup_3d7a30(self);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_117190(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d28a0;
    cleanup_3d7a30(self);
    cleanup_06a850(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_12aba0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d38e8;
    cleanup_3d7a30(self);
    cleanup_12aac0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_12ac10(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d3950;
    cleanup_3d7a30(self);
    cleanup_12aac0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_148840(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d6ba0;
    cleanup_3d7a30(self);
    cleanup_1487b0((unsigned char *)self + 44);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_16e6e0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d79f0;
    cleanup_3d7a30(self);
    cleanup_16df60(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_174e50(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d82a0;
    cleanup_3d7a30(self);
    cleanup_174c50((unsigned char *)self + 88);
    cleanup_0734b0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_174fd0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7d8340;
    cleanup_3d7a30(self);
    cleanup_174e90((unsigned char *)self + 764);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_1b0c20(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 100);
    cleanup_07c520(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_1d7840(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 52);
    cleanup_00ba70((unsigned char *)self + 40);
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_204430(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47cf40((unsigned char *)self + 16);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f130(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f170(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f1b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f1f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f230(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f270(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f2b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f2f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f330(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f370(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f3b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f3f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_20f430(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ddec0;
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_220c10(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_22de70(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_22e3b0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 68);
    cleanup_00ba70((unsigned char *)self + 36);
    cleanup_00ba70((unsigned char *)self + 24);
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_22e400(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 36);
    cleanup_00ba70((unsigned char *)self + 24);
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_22e440(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 32);
    cleanup_00ba70((unsigned char *)self + 20);
    cleanup_00ba70((unsigned char *)self + 8);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_23ad10(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_23ad40(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_2782c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_2ee810(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7ed940;
    cleanup_3d7a30(self);
    cleanup_2ee4f0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3005b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7f4428;
    cleanup_3d7a30(self);
    self->methods = methods_7c9c08;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_320c90(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7f8f70;
    cleanup_3d7a30(self);
    cleanup_106b00(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3290a0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 16);
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3400f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7fed70;
    cleanup_3d7a30(self);
    cleanup_00ba70((unsigned char *)self + 80);
    cleanup_00ba70((unsigned char *)self + 60);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_35fb00(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8090d0;
    cleanup_3d7a30(self);
    self->methods = methods_7c9c08;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_368f00(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_80c010;
    cleanup_3d7a30(self);
    self->methods = methods_80ad88;
    cleanup_3d7a30(self);
    self->methods = methods_7c9c08;
    cleanup_3d7a30(self);
    cleanup_02ad20(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_38d640(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_810c38;
    cleanup_38c9f0(self);
    cleanup_38c4b0(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3c87a0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_830548;
    cleanup_3d7a30(self);
    cleanup_3c8540((unsigned char *)self + 60);
    cleanup_3dc460(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3ca9a0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3dca80(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 24);
    cleanup_00ba70((unsigned char *)self + 12);
    cleanup_00ba70(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3dcbc0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 4);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_3e8610(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8336b0;
    cleanup_3d7a30(self);
    cleanup_3e8560(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_40bea0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 20);
    if (flags.value & 1) free_on_heap(self);
    return self;
}

struct Record *__fastcall delete_members_40e510(struct Record *self, struct DeleteFlags flags)
{
    cleanup_00ba70((unsigned char *)self + 76);
    cleanup_019210(self);
    if (flags.value & 1) free_on_heap(self);
    return self;
}
