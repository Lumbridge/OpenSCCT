struct Word { unsigned value; };
struct Source { const struct Word *word; };

#define COPY_WORD(Entry) \
    struct Word *__fastcall Entry(struct Word *out, struct Source from) \
    { \
        *out = *from.word; \
        return out; \
    }

COPY_WORD(copy_5ed1c0)
COPY_WORD(copy_5ed1f0)
COPY_WORD(copy_5ed220)
COPY_WORD(copy_5ed250)
COPY_WORD(copy_5f72c0)
COPY_WORD(copy_5f7720)
COPY_WORD(copy_5fde20)
COPY_WORD(copy_601d60)
COPY_WORD(copy_6042a0)
COPY_WORD(copy_604310)
COPY_WORD(copy_6112d0)
COPY_WORD(copy_611530)
COPY_WORD(copy_618650)
COPY_WORD(copy_620690)
COPY_WORD(copy_6207b0)
COPY_WORD(copy_626320)
COPY_WORD(copy_627d70)
COPY_WORD(copy_628720)
COPY_WORD(copy_62c000)
COPY_WORD(copy_62eb00)
COPY_WORD(copy_722850)
COPY_WORD(copy_722b80)
COPY_WORD(copy_722d80)
COPY_WORD(copy_722da0)
COPY_WORD(copy_722e90)
COPY_WORD(copy_739210)
COPY_WORD(copy_739220)
