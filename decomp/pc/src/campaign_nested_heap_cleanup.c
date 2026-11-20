#include "record_deletion.h"

extern const void *const methods_7de808[];
extern const void *const methods_7de8c8[];
extern const void *const methods_7fba18[];
extern const void *const methods_7fba24[];
extern const void *const methods_7fba30[];
extern const void *const methods_7fba9c[];
extern const void *const methods_83ac74[];
extern const void *const methods_876268[];
extern const void *const methods_8768b0[];
extern const void *const methods_876c90[];
extern const void *const methods_876cd8[];
extern const void *const methods_876f10[];
extern const void *const methods_877500[];
extern void __cdecl release_0014a0(struct Record *object);
extern void __cdecl release_1dd0d0(struct Record *object);
extern void __cdecl release_469670(struct Record *object);
extern void __cdecl release_747ae0(struct Record *object);
extern void __cdecl release_75ae00(struct Record *object);
extern void __cdecl release_75c0f0(struct Record *object);
extern void __cdecl release_75cbc0(struct Record *object);
extern void __cdecl release_75d300(struct Record *object);
extern void __cdecl release_75eac0(struct Record *object);
extern void __cdecl release_75ee20(struct Record *object);
extern void __cdecl release_760fe0(struct Record *object);
extern void __cdecl release_7653d0(struct Record *object);
extern void __cdecl release_765e50(struct Record *object);
extern void __cdecl release_767150(struct Record *object);
extern void __cdecl release_76f7b0(struct Record *object);
extern void __fastcall cleanup_1cb2d0(void *object);
extern void __fastcall cleanup_1d40e0(void *object);
extern void __fastcall cleanup_1dc7b0(void *object);
extern void __fastcall cleanup_1dcd80(void *object);
extern void __fastcall cleanup_2142c0(void *object);
extern void __fastcall cleanup_22a160(void *object);
extern void __fastcall cleanup_337470(void *object);
extern void __fastcall cleanup_337730(void *object);
extern void __fastcall cleanup_337e00(void *object);
extern void __fastcall cleanup_337e70(void *object);
extern void __fastcall cleanup_337ef0(void *object);
extern void __fastcall cleanup_338480(void *object);
extern void __fastcall cleanup_4230b0(void *object);
extern void __fastcall cleanup_42c900(void *object);
extern void __fastcall cleanup_4390c0(void *object);
extern void __fastcall cleanup_4395d0(void *object);
extern void __fastcall cleanup_439670(void *object);
extern void __fastcall cleanup_43a830(void *object);
extern void __fastcall cleanup_43b350(void *object);
extern void __fastcall cleanup_448d40(void *object);
extern void __fastcall cleanup_44de90(void *object);
extern void __fastcall cleanup_450330(void *object);
extern void __fastcall cleanup_4592b0(void *object);
extern void __fastcall cleanup_465510(void *object);
extern void __fastcall cleanup_467470(void *object);
extern void __fastcall cleanup_46b410(void *object);
extern void __fastcall cleanup_4743a0(void *object);
extern void __fastcall cleanup_47ba40(void *object);
extern void __fastcall cleanup_47ba80(void *object);
extern void __fastcall cleanup_47cf40(void *object);
extern void __fastcall cleanup_491520(void *object);
extern void __fastcall cleanup_4acf90(void *object);
extern void __fastcall cleanup_4fbb00(void *object);
extern void __fastcall cleanup_4fc8e0(void *object);
extern void __fastcall cleanup_5f5ad0(void *object);
extern void __fastcall cleanup_5f6fb0(void *object);
extern void __fastcall cleanup_5f77c0(void *object);
extern void __fastcall cleanup_5f77d0(void *object);
extern void __fastcall cleanup_5f7e50(void *object);
extern void __fastcall cleanup_5f7f10(void *object);
extern void __fastcall cleanup_5f7fb0(void *object);
extern void __fastcall cleanup_5fdf70(void *object);
extern void __fastcall cleanup_601e90(void *object);
extern void __fastcall cleanup_604640(void *object);
extern void __fastcall cleanup_616880(void *object);
extern void __fastcall cleanup_6205a0(void *object);
extern void __fastcall cleanup_621c90(void *object);
extern void __fastcall cleanup_62a7a0(void *object);
extern void __fastcall cleanup_62b080(void *object);
extern void __fastcall cleanup_748420(void *object);
extern void __fastcall cleanup_74c0a0(void *object);
extern void __fastcall cleanup_753fd0(void *object);
extern void __fastcall cleanup_75c080(void *object);
extern void __fastcall cleanup_75c100(void *object);
extern void __fastcall cleanup_75d0f0(void *object);
extern void __fastcall cleanup_75d970(void *object);
extern void __fastcall cleanup_75e050(void *object);
extern void __fastcall cleanup_75eee0(void *object);
extern void __fastcall cleanup_75f380(void *object);
extern void __fastcall cleanup_75f910(void *object);
extern void __fastcall cleanup_7617d0(void *object);
extern void __fastcall cleanup_762ad0(void *object);
extern void __fastcall cleanup_765410(void *object);
extern void __fastcall cleanup_767110(void *object);
extern void __fastcall cleanup_7678b0(void *object);
extern void __fastcall cleanup_768660(void *object);
extern void __fastcall cleanup_76da00(void *object);
extern void __fastcall cleanup_76f460(void *object);
extern void __fastcall cleanup_76f7e0(void *object);
extern void __fastcall cleanup_76f8d0(void *object);
extern void __fastcall cleanup_76f930(void *object);
extern void __fastcall cleanup_772190(void *object);
extern void __fastcall cleanup_772b40(void *object);
extern void __fastcall cleanup_775850(void *object);
extern void __stdcall release_73c870(struct Record *object);

