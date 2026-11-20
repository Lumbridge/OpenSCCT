struct Decoder { const void *methods; };
struct Description { const void *value; };
/* The base initializer receives width and mode in adjacent 32-bit x86 stack slots. */
extern void __fastcall initialize_decoder(struct Decoder *, struct Description, unsigned __int64);

extern const void *const methods_860798[];
extern const void *const methods_8607a8[];
extern const void *const methods_8607b8[];
extern const void *const methods_8607c8[];
extern const void *const methods_8607d8[];
extern const void *const methods_8607e8[];
extern const void *const methods_8607f8[];
extern const void *const methods_860808[];
extern const void *const methods_860818[];
extern const void *const methods_860828[];
extern const void *const methods_860838[];
extern const void *const methods_860848[];
extern const void *const methods_860858[];
extern const void *const methods_860868[];
extern const void *const methods_860878[];
extern const void *const methods_860888[];
extern const void *const methods_860898[];
extern const void *const methods_8608ac[];
extern const void *const methods_8608bc[];
extern const void *const methods_8608cc[];
extern const void *const methods_8608dc[];
extern const void *const methods_8608ec[];
extern const void *const methods_8608fc[];
extern const void *const methods_86090c[];
extern const void *const methods_86091c[];
extern const void *const methods_86092c[];
extern const void *const methods_86093c[];
extern const void *const methods_86094c[];
extern const void *const methods_86095c[];
extern const void *const methods_86096c[];
extern const void *const methods_86097c[];
extern const void *const methods_86099c[];
extern const void *const methods_8609ac[];
extern const void *const methods_8609bc[];
extern const void *const methods_8609ec[];
extern const void *const methods_8609fc[];
extern const void *const methods_860a0c[];
extern const void *const methods_860a1c[];
extern const void *const methods_860a40[];
extern const void *const methods_860a50[];
extern const void *const methods_860a60[];

#define CONSTRUCT_DECODER(Entry, Bits, Mode, Methods) \
    struct Decoder *__fastcall Entry(struct Decoder *self, struct Description description) \
    { \
        initialize_decoder(self, description, ((unsigned __int64)Mode << 32) | Bits); \
        self->methods = Methods; \
        return self; \
    }

CONSTRUCT_DECODER(construct_66e3ec, 24, 1, methods_860798)
CONSTRUCT_DECODER(construct_66e4d8, 32, 1, methods_8607a8)
CONSTRUCT_DECODER(construct_66e5ad, 32, 1, methods_8607b8)
CONSTRUCT_DECODER(construct_66e5e5, 16, 1, methods_8607c8)
CONSTRUCT_DECODER(construct_66e699, 16, 1, methods_8607d8)
CONSTRUCT_DECODER(construct_66e758, 16, 1, methods_8607e8)
CONSTRUCT_DECODER(construct_66e82e, 16, 1, methods_8607f8)
CONSTRUCT_DECODER(construct_66e904, 8, 1, methods_860808)
CONSTRUCT_DECODER(construct_66e9c5, 8, 1, methods_860818)
CONSTRUCT_DECODER(construct_66eab8, 16, 1, methods_860828)
CONSTRUCT_DECODER(construct_66eaf0, 16, 1, methods_860838)
CONSTRUCT_DECODER(construct_66ec08, 32, 1, methods_860848)
CONSTRUCT_DECODER(construct_66ed0c, 32, 1, methods_860858)
CONSTRUCT_DECODER(construct_66ed44, 32, 1, methods_860868)
CONSTRUCT_DECODER(construct_66ee77, 32, 1, methods_860878)
CONSTRUCT_DECODER(construct_66ef71, 32, 1, methods_860888)
CONSTRUCT_DECODER(construct_66efa9, 64, 1, methods_860898)
CONSTRUCT_DECODER(construct_66f0fa, 16, 1, methods_8608ac)
CONSTRUCT_DECODER(construct_66f132, 8, 1, methods_8608bc)
CONSTRUCT_DECODER(construct_66f281, 8, 1, methods_8608cc)
CONSTRUCT_DECODER(construct_66f39a, 16, 1, methods_8608dc)
CONSTRUCT_DECODER(construct_66f3d2, 8, 1, methods_8608ec)
CONSTRUCT_DECODER(construct_66f493, 16, 1, methods_8608fc)
CONSTRUCT_DECODER(construct_66f54b, 16, 2, methods_86090c)
CONSTRUCT_DECODER(construct_66f614, 16, 2, methods_86091c)
CONSTRUCT_DECODER(construct_66f64c, 32, 2, methods_86092c)
CONSTRUCT_DECODER(construct_66f711, 32, 3, methods_86093c)
CONSTRUCT_DECODER(construct_66f820, 32, 2, methods_86094c)
CONSTRUCT_DECODER(construct_66f9e2, 32, 2, methods_86095c)
CONSTRUCT_DECODER(construct_66fa1a, 64, 3, methods_86096c)
CONSTRUCT_DECODER(construct_66faf7, 16, 2, methods_86097c)
CONSTRUCT_DECODER(construct_66fc21, 16, 1, methods_86099c)
CONSTRUCT_DECODER(construct_66fc59, 32, 4, methods_8609ac)
CONSTRUCT_DECODER(construct_66fc91, 16, 4, methods_8609bc)
CONSTRUCT_DECODER(construct_66ffd9, 32, 4, methods_8609ec)
CONSTRUCT_DECODER(construct_670011, 64, 4, methods_8609fc)
CONSTRUCT_DECODER(construct_670049, 32, 4, methods_860a0c)
CONSTRUCT_DECODER(construct_670081, 64, 4, methods_860a1c)
CONSTRUCT_DECODER(construct_670485, 128, 4, methods_860a40)
CONSTRUCT_DECODER(construct_6704c0, 32, 1, methods_860a50)
CONSTRUCT_DECODER(construct_6704f8, 48, 1, methods_860a60)
