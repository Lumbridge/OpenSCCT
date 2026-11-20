#include "record_copy.h"

struct Record4 { unsigned int data[1]; };
struct Record8 { unsigned int data[2]; };
struct Record32 { unsigned int data[8]; };

CONSTRUCT_COPIES(r29f610, Record8)
CONSTRUCT_RANGE(r2a9140, Record8)
CONSTRUCT_COPIES(r2ab840, Record4)
CONSTRUCT_RANGE(r2afd60, Record4)
CONSTRUCT_COPIES(r2b7fe0, Record32)
CONSTRUCT_RANGE(r2b8010, Record32)
