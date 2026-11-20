#include "record_deletion.h"

extern void __cdecl release_record(struct Record *object);
extern void __fastcall destroy_00ba70(struct Record *self);
extern void __fastcall destroy_00c6b0(struct Record *self);
extern void __fastcall destroy_00c880(struct Record *self);
extern void __fastcall destroy_021410(struct Record *self);
extern void __fastcall destroy_021fe0(struct Record *self);
extern void __fastcall destroy_051330(struct Record *self);
extern void __fastcall destroy_08cac0(struct Record *self);
extern void __fastcall destroy_0c6a20(struct Record *self);
extern void __fastcall destroy_0c7120(struct Record *self);
extern void __fastcall destroy_0da8e0(struct Record *self);
extern void __fastcall destroy_0e0e20(struct Record *self);
extern void __fastcall destroy_0e4e20(struct Record *self);
extern void __fastcall destroy_0ebb20(struct Record *self);
extern void __fastcall destroy_0f3640(struct Record *self);
extern void __fastcall destroy_146aa0(struct Record *self);
extern void __fastcall destroy_1a7980(struct Record *self);
extern void __fastcall destroy_1af880(struct Record *self);
extern void __fastcall destroy_1cd3a0(struct Record *self);
extern void __fastcall destroy_1cd470(struct Record *self);
extern void __fastcall destroy_1d00b0(struct Record *self);
extern void __fastcall destroy_1d1180(struct Record *self);
extern void __fastcall destroy_1d8690(struct Record *self);
extern void __fastcall destroy_1e1cc0(struct Record *self);
extern void __fastcall destroy_211420(struct Record *self);
extern void __fastcall destroy_2660a0(struct Record *self);
extern void __fastcall destroy_2d7bd0(struct Record *self);
extern void __fastcall destroy_2e3030(struct Record *self);
extern void __fastcall destroy_2f5230(struct Record *self);
extern void __fastcall destroy_2f52d0(struct Record *self);
extern void __fastcall destroy_31f550(struct Record *self);
extern void __fastcall destroy_388860(struct Record *self);
extern void __fastcall destroy_3a3a20(struct Record *self);
extern void __fastcall destroy_3c8300(struct Record *self);
extern void __fastcall destroy_48b2e0(struct Record *self);
extern void __fastcall destroy_491520(struct Record *self);
extern void __fastcall destroy_4963c0(struct Record *self);
extern void __fastcall destroy_4989e0(struct Record *self);
extern void __fastcall destroy_49cf50(struct Record *self);
extern void __fastcall destroy_49d610(struct Record *self);
extern void __fastcall destroy_4a1ae0(struct Record *self);
extern void __fastcall destroy_4b3b10(struct Record *self);
extern void __fastcall destroy_4b6e10(struct Record *self);
extern void __fastcall destroy_4b7920(struct Record *self);
extern void __fastcall destroy_4b9b00(struct Record *self);
extern void __fastcall destroy_4bbd40(struct Record *self);
extern void __fastcall destroy_4c1260(struct Record *self);
extern void __fastcall destroy_4c3310(struct Record *self);
extern void __fastcall destroy_4c3360(struct Record *self);
extern void __fastcall destroy_4c6950(struct Record *self);
extern void __fastcall destroy_4c82a0(struct Record *self);
extern void __fastcall destroy_4df680(struct Record *self);
extern void __fastcall destroy_4e5500(struct Record *self);
extern void __fastcall destroy_4eb980(struct Record *self);
extern void __fastcall destroy_4ed550(struct Record *self);
extern void __fastcall destroy_4eeef0(struct Record *self);
extern void __fastcall destroy_4f1d30(struct Record *self);
extern void __fastcall destroy_4f6760(struct Record *self);

