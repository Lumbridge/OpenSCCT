#include "tree_iteration.h"

TREE_LAYOUT(Node12, 12);
TREE_LAYOUT(Node20, 20);
TREE_LAYOUT(Node32, 32);
TREE_LAYOUT(Node36, 36);
TREE_LAYOUT(Node4, 4);
TREE_LAYOUT(Node40, 40);
TREE_LAYOUT(Node8, 8);
extern void __fastcall tree_next_380710(struct Ref_Node4 *it);
extern void __fastcall tree_next_421560(struct Ref_Node32 *it);
extern void __fastcall tree_next_450020(struct Ref_Node40 *it);
extern void __fastcall tree_next_4846c0(struct Ref_Node8 *it);
extern void __fastcall tree_next_488f90(struct Ref_Node8 *it);
extern void __fastcall tree_next_48bc70(struct Ref_Node12 *it);
extern void __fastcall tree_next_496740(struct Ref_Node8 *it);
extern void __fastcall tree_next_49f3b0(struct Ref_Node8 *it);
extern void __fastcall tree_next_4a3450(struct Ref_Node8 *it);
extern void __fastcall tree_next_4a5890(struct Ref_Node8 *it);
extern void __fastcall tree_next_4abb80(struct Ref_Node20 *it);
extern void __fastcall tree_next_4ae900(struct Ref_Node8 *it);
extern void __fastcall tree_next_4aeb00(struct Ref_Node20 *it);
extern void __fastcall tree_next_4bfd80(struct Ref_Node8 *it);
extern void __fastcall tree_next_4c53a0(struct Ref_Node8 *it);
extern void __fastcall tree_next_4c9110(struct Ref_Node8 *it);
extern void __fastcall tree_next_4c9170(struct Ref_Node8 *it);
extern void __fastcall tree_next_4d3c50(struct Ref_Node36 *it);
extern void __fastcall tree_next_4d73e0(struct Ref_Node8 *it);
extern void __fastcall tree_next_4ddc40(struct Ref_Node20 *it);
extern void __fastcall tree_next_4e8250(struct Ref_Node8 *it);
extern void __fastcall tree_next_4ec0d0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_383690(struct Ref_Node4 *it);
extern void __fastcall tree_prev_421500(struct Ref_Node32 *it);
extern void __fastcall tree_prev_454ff0(struct Ref_Node40 *it);
extern void __fastcall tree_prev_4890a0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_48bde0(struct Ref_Node12 *it);
extern void __fastcall tree_prev_496860(struct Ref_Node8 *it);
extern void __fastcall tree_prev_499210(struct Ref_Node8 *it);
extern void __fastcall tree_prev_49f4d0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4a3120(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4a59b0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4abc60(struct Ref_Node20 *it);
extern void __fastcall tree_prev_4aeaa0(struct Ref_Node20 *it);
extern void __fastcall tree_prev_4aeb60(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4bfea0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4c5500(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4c92e0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4c9340(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4d3d50(struct Ref_Node36 *it);
extern void __fastcall tree_prev_4d7500(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4ddd30(struct Ref_Node20 *it);
extern void __fastcall tree_prev_4ec1b0(struct Ref_Node8 *it);
extern void __fastcall tree_prev_4ed8d0(struct Ref_Node8 *it);

#define PREFIX_STEP(Entry, N, Step) \
    struct Ref_##N *__fastcall Entry(struct Ref_##N *it) \
    { \
        Step(it); \
        return it; \
    }

PREFIX_STEP(tree_next_380710_prefix_383710, Node4, tree_next_380710)
PREFIX_STEP(tree_next_380710_prefix_3858f0, Node4, tree_next_380710)
PREFIX_STEP(tree_prev_383690_prefix_385870, Node4, tree_prev_383690)
PREFIX_STEP(tree_prev_383690_prefix_387ab0, Node4, tree_prev_383690)
PREFIX_STEP(tree_prev_421500_prefix_421a10, Node32, tree_prev_421500)
PREFIX_STEP(tree_prev_421500_prefix_421ce0, Node32, tree_prev_421500)
PREFIX_STEP(tree_next_421560_prefix_421a00, Node32, tree_next_421560)
PREFIX_STEP(tree_next_421560_prefix_421cd0, Node32, tree_next_421560)
PREFIX_STEP(tree_next_450020_prefix_450140, Node40, tree_next_450020)
PREFIX_STEP(tree_next_450020_prefix_450240, Node40, tree_next_450020)
PREFIX_STEP(tree_prev_454ff0_prefix_455330, Node40, tree_prev_454ff0)
PREFIX_STEP(tree_prev_454ff0_prefix_455520, Node40, tree_prev_454ff0)
PREFIX_STEP(tree_next_4846c0_prefix_484980, Node8, tree_next_4846c0)
PREFIX_STEP(tree_next_4846c0_prefix_4995d0, Node8, tree_next_4846c0)
PREFIX_STEP(tree_next_488f90_prefix_4893e0, Node8, tree_next_488f90)
PREFIX_STEP(tree_next_488f90_prefix_4897a0, Node8, tree_next_488f90)
PREFIX_STEP(tree_prev_4890a0_prefix_489580, Node8, tree_prev_4890a0)
PREFIX_STEP(tree_prev_4890a0_prefix_489800, Node8, tree_prev_4890a0)
PREFIX_STEP(tree_next_48bc70_prefix_48c3f0, Node12, tree_next_48bc70)
PREFIX_STEP(tree_next_48bc70_prefix_48c990, Node12, tree_next_48bc70)
PREFIX_STEP(tree_prev_48bde0_prefix_48c7b0, Node12, tree_prev_48bde0)
PREFIX_STEP(tree_prev_48bde0_prefix_48c9f0, Node12, tree_prev_48bde0)
PREFIX_STEP(tree_next_496740_prefix_496a00, Node8, tree_next_496740)
PREFIX_STEP(tree_next_496740_prefix_496c40, Node8, tree_next_496740)
PREFIX_STEP(tree_prev_496860_prefix_496bb0, Node8, tree_prev_496860)
PREFIX_STEP(tree_prev_496860_prefix_496c80, Node8, tree_prev_496860)
PREFIX_STEP(tree_prev_499210_prefix_499400, Node8, tree_prev_499210)
PREFIX_STEP(tree_prev_499210_prefix_4995e0, Node8, tree_prev_499210)
PREFIX_STEP(tree_next_49f3b0_prefix_49f670, Node8, tree_next_49f3b0)
PREFIX_STEP(tree_next_49f3b0_prefix_49fae0, Node8, tree_next_49f3b0)
PREFIX_STEP(tree_prev_49f4d0_prefix_49f830, Node8, tree_prev_49f4d0)
PREFIX_STEP(tree_prev_49f4d0_prefix_49fb20, Node8, tree_prev_49f4d0)
PREFIX_STEP(tree_prev_4a3120_prefix_4a3440, Node8, tree_prev_4a3120)
PREFIX_STEP(tree_prev_4a3120_prefix_4a35a0, Node8, tree_prev_4a3120)
PREFIX_STEP(tree_next_4a3450_prefix_4a35b0, Node8, tree_next_4a3450)
PREFIX_STEP(tree_next_4a3450_prefix_4a3720, Node8, tree_next_4a3450)
PREFIX_STEP(tree_next_4a5890_prefix_4a5ba0, Node8, tree_next_4a5890)
PREFIX_STEP(tree_next_4a5890_prefix_4a5e20, Node8, tree_next_4a5890)
PREFIX_STEP(tree_prev_4a59b0_prefix_4a5d80, Node8, tree_prev_4a59b0)
PREFIX_STEP(tree_prev_4a59b0_prefix_4a5ea0, Node8, tree_prev_4a59b0)
PREFIX_STEP(tree_next_4abb80_prefix_4abed0, Node20, tree_next_4abb80)
PREFIX_STEP(tree_next_4abb80_prefix_4ac100, Node20, tree_next_4abb80)
PREFIX_STEP(tree_prev_4abc60_prefix_4ac050, Node20, tree_prev_4abc60)
PREFIX_STEP(tree_prev_4abc60_prefix_4ac140, Node20, tree_prev_4abc60)
PREFIX_STEP(tree_next_4ae900_prefix_4aee40, Node8, tree_next_4ae900)
PREFIX_STEP(tree_next_4ae900_prefix_4af2e0, Node8, tree_next_4ae900)
PREFIX_STEP(tree_prev_4aeaa0_prefix_4af060, Node20, tree_prev_4aeaa0)
PREFIX_STEP(tree_prev_4aeaa0_prefix_4af360, Node20, tree_prev_4aeaa0)
PREFIX_STEP(tree_next_4aeb00_prefix_4af050, Node20, tree_next_4aeb00)
PREFIX_STEP(tree_next_4aeb00_prefix_4af350, Node20, tree_next_4aeb00)
PREFIX_STEP(tree_prev_4aeb60_prefix_4af070, Node8, tree_prev_4aeb60)
PREFIX_STEP(tree_prev_4aeb60_prefix_4af370, Node8, tree_prev_4aeb60)
PREFIX_STEP(tree_next_4bfd80_prefix_4c0180, Node8, tree_next_4bfd80)
PREFIX_STEP(tree_next_4bfd80_prefix_4c0400, Node8, tree_next_4bfd80)
PREFIX_STEP(tree_prev_4bfea0_prefix_4c0330, Node8, tree_prev_4bfea0)
PREFIX_STEP(tree_prev_4bfea0_prefix_4c0440, Node8, tree_prev_4bfea0)
PREFIX_STEP(tree_next_4c53a0_prefix_4c57b0, Node8, tree_next_4c53a0)
PREFIX_STEP(tree_next_4c53a0_prefix_4c5b40, Node8, tree_next_4c53a0)
PREFIX_STEP(tree_prev_4c5500_prefix_4c5a40, Node8, tree_prev_4c5500)
PREFIX_STEP(tree_prev_4c5500_prefix_4c5bb0, Node8, tree_prev_4c5500)
PREFIX_STEP(tree_next_4c9110_prefix_4c95a0, Node8, tree_next_4c9110)
PREFIX_STEP(tree_next_4c9110_prefix_4c9b50, Node8, tree_next_4c9110)
PREFIX_STEP(tree_next_4c9170_prefix_4c95b0, Node8, tree_next_4c9170)
PREFIX_STEP(tree_next_4c9170_prefix_4c9b60, Node8, tree_next_4c9170)
PREFIX_STEP(tree_prev_4c92e0_prefix_4c9a00, Node8, tree_prev_4c92e0)
PREFIX_STEP(tree_prev_4c92e0_prefix_4c9c80, Node8, tree_prev_4c92e0)
PREFIX_STEP(tree_prev_4c9340_prefix_4c9a10, Node8, tree_prev_4c9340)
PREFIX_STEP(tree_prev_4c9340_prefix_4c9c90, Node8, tree_prev_4c9340)
PREFIX_STEP(tree_next_4d3c50_prefix_4d3f50, Node36, tree_next_4d3c50)
PREFIX_STEP(tree_next_4d3c50_prefix_4d4210, Node36, tree_next_4d3c50)
PREFIX_STEP(tree_prev_4d3d50_prefix_4d40f0, Node36, tree_prev_4d3d50)
PREFIX_STEP(tree_prev_4d3d50_prefix_4d4250, Node36, tree_prev_4d3d50)
PREFIX_STEP(tree_next_4d73e0_prefix_4d76e0, Node8, tree_next_4d73e0)
PREFIX_STEP(tree_next_4d73e0_prefix_4d7930, Node8, tree_next_4d73e0)
PREFIX_STEP(tree_prev_4d7500_prefix_4d7890, Node8, tree_prev_4d7500)
PREFIX_STEP(tree_prev_4d7500_prefix_4d7970, Node8, tree_prev_4d7500)
PREFIX_STEP(tree_next_4ddc40_prefix_4de0a0, Node20, tree_next_4ddc40)
PREFIX_STEP(tree_next_4ddc40_prefix_4de290, Node20, tree_next_4ddc40)
PREFIX_STEP(tree_prev_4ddd30_prefix_4de190, Node20, tree_prev_4ddd30)
PREFIX_STEP(tree_prev_4ddd30_prefix_4de2e0, Node20, tree_prev_4ddd30)
PREFIX_STEP(tree_next_4e8250_prefix_4e89a0, Node8, tree_next_4e8250)
PREFIX_STEP(tree_next_4e8250_prefix_4e8e80, Node8, tree_next_4e8250)
PREFIX_STEP(tree_next_4ec0d0_prefix_4ec350, Node8, tree_next_4ec0d0)
PREFIX_STEP(tree_next_4ec0d0_prefix_4ec520, Node8, tree_next_4ec0d0)
PREFIX_STEP(tree_prev_4ec1b0_prefix_4ec430, Node8, tree_prev_4ec1b0)
PREFIX_STEP(tree_prev_4ec1b0_prefix_4ec5b0, Node8, tree_prev_4ec1b0)
PREFIX_STEP(tree_prev_4ed8d0_prefix_4edb00, Node8, tree_prev_4ed8d0)
PREFIX_STEP(tree_prev_4ed8d0_prefix_4edbd0, Node8, tree_prev_4ed8d0)
