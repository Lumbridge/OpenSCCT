#include "campaign_fixed_record_growth.h"

#define APPEND_RECORD(Entry, Record, Tag) \
    int __fastcall append_record_##Entry(struct Array *array, struct ref_##Record value) \
    { \
        int index = extend_records(array, 1, sizeof(struct Record), Tag); \
        ((struct Record *)array->buffer.bytes)[index] = *value.record; \
        return index; \
    }

/* The x86 placement allocation entry takes object_size and the array on
   the stack. The instantiation supplies the record width. */
#define PLACE_RECORD(Entry, Record, Tag) \
    void *__cdecl place_record_##Entry(unsigned int object_size, struct Array *array) \
    { \
        int index = extend_records(array, 1, sizeof(struct Record), Tag); \
        return ((struct Record *)array->buffer.bytes) + index; \
    }

#define ADD_RECORDS(Entry, Record, Tag) \
    int __fastcall add_records_##Entry(struct Array *array, struct ArraySize amount) \
    { return extend_records(array, amount.value, sizeof(struct Record), Tag); }

struct record_1 { unsigned char bytes[1]; };
struct ref_record_1 { const struct record_1 *record; };
struct record_2 { unsigned char bytes[2]; };
struct ref_record_2 { const struct record_2 *record; };
struct record_4 { unsigned int words[1]; };
struct ref_record_4 { const struct record_4 *record; };
struct record_8 { unsigned int words[2]; };
struct ref_record_8 { const struct record_8 *record; };
struct record_12 { unsigned int words[3]; };
struct ref_record_12 { const struct record_12 *record; };
struct record_16 { unsigned int words[4]; };
struct ref_record_16 { const struct record_16 *record; };
struct record_20 { unsigned int words[5]; };
struct ref_record_20 { const struct record_20 *record; };
struct record_24 { unsigned int words[6]; };
struct ref_record_24 { const struct record_24 *record; };
struct record_28 { unsigned int words[7]; };
struct ref_record_28 { const struct record_28 *record; };
struct record_32 { unsigned int words[8]; };
struct ref_record_32 { const struct record_32 *record; };
struct record_36 { unsigned int words[9]; };
struct ref_record_36 { const struct record_36 *record; };
struct record_40 { unsigned int words[10]; };
struct ref_record_40 { const struct record_40 *record; };
struct record_44 { unsigned int words[11]; };
struct ref_record_44 { const struct record_44 *record; };
struct record_48 { unsigned int words[12]; };
struct ref_record_48 { const struct record_48 *record; };
struct record_56 { unsigned int words[14]; };
struct ref_record_56 { const struct record_56 *record; };
struct record_60 { unsigned int words[15]; };
struct ref_record_60 { const struct record_60 *record; };
struct record_64 { unsigned int words[16]; };
struct ref_record_64 { const struct record_64 *record; };
struct record_68 { unsigned int words[17]; };
struct ref_record_68 { const struct record_68 *record; };
struct record_72 { unsigned int words[18]; };
struct ref_record_72 { const struct record_72 *record; };
struct record_76 { unsigned int words[19]; };
struct ref_record_76 { const struct record_76 *record; };
struct record_80 { unsigned int words[20]; };
struct ref_record_80 { const struct record_80 *record; };
struct record_100 { unsigned int words[25]; };
struct ref_record_100 { const struct record_100 *record; };
struct record_120 { unsigned int words[30]; };
struct ref_record_120 { const struct record_120 *record; };
struct record_124 { unsigned int words[31]; };
struct ref_record_124 { const struct record_124 *record; };
struct record_128 { unsigned int words[32]; };
struct ref_record_128 { const struct record_128 *record; };
struct record_132 { unsigned int words[33]; };
struct ref_record_132 { const struct record_132 *record; };
struct record_136 { unsigned int words[34]; };
struct ref_record_136 { const struct record_136 *record; };
struct record_148 { unsigned int words[37]; };
struct ref_record_148 { const struct record_148 *record; };
struct record_156 { unsigned int words[39]; };
struct ref_record_156 { const struct record_156 *record; };
struct record_192 { unsigned int words[48]; };
struct ref_record_192 { const struct record_192 *record; };
struct record_376 { unsigned int words[94]; };
struct ref_record_376 { const struct record_376 *record; };
struct record_1100 { unsigned int words[275]; };
struct ref_record_1100 { const struct record_1100 *record; };

extern const unsigned short record_tag_7b78d8[];
extern const unsigned short record_tag_7b7b08[];
extern const unsigned short record_tag_7ba17c[];
extern const unsigned short record_tag_7bc158[];
extern const unsigned short record_tag_7bc180[];
extern const unsigned short record_tag_7be9a8[];
extern const unsigned short record_tag_7cf82c[];
extern const unsigned short record_tag_7db4cc[];
extern const unsigned short record_tag_831360[];