struct Record *__fastcall delete_members_1cbab0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_1cb2d0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_1d40c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_1d40e0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_1db610(struct Record *self, struct DeleteFlags flags)
{
    cleanup_43a830(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_1db8e0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4395d0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_1db900(struct Record *self, struct DeleteFlags flags)
{
    cleanup_439670(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_1dcc00(struct Record *self, struct DeleteFlags flags)
{
    cleanup_1dc7b0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_1dcd60(struct Record *self, struct DeleteFlags flags)
{
    cleanup_1dcd80(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_214480(struct Record *self, struct DeleteFlags flags)
{
    cleanup_2142c0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_21c680(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7de808;
    cleanup_450330(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_21ce40(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7de8c8;
    cleanup_448d40(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_22a140(struct Record *self, struct DeleteFlags flags)
{
    cleanup_22a160(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_3380d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7fba18;
    cleanup_337ef0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_338100(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7fba30;
    cleanup_337e00(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_338170(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7fba24;
    cleanup_337e70(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_3382f0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_337730(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_3391f0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_7fba9c;
    cleanup_338480(self);
    cleanup_337470(self);
    cleanup_337730(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_423380(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4230b0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_428f20(struct Record *self, struct DeleteFlags flags)
{
    cleanup_44de90(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_42cae0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_42c900(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_438f20(struct Record *self, struct DeleteFlags flags)
{
    cleanup_465510(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_439530(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4390c0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_43b4b0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_43b350(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_44de60(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_83ac74;
    cleanup_467470(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_4526c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_450330(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_454c30(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4592b0(self);
    if (flags.value & 1) release_1dd0d0(self);
    return self;
}

struct Record *__fastcall delete_members_467a80(struct Record *self, struct DeleteFlags flags)
{
    cleanup_46b410(self);
    if (flags.value & 1) release_469670(self);
    return self;
}

struct Record *__fastcall delete_members_468260(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4743a0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_47c000(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47ba80(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_47c020(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47ba40(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_47e580(struct Record *self, struct DeleteFlags flags)
{
    cleanup_491520((unsigned char *)self + 8);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_4a3280(struct Record *self, struct DeleteFlags flags)
{
    cleanup_47cf40((unsigned char *)self + 12);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_4cf2a0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4acf90((unsigned char *)self + 12);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_4fbbb0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4fbb00(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_4fd2b0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_4fc8e0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5f7e30(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f7e50(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5f7e60(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f7f10(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5f7f90(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f7fb0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5f9a40(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f77c0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5f9a60(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f77d0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5fa990(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f5ad0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_5fdf50(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5fdf70(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_601e70(struct Record *self, struct DeleteFlags flags)
{
    cleanup_601e90(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_604620(struct Record *self, struct DeleteFlags flags)
{
    cleanup_604640(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_60f8d0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_5f6fb0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_619010(struct Record *self, struct DeleteFlags flags)
{
    cleanup_616880(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_620fd0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_6205a0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_624ac0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_621c90(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_62cbd0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_62b080(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_62d4f0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_62a7a0(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_73caf0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_748420(self);
    if (flags.value & 1) release_747ae0(self);
    return self;
}

struct Record *__fastcall delete_members_74c3c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_74c0a0(self);
    cleanup_74c0a0(self);
    if (flags.value & 1) release_73c870(self);
    return self;
}

struct Record *__fastcall delete_members_74d6f0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75c080(self);
    if (flags.value & 1) release_75ae00(self);
    return self;
}

struct Record *__fastcall delete_members_74d710(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75c100(self);
    if (flags.value & 1) release_75c0f0(self);
    return self;
}

struct Record *__fastcall delete_members_7502f0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75d0f0(self);
    if (flags.value & 1) release_75cbc0(self);
    return self;
}

struct Record *__fastcall delete_members_750460(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75d970(self);
    if (flags.value & 1) release_75d300(self);
    return self;
}

struct Record *__fastcall delete_members_7515c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75f380(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_751b50(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75f910(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_754300(struct Record *self, struct DeleteFlags flags)
{
    cleanup_753fd0(self);
    if (flags.value & 1) release_75eac0(self);
    return self;
}

struct Record *__fastcall delete_members_755140(struct Record *self, struct DeleteFlags flags)
{
    cleanup_7617d0(self);
    if (flags.value & 1) release_760fe0(self);
    return self;
}

struct Record *__fastcall delete_members_75e260(struct Record *self, struct DeleteFlags flags)
{
    cleanup_765410(self);
    if (flags.value & 1) release_7653d0(self);
    return self;
}

struct Record *__fastcall delete_members_75e4d0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_75e050(self);
    if (flags.value & 1) release_0014a0(self);
    return self;
}

struct Record *__fastcall delete_members_761050(struct Record *self, struct DeleteFlags flags)
{
    cleanup_767110(self);
    if (flags.value & 1) release_765e50(self);
    return self;
}

struct Record *__fastcall delete_members_7621b0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_876268;
    cleanup_75eee0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_762ec0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_762ad0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_766190(struct Record *self, struct DeleteFlags flags)
{
    cleanup_7678b0(self);
    if (flags.value & 1) release_767150(self);
    return self;
}

struct Record *__fastcall delete_members_767160(struct Record *self, struct DeleteFlags flags)
{
    cleanup_76f7e0(self);
    if (flags.value & 1) release_76f7b0(self);
    return self;
}

struct Record *__fastcall delete_members_768da0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_768660(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_7693d0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_8768b0;
    cleanup_75eee0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_76da80(struct Record *self, struct DeleteFlags flags)
{
    cleanup_76da00(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_76ea70(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_876c90;
    cleanup_75eee0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_76edd0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_876cd8;
    cleanup_75eee0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_76f4c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_76f460(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_76f8e0(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_876f10;
    cleanup_75eee0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_7703c0(struct Record *self, struct DeleteFlags flags)
{
    cleanup_76f930(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_772290(struct Record *self, struct DeleteFlags flags)
{
    cleanup_772190(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_772e50(struct Record *self, struct DeleteFlags flags)
{
    cleanup_772b40(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_775280(struct Record *self, struct DeleteFlags flags)
{
    self->methods = methods_877500;
    cleanup_76f8d0(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}

struct Record *__fastcall delete_members_777750(struct Record *self, struct DeleteFlags flags)
{
    cleanup_775850(self);
    if (flags.value & 1) release_75ee20(self);
    return self;
}