DELETE_RECORD(delete_record_00c850, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_00cc70, destroy_00c6b0, free_on_heap)
DELETE_RECORD(delete_record_00d0a0, destroy_00c880, free_on_heap)
DELETE_RECORD(delete_record_021e40, destroy_021410, free_on_heap)
DELETE_RECORD(delete_record_022030, destroy_021fe0, free_on_heap)
DELETE_RECORD(delete_record_0513f0, destroy_051330, free_on_heap)
DELETE_RECORD(delete_record_05b9b0, destroy_4f1d30, release_record)
DELETE_RECORD(delete_record_08d290, destroy_08cac0, free_on_heap)
DELETE_RECORD(delete_record_0c72f0, destroy_0c7120, free_on_heap)
DELETE_RECORD(delete_record_0c8320, destroy_1cd3a0, free_on_heap)
DELETE_RECORD(delete_record_0c83a0, destroy_1cd470, free_on_heap)
DELETE_RECORD(delete_record_0dcd10, destroy_0da8e0, free_on_heap)
DELETE_RECORD(delete_record_0e4160, destroy_0e0e20, free_on_heap)
DELETE_RECORD(delete_record_0eba50, destroy_0e4e20, free_on_heap)
DELETE_RECORD(delete_record_0f3610, destroy_0ebb20, free_on_heap)
DELETE_RECORD(delete_record_0fd570, destroy_0f3640, free_on_heap)
DELETE_RECORD(delete_record_146fd0, destroy_146aa0, free_on_heap)
DELETE_RECORD(delete_record_15f7d0, destroy_0c6a20, free_on_heap)
DELETE_RECORD(delete_record_16d5e0, destroy_1d00b0, free_on_heap)
DELETE_RECORD(delete_record_1af940, destroy_1af880, free_on_heap)
DELETE_RECORD(delete_record_1cdec0, destroy_211420, free_on_heap)
DELETE_RECORD(delete_record_1cdf70, destroy_1d1180, free_on_heap)
DELETE_RECORD(delete_record_1d77d0, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_1d8c00, destroy_1d8690, free_on_heap)
DELETE_RECORD(delete_record_1e1d40, destroy_1e1cc0, free_on_heap)
DELETE_RECORD(delete_record_210540, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_221780, destroy_2660a0, free_on_heap)
DELETE_RECORD(delete_record_23cbf0, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_276e90, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_2d7da0, destroy_2d7bd0, free_on_heap)
DELETE_RECORD(delete_record_2e3330, destroy_2e3030, free_on_heap)
DELETE_RECORD(delete_record_2f52a0, destroy_2f5230, free_on_heap)
DELETE_RECORD(delete_record_2f5410, destroy_2f52d0, free_on_heap)
DELETE_RECORD(delete_record_31f6d0, destroy_31f550, free_on_heap)
DELETE_RECORD(delete_record_32ec60, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_332290, destroy_1a7980, free_on_heap)
DELETE_RECORD(delete_record_37ffa0, destroy_3a3a20, free_on_heap)
DELETE_RECORD(delete_record_389710, destroy_388860, free_on_heap)
DELETE_RECORD(delete_record_3c8370, destroy_3c8300, free_on_heap)
DELETE_RECORD(delete_record_3cf330, destroy_00ba70, free_on_heap)
DELETE_RECORD(delete_record_47dac0, destroy_49d610, release_record)
DELETE_RECORD(delete_record_490470, destroy_4c3310, release_record)
DELETE_RECORD(delete_record_490490, destroy_4c3360, release_record)
DELETE_RECORD(delete_record_498040, destroy_4963c0, release_record)
DELETE_RECORD(delete_record_49f010, destroy_4a1ae0, release_record)
DELETE_RECORD(delete_record_4a7710, destroy_4b7920, release_record)
DELETE_RECORD(delete_record_4b3b70, destroy_4b3b10, release_record)
DELETE_RECORD(delete_record_4bf420, destroy_4c1260, release_record)
DELETE_RECORD(delete_record_4c1890, destroy_4c6950, release_record)
DELETE_RECORD(delete_record_4c18b0, destroy_4df680, release_record)
DELETE_RECORD(delete_record_4c4130, destroy_4b6e10, release_record)
DELETE_RECORD(delete_record_4c4150, destroy_4bbd40, release_record)
DELETE_RECORD(delete_record_4c4170, destroy_4b9b00, release_record)
DELETE_RECORD(delete_record_4c4190, destroy_49cf50, release_record)
DELETE_RECORD(delete_record_4c7620, destroy_4989e0, release_record)
DELETE_RECORD(delete_record_4c89b0, destroy_4c82a0, release_record)
DELETE_RECORD(delete_record_4c89d0, destroy_4e5500, release_record)
DELETE_RECORD(delete_record_4ce6e0, destroy_491520, release_record)
DELETE_RECORD(delete_record_4e0440, destroy_48b2e0, release_record)
DELETE_RECORD(delete_record_4e0a70, destroy_4eb980, release_record)
DELETE_RECORD(delete_record_4e75f0, destroy_4ed550, release_record)
DELETE_RECORD(delete_record_4e7610, destroy_4eeef0, release_record)
DELETE_RECORD(delete_record_4f1fe0, destroy_4f6760, release_record)
