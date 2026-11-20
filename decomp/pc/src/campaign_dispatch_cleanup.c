#include "record_deletion.h"

struct PoolAllocation { struct Record *object; unsigned count; };

extern void __stdcall release_739a40(struct Record *object);
extern void __stdcall release_73c870(struct Record *object);
extern void __fastcall cleanup_4723d0(struct Record *object);
extern void __fastcall cleanup_478540(struct Record *object);
extern void __fastcall cleanup_478ce0(struct Record *object);
extern void __fastcall cleanup_47abd0(struct Record *object);
extern void __fastcall cleanup_47b460(struct Record *object);
extern void __fastcall cleanup_5e9230(struct Record *object);
extern void __fastcall cleanup_5ead00(struct Record *object);
extern void __fastcall cleanup_5ec320(struct Record *object);
extern void __fastcall cleanup_5ec6c0(struct Record *object);
extern void __fastcall cleanup_5eca60(struct Record *object);
extern void __fastcall cleanup_5ece00(struct Record *object);
extern void __fastcall cleanup_5ee420(struct Record *object);
extern void __fastcall cleanup_5ee8d0(struct Record *object);
extern void __fastcall cleanup_5eeca0(struct Record *object);
extern void __fastcall cleanup_5ef150(struct Record *object);
extern void __fastcall cleanup_5ef4a0(struct Record *object);
extern void __fastcall cleanup_5ef510(struct Record *object);
extern void __fastcall cleanup_5ef6f0(struct Record *object);
extern void __fastcall cleanup_5ef880(struct Record *object);
extern void __fastcall cleanup_5f18e0(struct Record *object);
extern void __fastcall cleanup_5f2730(struct Record *object);
extern void __fastcall cleanup_5f2cb0(struct Record *object);
extern void __fastcall cleanup_5f72d0(struct Record *object);
extern void __fastcall cleanup_5f7800(struct Record *object);
extern void __fastcall cleanup_5f7970(struct Record *object);
extern void __fastcall cleanup_5f7b00(struct Record *object);
extern void __fastcall cleanup_5fcf90(struct Record *object);
extern void __fastcall cleanup_5fd730(struct Record *object);
extern void __fastcall cleanup_5fd7b0(struct Record *object);
extern void __fastcall cleanup_5fdc20(struct Record *object);
extern void __fastcall cleanup_601820(struct Record *object);
extern void __fastcall cleanup_601990(struct Record *object);
extern void __fastcall cleanup_601b90(struct Record *object);
extern void __fastcall cleanup_601c50(struct Record *object);
extern void __fastcall cleanup_601f20(struct Record *object);
extern void __fastcall cleanup_603a60(struct Record *object);
extern void __fastcall cleanup_603e00(struct Record *object);
extern void __fastcall cleanup_604690(struct Record *object);
extern void __fastcall cleanup_60bb40(struct Record *object);
extern void __fastcall cleanup_6100a0(struct Record *object);
extern void __fastcall cleanup_610ab0(struct Record *object);
extern void __fastcall cleanup_6110b0(struct Record *object);
extern void __fastcall cleanup_6112e0(struct Record *object);
extern void __fastcall cleanup_61d5f0(struct Record *object);
extern void __fastcall cleanup_61e6d0(struct Record *object);
extern void __fastcall cleanup_6216a0(struct Record *object);
extern void __fastcall cleanup_622010(struct Record *object);
extern void __fastcall cleanup_622620(struct Record *object);
extern void __fastcall cleanup_626df0(struct Record *object);
extern void __fastcall cleanup_627b40(struct Record *object);
extern void __fastcall cleanup_628360(struct Record *object);
extern void __fastcall cleanup_628ef0(struct Record *object);
extern void __fastcall cleanup_62db10(struct Record *object);
extern void __fastcall cleanup_62e0f0(struct Record *object);
extern void __fastcall cleanup_62e730(struct Record *object);
extern void __fastcall cleanup_71f4f0(struct Record *object);
extern void __fastcall cleanup_71f580(struct Record *object);
extern void __fastcall cleanup_71f760(struct Record *object);
extern void __fastcall cleanup_721400(struct Record *object);
extern void __fastcall cleanup_722650(struct Record *object);
extern void __fastcall cleanup_722860(struct Record *object);
extern void __fastcall cleanup_722bb0(struct Record *object);
extern void __fastcall cleanup_722c90(struct Record *object);
extern void __fastcall cleanup_722d70(struct Record *object);
extern void __fastcall cleanup_727cc0(struct Record *object);
extern void __fastcall cleanup_7280f0(struct Record *object);
extern void __fastcall cleanup_728410(struct Record *object);
extern void __fastcall cleanup_728970(struct Record *object);
extern void __fastcall cleanup_728af0(struct Record *object);
extern void __fastcall cleanup_728b70(struct Record *object);
extern void __fastcall cleanup_728ba0(struct Record *object);
extern void __fastcall cleanup_729200(struct Record *object);
extern void __fastcall cleanup_72b240(struct Record *object);
extern void __fastcall cleanup_72ba10(struct Record *object);
extern void __fastcall cleanup_72fdd0(struct Record *object);
extern void __fastcall cleanup_72ff00(struct Record *object);
extern void __fastcall cleanup_7300b0(struct Record *object);
extern void __fastcall cleanup_730db0(struct Record *object);
extern void __fastcall cleanup_732550(struct Record *object);
extern void __fastcall cleanup_733b50(struct Record *object);
extern void __fastcall cleanup_736030(struct Record *object);
extern void __fastcall cleanup_7371a0(struct Record *object);
extern void __fastcall cleanup_76d800(struct Record *object);

