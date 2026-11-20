/* Virtual methods identified by a registered class table and slot. A table
   can inherit an entry from a base class. Stack words preserve the member
   calling convention where the original parameter types are unknown. */
struct stack_word { unsigned int value; };

unsigned int __fastcall UState_slot_38_r00c8f0(void *self, struct stack_word arg1)
{
    return ~0u;
}

unsigned int __fastcall UState_slot_40_r00c900(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6, struct stack_word arg7)
{
    return 1;
}

unsigned int __fastcall UState_slot_41_r00c910(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5)
{
    return 0;
}

unsigned int __fastcall UState_slot_26_r00cda0(void *self)
{
    return *(const unsigned int *)((const unsigned char *)self + 0x34);
}

unsigned char __fastcall UClassProperty_slot_39_r03ef50(void *self)
{
    return 5;
}

unsigned int __fastcall UState_slot_36_r03f410(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4)
{
    return 0;
}

void __fastcall AInfo_slot_77_r04a010(void *self, struct stack_word arg1, struct stack_word arg2)
{
}

void __fastcall UObject_slot_6_r04a550(void *self, struct stack_word arg1)
{
}

unsigned int __fastcall UObject_slot_0_r04a560(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3)
{
    return 0;
}

unsigned int __fastcall UState_slot_44_r0543a0(void *self, struct stack_word arg1, struct stack_word arg2)
{
    return 0;
}

unsigned int __fastcall UArrayProperty_slot_31_r055aa0(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3)
{
    return 1;
}

unsigned char __fastcall UConstantColor_slot_30_r068db0(void *self)
{
    return 1;
}

void __fastcall UObject_slot_15_r068f20(void *self)
{
}

unsigned int __fastcall UState_slot_27_r069ef0(void *self)
{
    return *(const unsigned int *)((const unsigned char *)self + 0x28);
}

unsigned int __fastcall UObject_slot_11_r06ac20(void *self)
{
    return 0;
}

unsigned int __fastcall UEnum_slot_23_r071b80(void *self)
{
    return 1;
}

unsigned int __fastcall UObject_slot_1_r071bb0(void *self, struct stack_word arg1)
{
    return 0;
}

unsigned int __fastcall UPrimitive_slot_27_r09e980(void *self, struct stack_word arg1)
{
    return 1;
}

void * __fastcall UProxyBitmapMaterial_slot_36_r0a6660(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3)
{
    return self;
}

unsigned int __fastcall ASDisk_slot_79_r10bd10(void *self)
{
    return ~0u;
}

unsigned int __fastcall UMapProperty_slot_29_r056580(void *self, struct stack_word arg1, struct stack_word arg2)
{
    return 1;
}

void __fastcall UMapProperty_slot_30_r056590(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3)
{
}

void __fastcall ACamera_slot_83_r0565d0(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4)
{
}

void __fastcall UGUIHorzList_slot_44_r09e990(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6)
{
}

unsigned int __fastcall UVertexBuffer_slot_22_r10a650(void *self)
{
    return *(const unsigned int *)((const unsigned char *)self + 0x38);
}

void __fastcall UArrayProperty_slot_22_r0562c0(void *self, struct stack_word arg1)
{
    *(unsigned int *)((unsigned char *)self + 0x58) = arg1.value;
}

unsigned int __fastcall UTextBufferFactory_slot_23_r015b00(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6, struct stack_word arg7, struct stack_word arg8, struct stack_word arg9)
{
    return 0;
}

unsigned int __fastcall UTextBufferFactory_slot_24_r015b10(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6)
{
    return 0;
}

void * __fastcall UTexMatrix_slot_35_r0a9d10(void *self, struct stack_word arg1)
{
    return (unsigned char *)self + 0x58;
}

unsigned int __fastcall UNetPendingLevel_slot_23_r0a80a0(void *self)
{
    return *(const unsigned int *)((const unsigned char *)self + 0xec);
}

void __fastcall UPersistentData_slot_42_r09e9a0(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6, struct stack_word arg7)
{
}

void __fastcall UPersistentData_slot_47_r09e9b0(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5)
{
}

void __fastcall UPersistentData_slot_49_r068f10(void *self, struct stack_word arg1)
{
    *(unsigned int *)((unsigned char *)self + 0x10) = arg1.value;
}

unsigned char __fastcall UPersistentData_slot_68_r068da0(void *self)
{
    return 0;
}

void __fastcall UPersistentData_slot_71_r068d40(void *self, struct stack_word arg1, struct stack_word arg2, struct stack_word arg3, struct stack_word arg4, struct stack_word arg5, struct stack_word arg6, struct stack_word arg7, struct stack_word arg8, struct stack_word arg9)
{
}
