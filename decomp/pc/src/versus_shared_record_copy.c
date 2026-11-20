#include "record_copy.h"

struct Record32 { unsigned int data[8]; };
struct Record4 { unsigned int data[1]; };
struct Record8 { unsigned int data[2]; };

ASSIGN_RANGE(r2b7f70, Record32)
ASSIGN_RANGE(r29f640, Record8)
ASSIGN_RANGE(r2afdc0, Record4)
