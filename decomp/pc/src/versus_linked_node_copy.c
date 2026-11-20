#include "linked_node_copy.h"

PAIR_LAYOUT(u32u16, unsigned, unsigned short);
struct Ref_u32u16 { const struct u32u16 *value; };
struct Block42 { unsigned short data[21]; };
PAIR_LAYOUT(u16block42, unsigned short, struct Block42);
struct Ref_u16block42 { const struct u16block42 *value; };
PAIR_LAYOUT(u32u32, unsigned, unsigned);
struct Ref_u32u32 { const struct u32u32 *value; };
PAIR_LAYOUT(u8u32, unsigned char, unsigned);
struct Ref_u8u32 { const struct u8u32 *value; };

MAKE_NODE(make_node_23ba70, u32u16, COPY_PAIR)
MAKE_NODE(make_node_253390, u16block42, COPY_PAIR)
MAKE_NODE(make_node_2646e0, u32u32, COPY_PAIR)
MAKE_NODE(make_node_26ba50, u8u32, COPY_PAIR)
