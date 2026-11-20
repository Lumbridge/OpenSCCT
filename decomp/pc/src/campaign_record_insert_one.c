union StackWord { unsigned value; float stack; };
struct Record4 { unsigned char bytes[4]; };
struct Vector4 { void *allocator; struct Record4 *begin, *end, *limit; };
struct Position4 { struct Record4 *ptr; };
struct Output4 { struct Position4 *ptr; };
struct Item4 { const struct Record4 *ptr; };

struct Record8 { unsigned char bytes[8]; };
struct Vector8 { void *allocator; struct Record8 *begin, *end, *limit; };
struct Position8 { struct Record8 *ptr; };
struct Output8 { struct Position8 *ptr; };
struct Item8 { const struct Record8 *ptr; };

struct Record12 { unsigned char bytes[12]; };
struct Vector12 { void *allocator; struct Record12 *begin, *end, *limit; };
struct Position12 { struct Record12 *ptr; };
struct Output12 { struct Position12 *ptr; };
struct Item12 { const struct Record12 *ptr; };

struct Record16 { unsigned char bytes[16]; };
struct Vector16 { void *allocator; struct Record16 *begin, *end, *limit; };
struct Position16 { struct Record16 *ptr; };
struct Output16 { struct Position16 *ptr; };
struct Item16 { const struct Record16 *ptr; };

struct Record32 { unsigned char bytes[32]; };
struct Vector32 { void *allocator; struct Record32 *begin, *end, *limit; };
struct Position32 { struct Record32 *ptr; };
struct Output32 { struct Position32 *ptr; };
struct Item32 { const struct Record32 *ptr; };

struct Record60 { unsigned char bytes[60]; };
struct Vector60 { void *allocator; struct Record60 *begin, *end, *limit; };
struct Position60 { struct Record60 *ptr; };
struct Output60 { struct Position60 *ptr; };
struct Item60 { const struct Record60 *ptr; };

extern void __fastcall copies_10f930_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_21da30_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_21dca0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_21df10_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_21e180_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_21e3f0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_38a4f0_12(struct Vector12 *, struct Position12, float, struct Item12);
extern void __fastcall copies_422170_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_423db0_8(struct Vector8 *, struct Position8, float, struct Item8);
extern void __fastcall copies_42aa00_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_42abe0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_42e150_60(struct Vector60 *, struct Position60, float, struct Item60);
extern void __fastcall copies_430d70_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_4324d0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_433bd0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_436fc0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_437230_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_444680_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_444890_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_444aa0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_444cb0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_444ec0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4450d0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4452e0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4454f0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_445700_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_445910_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_445b20_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_445d30_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_445f40_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446150_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446360_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446570_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446780_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446990_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446ba0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446db0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_446fc0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4471d0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4473e0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4475f0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_447800_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_447a10_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_447c20_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_447e30_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448040_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448250_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448460_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448670_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448880_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_448a90_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_4498a0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_449ab0_16(struct Vector16 *, struct Position16, float, struct Item16);
extern void __fastcall copies_45a8d0_8(struct Vector8 *, struct Position8, float, struct Item8);
extern void __fastcall copies_45b1f0_32(struct Vector32 *, struct Position32, float, struct Item32);
extern void __fastcall copies_460cf0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_4632b0_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_464d30_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_495860_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_49c850_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_4bef70_4(struct Vector4 *, struct Position4, float, struct Item4);
extern void __fastcall copies_73b470_4(struct Vector4 *, struct Position4, float, struct Item4);

#define INSERT_ONE(Entry, Width, Insert) \
    struct Position##Width *__fastcall Entry(struct Vector##Width *vector, struct Output##Width output, struct Position##Width where, struct Item##Width item) \
    { \
        static const union StackWord one = {1}; \
        unsigned size = !vector->begin ? 0 : (unsigned)(vector->end - vector->begin); \
        int index = size == 0 ? 0 : where.ptr - vector->begin; \
        Insert(vector, where, one.stack, item); \
        output.ptr->ptr = vector->begin + index; \
        return output.ptr; \
    }

