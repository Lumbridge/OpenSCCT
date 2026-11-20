/* The element types are identified only by their copied storage widths. */

struct Record4 { unsigned word[1]; };
struct Record12 { unsigned word[3]; };
struct Record16 { unsigned word[4]; };

#define CONSTRUCT_RECORD(Entry, T) \
    void __stdcall Entry(struct T *destination, const struct T *source) \
    { \
        if (destination) *destination = *source; \
    }

CONSTRUCT_RECORD(copy_10e540, Record4)
CONSTRUCT_RECORD(copy_1a1020, Record4)
CONSTRUCT_RECORD(copy_1a20c0, Record4)
CONSTRUCT_RECORD(copy_1a20e0, Record4)
CONSTRUCT_RECORD(copy_1a2100, Record4)
CONSTRUCT_RECORD(copy_21d080, Record16)
CONSTRUCT_RECORD(copy_21d0b0, Record16)
CONSTRUCT_RECORD(copy_21d0e0, Record16)
CONSTRUCT_RECORD(copy_21d110, Record16)
CONSTRUCT_RECORD(copy_21d140, Record16)
CONSTRUCT_RECORD(copy_385960, Record12)
CONSTRUCT_RECORD(copy_4944a0, Record4)
CONSTRUCT_RECORD(copy_49c0b0, Record4)
CONSTRUCT_RECORD(copy_4be860, Record4)
CONSTRUCT_RECORD(copy_4c76e0, Record4)