ADD_RECORDS(r009590, record_2, record_tag_7b78d8)
ADD_RECORDS(r029710, record_12, record_tag_7bc180)
APPEND_RECORD(r029980, record_12, record_tag_7bc180)
ADD_RECORDS(r035510, record_4, record_tag_7bc158)
ADD_RECORDS(r03a0d0, record_4, record_tag_7b7b08)
APPEND_RECORD(r03ab60, record_80, record_tag_7b7b08)
APPEND_RECORD(r03ac10, record_12, record_tag_7b7b08)
PLACE_RECORD(r0512e0, record_4, record_tag_7b7b08)
APPEND_RECORD(r051b00, record_20, record_tag_7b7b08)
APPEND_RECORD(r051c30, record_20, record_tag_7b7b08)
PLACE_RECORD(r066d00, record_12, record_tag_7b7b08)
ADD_RECORDS(r067100, record_1, record_tag_7be9a8)
APPEND_RECORD(r067280, record_1, record_tag_7be9a8)
PLACE_RECORD(r0672d0, record_4, record_tag_7b7b08)
PLACE_RECORD(r067320, record_40, record_tag_7b7b08)
PLACE_RECORD(r073960, record_28, record_tag_7b7b08)
PLACE_RECORD(r0739b0, record_16, record_tag_7b7b08)
PLACE_RECORD(r07d4f0, record_16, record_tag_7b7b08)
PLACE_RECORD(r07d660, record_16, record_tag_7b7b08)
PLACE_RECORD(r07d6b0, record_8, record_tag_7b7b08)
PLACE_RECORD(r080540, record_28, record_tag_7b7b08)
PLACE_RECORD(r080e60, record_16, record_tag_7b7b08)
PLACE_RECORD(r080eb0, record_192, record_tag_7b7b08)
PLACE_RECORD(r080f10, record_24, record_tag_7b7b08)
PLACE_RECORD(r080f60, record_28, record_tag_7b7b08)
PLACE_RECORD(r080fb0, record_20, record_tag_7b7b08)
PLACE_RECORD(r081000, record_36, record_tag_7b7b08)
PLACE_RECORD(r081050, record_44, record_tag_7b7b08)
PLACE_RECORD(r0810a0, record_128, record_tag_7b7b08)
PLACE_RECORD(r0810f0, record_8, record_tag_7b7b08)
PLACE_RECORD(r081140, record_124, record_tag_7b7b08)
PLACE_RECORD(r08b380, record_20, record_tag_7b7b08)
PLACE_RECORD(r08bc40, record_64, record_tag_7b7b08)
APPEND_RECORD(r092590, record_8, record_tag_7b7b08)
APPEND_RECORD(r0abb00, record_2, record_tag_7b78d8)
PLACE_RECORD(r0ac270, record_4, record_tag_7b7b08)
PLACE_RECORD(r0b86d0, record_132, record_tag_7b7b08)
PLACE_RECORD(r0beed0, record_28, record_tag_7b7b08)
PLACE_RECORD(r0bef20, record_40, record_tag_7b7b08)
PLACE_RECORD(r0bf640, record_28, record_tag_7b7b08)
APPEND_RECORD(r0c0920, record_28, record_tag_7b7b08)
APPEND_RECORD(r0c0e60, record_28, record_tag_7b7b08)
PLACE_RECORD(r0d87b0, record_24, record_tag_7b7b08)
PLACE_RECORD(r0d8800, record_24, record_tag_7cf82c)
PLACE_RECORD(r0d8850, record_48, record_tag_7b7b08)
PLACE_RECORD(r0da3b0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0da400, record_16, record_tag_7b7b08)
PLACE_RECORD(r0da450, record_36, record_tag_7b7b08)
PLACE_RECORD(r0da4a0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0da4f0, record_136, record_tag_7b7b08)
PLACE_RECORD(r0da550, record_8, record_tag_7b7b08)
PLACE_RECORD(r0da5a0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0da5f0, record_24, record_tag_7b7b08)
PLACE_RECORD(r0da640, record_8, record_tag_7b7b08)
PLACE_RECORD(r0dead0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0deb20, record_20, record_tag_7b7b08)
PLACE_RECORD(r0deb70, record_120, record_tag_7b7b08)
PLACE_RECORD(r0debc0, record_136, record_tag_7b7b08)
PLACE_RECORD(r0dec20, record_8, record_tag_7b7b08)
PLACE_RECORD(r0dec70, record_4, record_tag_7b7b08)
PLACE_RECORD(r0ded70, record_32, record_tag_7b7b08)
PLACE_RECORD(r0dedc0, record_8, record_tag_7b7b08)
PLACE_RECORD(r0dee10, record_4, record_tag_7b7b08)
PLACE_RECORD(r0dee60, record_16, record_tag_7b7b08)
PLACE_RECORD(r0deeb0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0def00, record_72, record_tag_7b7b08)
PLACE_RECORD(r0def50, record_64, record_tag_7b7b08)
PLACE_RECORD(r0defa0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0deff0, record_4, record_tag_7b7b08)
PLACE_RECORD(r0df040, record_16, record_tag_7b7b08)
PLACE_RECORD(r0df090, record_28, record_tag_7b7b08)
PLACE_RECORD(r0df0e0, record_8, record_tag_7b7b08)
PLACE_RECORD(r0df130, record_76, record_tag_7b7b08)
PLACE_RECORD(r0df180, record_12, record_tag_7b7b08)
PLACE_RECORD(r0df1d0, record_16, record_tag_7b7b08)
PLACE_RECORD(r0df220, record_12, record_tag_7b7b08)
PLACE_RECORD(r0df270, record_60, record_tag_7b7b08)
PLACE_RECORD(r0df2c0, record_72, record_tag_7b7b08)
PLACE_RECORD(r0e0ce0, record_12, record_tag_7b7b08)
APPEND_RECORD(r0e2700, record_36, record_tag_7b7b08)
ADD_RECORDS(r0e2ac0, record_16, record_tag_7b7b08)
ADD_RECORDS(r0e2c10, record_48, record_tag_7b7b08)
APPEND_RECORD(r0e2d80, record_48, record_tag_7b7b08)
ADD_RECORDS(r0e31b0, record_40, record_tag_7b7b08)
APPEND_RECORD(r0e8b40, record_156, record_tag_7b7b08)
APPEND_RECORD(r0e8c70, record_16, record_tag_7b7b08)
APPEND_RECORD(r0e8da0, record_20, record_tag_7b7b08)
APPEND_RECORD(r0e8ee0, record_120, record_tag_7b7b08)
APPEND_RECORD(r0e9000, record_136, record_tag_7b7b08)
APPEND_RECORD(r0e9140, record_8, record_tag_7b7b08)
APPEND_RECORD(r0e9860, record_8, record_tag_7b7b08)
APPEND_RECORD(r0e9c20, record_16, record_tag_7b7b08)
APPEND_RECORD(r0e9e20, record_16, record_tag_7b7b08)
APPEND_RECORD(r0ea1c0, record_16, record_tag_7b7b08)
APPEND_RECORD(r0ea2f0, record_28, record_tag_7b7b08)
APPEND_RECORD(r0ea420, record_8, record_tag_7b7b08)
APPEND_RECORD(r0ea540, record_76, record_tag_7b7b08)
APPEND_RECORD(r0ea9a0, record_68, record_tag_7b7b08)
APPEND_RECORD(r1299d0, record_16, record_tag_7b7b08)
APPEND_RECORD(r12d900, record_12, record_tag_7b7b08)
PLACE_RECORD(r1318e0, record_4, record_tag_7b7b08)
APPEND_RECORD(r13f230, record_20, record_tag_7b7b08)
PLACE_RECORD(r148680, record_4, record_tag_7b7b08)
PLACE_RECORD(r1508d0, record_16, record_tag_7b7b08)
PLACE_RECORD(r150920, record_72, record_tag_7b7b08)
PLACE_RECORD(r150970, record_12, record_tag_7b7b08)
PLACE_RECORD(r1509c0, record_16, record_tag_7b7b08)
PLACE_RECORD(r150a10, record_28, record_tag_7b7b08)
PLACE_RECORD(r150a60, record_16, record_tag_7b7b08)
PLACE_RECORD(r150ab0, record_8, record_tag_7b7b08)
APPEND_RECORD(r150fa0, record_16, record_tag_7b7b08)
APPEND_RECORD(r151130, record_12, record_tag_7b7b08)
APPEND_RECORD(r152910, record_72, record_tag_7b7b08)
APPEND_RECORD(r152e30, record_28, record_tag_7b7b08)
PLACE_RECORD(r154ad0, record_28, record_tag_7b7b08)
PLACE_RECORD(r15bc30, record_60, record_tag_7b7b08)
PLACE_RECORD(r15bc80, record_20, record_tag_7b7b08)
PLACE_RECORD(r15bcd0, record_28, record_tag_7b7b08)
PLACE_RECORD(r15bd20, record_16, record_tag_7b7b08)
APPEND_RECORD(r15fc00, record_60, record_tag_7b7b08)
APPEND_RECORD(r15fe50, record_60, record_tag_7b7b08)
APPEND_RECORD(r15ffc0, record_28, record_tag_7b7b08)
APPEND_RECORD(r160140, record_16, record_tag_7b7b08)
PLACE_RECORD(r16c9b0, record_4, record_tag_7b7b08)
PLACE_RECORD(r16ca00, record_40, record_tag_7b7b08)
PLACE_RECORD(r16ca50, record_8, record_tag_7b7b08)
PLACE_RECORD(r16caa0, record_12, record_tag_7b7b08)
PLACE_RECORD(r16caf0, record_8, record_tag_7b7b08)
PLACE_RECORD(r16cea0, record_4, record_tag_7b7b08)
APPEND_RECORD(r1713f0, record_28, record_tag_7b7b08)
PLACE_RECORD(r172970, record_24, record_tag_7b7b08)
APPEND_RECORD(r176e40, record_8, record_tag_7b7b08)
ADD_RECORDS(r1973b0, record_4, record_tag_7ba17c)
PLACE_RECORD(r1ac190, record_56, record_tag_7b7b08)
APPEND_RECORD(r1af350, record_20, record_tag_7b7b08)
APPEND_RECORD(r1c6ed0, record_20, record_tag_7b7b08)
PLACE_RECORD(r1cea60, record_4, record_tag_7b7b08)
APPEND_RECORD(r1de400, record_8, record_tag_7b7b08)
PLACE_RECORD(r1dfe10, record_4, record_tag_7db4cc)
APPEND_RECORD(r1e1720, record_8, record_tag_7b7b08)
PLACE_RECORD(r1e9f30, record_16, record_tag_7b7b08)
PLACE_RECORD(r1e9f80, record_8, record_tag_7b7b08)
PLACE_RECORD(r1e9fd0, record_56, record_tag_7b7b08)
APPEND_RECORD(r1ea900, record_8, record_tag_7b7b08)
APPEND_RECORD(r1eaa60, record_56, record_tag_7b7b08)
PLACE_RECORD(r210440, record_32, record_tag_7b7b08)
APPEND_RECORD(r297910, record_148, record_tag_7b7b08)
APPEND_RECORD(r2b1eb0, record_100, record_tag_7b7b08)
APPEND_RECORD(r2b1f10, record_32, record_tag_7b7b08)
APPEND_RECORD(r2b1f70, record_16, record_tag_7b7b08)
APPEND_RECORD(r2dc820, record_20, record_tag_7b7b08)
APPEND_RECORD(r2dc9a0, record_8, record_tag_7b7b08)
APPEND_RECORD(r2dcae0, record_8, record_tag_7b7b08)
APPEND_RECORD(r2fcf20, record_48, record_tag_7b7b08)
APPEND_RECORD(r3065f0, record_8, record_tag_7b7b08)
APPEND_RECORD(r313670, record_12, record_tag_7b7b08)
APPEND_RECORD(r32c360, record_36, record_tag_7b7b08)
APPEND_RECORD(r331620, record_120, record_tag_7b7b08)
PLACE_RECORD(r3721f0, record_60, record_tag_7b7b08)
PLACE_RECORD(r372240, record_40, record_tag_7b7b08)
PLACE_RECORD(r372290, record_12, record_tag_7b7b08)
PLACE_RECORD(r3722e0, record_64, record_tag_7b7b08)
APPEND_RECORD(r388140, record_16, record_tag_7b7b08)
APPEND_RECORD(r3881b0, record_1100, record_tag_7b7b08)
APPEND_RECORD(r3a31b0, record_376, record_tag_7b7b08)
PLACE_RECORD(r3c7940, record_156, record_tag_7b7b08)
ADD_RECORDS(r3d45d0, record_4, record_tag_831360)
PLACE_RECORD(r3da230, record_60, record_tag_7b7b08)
PLACE_RECORD(r3da280, record_44, record_tag_7b7b08)
APPEND_RECORD(r413e40, record_12, record_tag_7b7b08)
APPEND_RECORD(r4140d0, record_20, record_tag_7b7b08)
APPEND_RECORD(r414150, record_44, record_tag_7b7b08)
APPEND_RECORD(r41aaf0, record_16, record_tag_7b7b08)
APPEND_RECORD(r41aea0, record_16, record_tag_7b7b08)
APPEND_RECORD(r41af10, record_12, record_tag_7b7b08)
APPEND_RECORD(r41b020, record_28, record_tag_7b7b08)
APPEND_RECORD(r41b100, record_12, record_tag_7b7b08)
APPEND_RECORD(r41b1b0, record_20, record_tag_7b7b08)