INSERT_ONE(insert_10fb80, 4, copies_10f930_4)
INSERT_ONE(insert_21e660, 16, copies_21da30_16)
INSERT_ONE(insert_21e6b0, 16, copies_21dca0_16)
INSERT_ONE(insert_21e700, 16, copies_21df10_16)
INSERT_ONE(insert_21e750, 16, copies_21e180_16)
INSERT_ONE(insert_21e7a0, 16, copies_21e3f0_16)
INSERT_ONE(insert_38a8f0, 12, copies_38a4f0_12)
INSERT_ONE(insert_4223d0, 4, copies_422170_4)
INSERT_ONE(insert_424160, 8, copies_423db0_8)
INSERT_ONE(insert_42b2a0, 4, copies_42aa00_4)
INSERT_ONE(insert_42b310, 4, copies_42abe0_4)
INSERT_ONE(insert_42f210, 60, copies_42e150_60)
INSERT_ONE(insert_431070, 4, copies_430d70_4)
INSERT_ONE(insert_4327d0, 4, copies_4324d0_4)
INSERT_ONE(insert_433ed0, 4, copies_433bd0_4)
INSERT_ONE(insert_4375d0, 4, copies_436fc0_4)
INSERT_ONE(insert_4376a0, 4, copies_437230_4)
INSERT_ONE(insert_448e00, 16, copies_444680_16)
INSERT_ONE(insert_448e50, 16, copies_444890_16)
INSERT_ONE(insert_448ea0, 16, copies_444aa0_16)
INSERT_ONE(insert_448ef0, 16, copies_444cb0_16)
INSERT_ONE(insert_448f40, 16, copies_444ec0_16)
INSERT_ONE(insert_448f90, 16, copies_4450d0_16)
INSERT_ONE(insert_448fe0, 16, copies_4452e0_16)
INSERT_ONE(insert_449030, 16, copies_4454f0_16)
INSERT_ONE(insert_449080, 16, copies_445700_16)
INSERT_ONE(insert_4490d0, 16, copies_445910_16)
INSERT_ONE(insert_449120, 16, copies_445b20_16)
INSERT_ONE(insert_449170, 16, copies_445d30_16)
INSERT_ONE(insert_4491c0, 16, copies_445f40_16)
INSERT_ONE(insert_449210, 16, copies_446150_16)
INSERT_ONE(insert_449260, 16, copies_446360_16)
INSERT_ONE(insert_4492b0, 16, copies_446570_16)
INSERT_ONE(insert_449300, 16, copies_446780_16)
INSERT_ONE(insert_449350, 16, copies_446990_16)
INSERT_ONE(insert_4493a0, 16, copies_446ba0_16)
INSERT_ONE(insert_4493f0, 16, copies_446db0_16)
INSERT_ONE(insert_449440, 16, copies_446fc0_16)
INSERT_ONE(insert_449490, 16, copies_4471d0_16)
INSERT_ONE(insert_4494e0, 16, copies_4473e0_16)
INSERT_ONE(insert_449530, 16, copies_4475f0_16)
INSERT_ONE(insert_449580, 16, copies_447800_16)
INSERT_ONE(insert_4495d0, 16, copies_447a10_16)
INSERT_ONE(insert_449620, 16, copies_447c20_16)
INSERT_ONE(insert_449670, 16, copies_447e30_16)
INSERT_ONE(insert_4496c0, 16, copies_448040_16)
INSERT_ONE(insert_449710, 16, copies_448250_16)
INSERT_ONE(insert_449760, 16, copies_448460_16)
INSERT_ONE(insert_4497b0, 16, copies_448670_16)
INSERT_ONE(insert_449800, 16, copies_448880_16)
INSERT_ONE(insert_449850, 16, copies_448a90_16)
INSERT_ONE(insert_44afc0, 16, copies_4498a0_16)
INSERT_ONE(insert_44b010, 16, copies_449ab0_16)
INSERT_ONE(insert_45ab20, 8, copies_45a8d0_8)
INSERT_ONE(insert_45b470, 32, copies_45b1f0_32)
INSERT_ONE(insert_460f30, 4, copies_460cf0_4)
INSERT_ONE(insert_463570, 4, copies_4632b0_4)
INSERT_ONE(insert_464f70, 4, copies_464d30_4)
INSERT_ONE(insert_495c70, 4, copies_495860_4)
INSERT_ONE(insert_49cb60, 4, copies_49c850_4)
INSERT_ONE(insert_4bf1f0, 4, copies_4bef70_4)
INSERT_ONE(insert_73b420, 4, copies_73b470_4)
