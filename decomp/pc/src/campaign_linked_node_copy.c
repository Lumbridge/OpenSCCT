#include "linked_node_copy.h"

PAIR_LAYOUT(u32u32, unsigned, unsigned);
struct Ref_u32u32 { const struct u32u32 *value; };
struct Block42 { unsigned short data[21]; };
PAIR_LAYOUT(u16block42, unsigned short, struct Block42);
struct Ref_u16block42 { const struct u16block42 *value; };
PAIR_LAYOUT(u32u16, unsigned, unsigned short);
struct Ref_u32u16 { const struct u32u16 *value; };
PAIR_LAYOUT(u8u32, unsigned char, unsigned);
struct Ref_u8u32 { const struct u8u32 *value; };
SINGLE_LAYOUT(u32, unsigned);
struct Ref_u32 { const struct u32 *value; };
PAIR_LAYOUT(u16u32, unsigned short, unsigned);
struct Ref_u16u32 { const struct u16u32 *value; };

MAKE_NODE(make_node_5ee0c0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_5ee0f0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_5ee120, u32u32, COPY_PAIR)
MAKE_NODE(make_node_5ee150, u32u32, COPY_PAIR)
MAKE_NODE(make_node_5fabd0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_619050, u16block42, COPY_PAIR)
MAKE_NODE(make_node_61c390, u32u32, COPY_PAIR)
MAKE_NODE(make_node_61c3c0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_61c3f0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_625b90, u32u32, COPY_PAIR)
MAKE_NODE(make_node_628990, u32u32, COPY_PAIR)
MAKE_NODE(make_node_62d600, u32u16, COPY_PAIR)
MAKE_NODE(make_node_62d630, u32u32, COPY_PAIR)
MAKE_NODE(make_node_62d660, u32u32, COPY_PAIR)
MAKE_NODE(make_node_62f3d0, u8u32, COPY_PAIR)
MAKE_NODE(make_node_723da0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_727f10, u32, COPY_KEY)
MAKE_NODE(make_node_735eb0, u16u32, COPY_PAIR)