#define RELEASE_BLOCK(Entry, Free) \
    void __fastcall Entry(void *pool, struct PoolAllocation allocation) \
    { Free(allocation.object); }

DELETE_RECORD(delete_4725b0, cleanup_4723d0, release_73c870)
DELETE_RECORD(delete_475810, cleanup_47b460, release_73c870)
DELETE_RECORD(delete_4789f0, cleanup_478540, release_73c870)
DELETE_RECORD(delete_478dc0, cleanup_478ce0, release_73c870)
DELETE_RECORD(delete_47ac50, cleanup_47abd0, release_73c870)
DELETE_RECORD(delete_5e9210, cleanup_5e9230, release_739a40)
DELETE_RECORD(delete_5eace0, cleanup_5ead00, release_739a40)
DELETE_RECORD(delete_5ec2a0, cleanup_5ec320, release_739a40)
DELETE_RECORD(delete_5ec2c0, cleanup_5ec6c0, release_739a40)
DELETE_RECORD(delete_5ec2e0, cleanup_5eca60, release_739a40)
DELETE_RECORD(delete_5ec300, cleanup_5ece00, release_739a40)
RELEASE_BLOCK(release_block_5ed2a0, release_739a40)
RELEASE_BLOCK(release_block_5ed300, release_739a40)
RELEASE_BLOCK(release_block_5ed360, release_739a40)
RELEASE_BLOCK(release_block_5ed3c0, release_739a40)
DELETE_RECORD(delete_5ee230, cleanup_5ee420, release_739a40)
DELETE_RECORD(delete_5ee6f0, cleanup_5ee8d0, release_739a40)
DELETE_RECORD(delete_5eeb80, cleanup_5eeca0, release_739a40)
DELETE_RECORD(delete_5ef080, cleanup_5ef150, release_739a40)
DELETE_RECORD(delete_5ef460, cleanup_5ef510, release_739a40)
DELETE_RECORD(delete_5ef480, cleanup_5ef4a0, release_739a40)
DELETE_RECORD(delete_5ef6d0, cleanup_5ef6f0, release_739a40)
DELETE_RECORD(delete_5ef860, cleanup_5ef880, release_739a40)
DELETE_RECORD(delete_5f18c0, cleanup_5f18e0, release_739a40)
DELETE_RECORD(delete_5f2710, cleanup_5f2730, release_739a40)
DELETE_RECORD(delete_5f2c90, cleanup_5f2cb0, release_739a40)
DELETE_RECORD(delete_5f7390, cleanup_5f72d0, release_739a40)
DELETE_RECORD(delete_5f77e0, cleanup_5f7800, release_739a40)
DELETE_RECORD(delete_5f78c0, cleanup_5f7970, release_739a40)
DELETE_RECORD(delete_5f7ae0, cleanup_5f7b00, release_739a40)
RELEASE_BLOCK(release_block_5f8370, release_739a40)
RELEASE_BLOCK(release_block_5f8410, release_739a40)
RELEASE_BLOCK(release_block_5f8420, release_739a40)
DELETE_RECORD(delete_5fcf60, cleanup_5fcf90, release_739a40)
DELETE_RECORD(delete_5fd710, cleanup_5fd730, release_739a40)
DELETE_RECORD(delete_5fd790, cleanup_5fd7b0, release_739a40)
DELETE_RECORD(delete_5fd840, cleanup_5fdc20, release_739a40)
DELETE_RECORD(delete_6018d0, cleanup_601820, release_739a40)
DELETE_RECORD(delete_601940, cleanup_601990, release_739a40)
DELETE_RECORD(delete_601b70, cleanup_601b90, release_739a40)
DELETE_RECORD(delete_601c30, cleanup_601c50, release_739a40)
RELEASE_BLOCK(release_block_601dc0, release_739a40)
RELEASE_BLOCK(release_block_601de0, release_739a40)
DELETE_RECORD(delete_601f00, cleanup_601f20, release_739a40)
RELEASE_BLOCK(release_block_602010, release_739a40)
DELETE_RECORD(delete_602280, cleanup_603a60, release_739a40)
DELETE_RECORD(delete_6022a0, cleanup_603e00, release_739a40)
RELEASE_BLOCK(release_block_604410, release_739a40)
RELEASE_BLOCK(release_block_604420, release_739a40)
RELEASE_BLOCK(release_block_6044b0, release_739a40)
DELETE_RECORD(delete_60bd30, cleanup_60bb40, release_739a40)
RELEASE_BLOCK(release_block_60f630, release_739a40)
DELETE_RECORD(delete_610080, cleanup_6100a0, release_739a40)
DELETE_RECORD(delete_610bd0, cleanup_610ab0, release_739a40)
DELETE_RECORD(delete_610ca0, cleanup_6110b0, release_739a40)
DELETE_RECORD(delete_610cf0, cleanup_6112e0, release_739a40)
RELEASE_BLOCK(release_block_611560, release_739a40)
RELEASE_BLOCK(release_block_618850, release_739a40)
RELEASE_BLOCK(release_block_618890, release_739a40)
DELETE_RECORD(delete_61d5d0, cleanup_61d5f0, release_739a40)
DELETE_RECORD(delete_61d870, cleanup_604690, release_739a40)
DELETE_RECORD(delete_61e670, cleanup_61e6d0, release_739a40)
RELEASE_BLOCK(release_block_620820, release_739a40)
RELEASE_BLOCK(release_block_620850, release_739a40)
DELETE_RECORD(delete_621680, cleanup_6216a0, release_739a40)
DELETE_RECORD(delete_621ee0, cleanup_622010, release_739a40)
DELETE_RECORD(delete_622600, cleanup_622620, release_739a40)
RELEASE_BLOCK(release_block_6235b0, release_739a40)
RELEASE_BLOCK(release_block_623630, release_739a40)
DELETE_RECORD(delete_626da0, cleanup_626df0, release_739a40)
DELETE_RECORD(delete_626dd0, cleanup_627b40, release_739a40)
DELETE_RECORD(delete_628340, cleanup_628360, release_739a40)
RELEASE_BLOCK(release_block_628770, release_739a40)
DELETE_RECORD(delete_628bb0, cleanup_628ef0, release_739a40)
RELEASE_BLOCK(release_block_62c220, release_739a40)
DELETE_RECORD(delete_62dab0, cleanup_62db10, release_739a40)
DELETE_RECORD(delete_62e0d0, cleanup_62e0f0, release_739a40)
DELETE_RECORD(delete_62e4b0, cleanup_62e730, release_739a40)
RELEASE_BLOCK(release_block_62eb10, release_739a40)
DELETE_RECORD(delete_71f480, cleanup_71f760, release_739a40)
DELETE_RECORD(delete_71f4d0, cleanup_722650, release_739a40)
DELETE_RECORD(delete_721320, cleanup_721400, release_739a40)
DELETE_RECORD(delete_721350, cleanup_722860, release_739a40)
DELETE_RECORD(delete_722b90, cleanup_722bb0, release_739a40)
DELETE_RECORD(delete_722c70, cleanup_722c90, release_739a40)
DELETE_RECORD(delete_722d50, cleanup_722d70, release_739a40)
RELEASE_BLOCK(release_block_722ef0, release_739a40)
RELEASE_BLOCK(release_block_722f60, release_739a40)
RELEASE_BLOCK(release_block_722fb0, release_739a40)
RELEASE_BLOCK(release_block_727b40, release_739a40)
DELETE_RECORD(delete_727ca0, cleanup_727cc0, release_739a40)
DELETE_RECORD(delete_728040, cleanup_7280f0, release_739a40)
DELETE_RECORD(delete_7283f0, cleanup_728410, release_739a40)
DELETE_RECORD(delete_728950, cleanup_728970, release_739a40)
DELETE_RECORD(delete_728ad0, cleanup_728af0, release_739a40)
DELETE_RECORD(delete_728b50, cleanup_728b70, release_739a40)
DELETE_RECORD(delete_728b80, cleanup_728ba0, release_739a40)
RELEASE_BLOCK(release_block_728c60, release_739a40)
DELETE_RECORD(delete_728f60, cleanup_729200, release_739a40)
DELETE_RECORD(delete_72b160, cleanup_72b240, release_739a40)
DELETE_RECORD(delete_72b9f0, cleanup_72ba10, release_739a40)
DELETE_RECORD(delete_72c2e0, cleanup_71f580, release_739a40)
DELETE_RECORD(delete_72c770, cleanup_71f4f0, release_739a40)
DELETE_RECORD(delete_72fdb0, cleanup_72fdd0, release_739a40)
DELETE_RECORD(delete_72fee0, cleanup_72ff00, release_739a40)
DELETE_RECORD(delete_730090, cleanup_7300b0, release_739a40)
DELETE_RECORD(delete_730d90, cleanup_730db0, release_739a40)
DELETE_RECORD(delete_732440, cleanup_732550, release_739a40)
DELETE_RECORD(delete_733b30, cleanup_733b50, release_739a40)
DELETE_RECORD(delete_736010, cleanup_736030, release_739a40)
RELEASE_BLOCK(release_block_736410, release_739a40)
DELETE_RECORD(delete_736be0, cleanup_7371a0, release_739a40)
RELEASE_BLOCK(release_block_738e10, release_739a40)
DELETE_RECORD(delete_76d920, cleanup_76d800, release_73c870)
