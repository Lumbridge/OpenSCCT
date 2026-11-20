#include "record_deletion.h"

extern void __fastcall destroy_241af0(struct Record *self);
extern void __stdcall release_record(struct Record *object);

DELETE_RECORD(delete_record_256d30, destroy_241af0, release_record)
