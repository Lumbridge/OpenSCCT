#include "campaign_array_resize_abi.h"

struct FSArray { struct ArrayBuffer buffer; int count; };
union Stride { int bytes; float stack_word; };
/* Carries an integer stride on the x86 member-call stack. */
void __fastcall resize_fsarray(struct FSArray *array, struct ArraySize stride);
