struct Object { const void *methods; };
struct Flags { unsigned value; };

extern const void *const methods_7be298[];
extern const void *const methods_7be300[];
extern const void *const methods_7be308[];
extern const void *const methods_7d416c[];
extern const void *const methods_7d4170[];
extern const void *const methods_7d8cdc[];
extern const void *const methods_7d98d4[];
extern const void *const methods_7dad24[];
extern const void *const methods_7dad2c[];
extern const void *const methods_7db34c[];
extern const void *const methods_7de304[];
extern const void *const methods_7e2254[];
extern const void *const methods_83a988[];
extern const void *const methods_83a99c[];
extern const void *const methods_83aa00[];
extern const void *const methods_83b52c[];
extern const void *const methods_83b830[];
extern const void *const methods_83b8a0[];
extern const void *const methods_83b978[];
extern const void *const methods_83bafc[];
extern const void *const methods_83c63c[];
extern const void *const methods_83cfe4[];
extern const void *const methods_83d858[];
extern const void *const methods_83e7bc[];
extern const void *const methods_83f3c0[];
extern const void *const methods_83f3d8[];
extern const void *const methods_840a54[];
extern const void *const methods_8426cc[];
extern const void *const methods_842f9c[];
extern const void *const methods_8437bc[];
extern const void *const methods_843a30[];
extern const void *const methods_843a34[];
extern const void *const methods_844480[];
extern const void *const methods_84520c[];
extern const void *const methods_847da8[];
extern const void *const methods_847f0c[];
extern const void *const methods_848008[];
extern const void *const methods_849478[];
extern const void *const methods_8496a0[];
extern const void *const methods_8496a4[];
extern const void *const methods_849704[];
extern const void *const methods_84af5c[];
extern const void *const methods_84bc9c[];
extern const void *const methods_84eb50[];
extern const void *const methods_8736b0[];
extern const void *const methods_874e54[];
extern const void *const methods_876228[];
extern const void *const methods_876380[];
extern const void *const methods_876428[];
extern const void *const methods_876468[];
extern void __cdecl release_0014a0(void *);
extern void __cdecl release_1dd0d0(void *);
extern void __cdecl release_1dd8b0(void *);
extern void __cdecl release_213be0(void *);
extern void __cdecl release_427b20(void *);
extern void __cdecl release_427d00(void *);
extern void __cdecl release_4388c0(void *);
extern void __cdecl release_438d20(void *);
extern void __cdecl release_44ea60(void *);
extern void __cdecl release_459a00(void *);
extern void __cdecl release_4645b0(void *);
extern void __cdecl release_487b90(void *);
extern void __cdecl release_767a10(void *);

#define DESTROY(Entry, Methods, Release) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        self->methods = Methods; \
        if (flags.value & 1) \
            Release(self); \
        return self; \
    }

DESTROY(destroy_05c3b0, methods_7be298, release_487b90)
DESTROY(destroy_05cdd0, methods_7be300, release_487b90)
DESTROY(destroy_05ce10, methods_7be300, release_487b90)
DESTROY(destroy_05ce50, methods_7be308, release_487b90)
DESTROY(destroy_05ce90, methods_7be308, release_487b90)
DESTROY(destroy_05eca0, methods_7be298, release_487b90)
DESTROY(destroy_135a50, methods_7d416c, release_487b90)
DESTROY(destroy_135a90, methods_7d4170, release_487b90)
DESTROY(destroy_135af0, methods_7d416c, release_487b90)
DESTROY(destroy_135b10, methods_7d4170, release_487b90)
DESTROY(destroy_19c8d0, methods_7d8cdc, release_487b90)
DESTROY(destroy_19ceb0, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e3f0, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e410, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e430, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e450, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e470, methods_7d8cdc, release_487b90)
DESTROY(destroy_19e490, methods_7d8cdc, release_487b90)
DESTROY(destroy_19f8b0, methods_7d8cdc, release_487b90)
DESTROY(destroy_1a0070, methods_7d8cdc, release_487b90)
DESTROY(destroy_1a0840, methods_7d8cdc, release_487b90)
DESTROY(destroy_1a3cd0, methods_7d98d4, release_487b90)
DESTROY(destroy_1d3af0, methods_7dad24, release_1dd0d0)
DESTROY(destroy_1d3bc0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d4100, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d4120, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d5260, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d5280, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d52a0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1d53d0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_1db2a0, methods_7db34c, release_1dd0d0)
DESTROY(destroy_1db920, methods_7db34c, release_1dd0d0)
DESTROY(destroy_1dd890, methods_7dad24, release_1dd8b0)
DESTROY(destroy_212ea0, methods_7de304, release_1dd0d0)
DESTROY(destroy_213bc0, methods_7dad24, release_213be0)
DESTROY(destroy_22a0f0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_22c9e0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_2432e0, methods_7e2254, release_1dd0d0)
DESTROY(destroy_426200, methods_7dad24, release_1dd0d0)
DESTROY(destroy_427b00, methods_7dad24, release_427b20)
DESTROY(destroy_427ce0, methods_7dad24, release_427d00)
DESTROY(destroy_4388a0, methods_7dad24, release_4388c0)
DESTROY(destroy_438d00, methods_7dad24, release_438d20)
DESTROY(destroy_439640, methods_83a988, release_1dd0d0)
DESTROY(destroy_4396a0, methods_83a99c, release_1dd0d0)
DESTROY(destroy_439c90, methods_7dad24, release_1dd0d0)
DESTROY(destroy_43b8e0, methods_83aa00, release_1dd0d0)
DESTROY(destroy_44ea40, methods_7dad24, release_44ea60)
DESTROY(destroy_4599e0, methods_7dad24, release_459a00)
DESTROY(destroy_45bcd0, methods_83b52c, release_1dd0d0)
DESTROY(destroy_45bd20, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_45bd40, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_464590, methods_7dad24, release_4645b0)
DESTROY(destroy_4653c0, methods_83b830, release_0014a0)
DESTROY(destroy_465540, methods_83b830, release_1dd0d0)
DESTROY(destroy_465560, methods_83b830, release_1dd0d0)
DESTROY(destroy_465580, methods_83b830, release_1dd0d0)
DESTROY(destroy_465670, methods_83b8a0, release_0014a0)
DESTROY(destroy_4667b0, methods_7dad2c, release_1dd0d0)
DESTROY(destroy_466840, methods_83b978, release_1dd0d0)
DESTROY(destroy_4668d0, methods_83b978, release_1dd0d0)
DESTROY(destroy_466b80, methods_83aa00, release_1dd0d0)
DESTROY(destroy_466be0, methods_83aa00, release_1dd0d0)
DESTROY(destroy_467480, methods_83bafc, release_1dd0d0)
DESTROY(destroy_47ff40, methods_83c63c, release_487b90)
DESTROY(destroy_480300, methods_83c63c, release_487b90)
DESTROY(destroy_48f180, methods_83cfe4, release_487b90)
DESTROY(destroy_48f1c0, methods_83cfe4, release_487b90)
DESTROY(destroy_48f970, methods_7d8cdc, release_487b90)
DESTROY(destroy_48f990, methods_7d8cdc, release_487b90)
DESTROY(destroy_48f9b0, methods_7d8cdc, release_487b90)
DESTROY(destroy_48f9d0, methods_7d8cdc, release_487b90)
DESTROY(destroy_497c10, methods_83d858, release_487b90)
DESTROY(destroy_497eb0, methods_7be298, release_487b90)
DESTROY(destroy_4a4870, methods_83e7bc, release_487b90)
DESTROY(destroy_4ad3e0, methods_83f3c0, release_487b90)
DESTROY(destroy_4ad5e0, methods_83f3d8, release_487b90)
DESTROY(destroy_4ae3d0, methods_83e7bc, release_487b90)
DESTROY(destroy_4b30f0, methods_7d8cdc, release_487b90)
DESTROY(destroy_4b3110, methods_7d8cdc, release_487b90)
DESTROY(destroy_4b3130, methods_7d8cdc, release_487b90)
DESTROY(destroy_4b3150, methods_7d8cdc, release_487b90)
DESTROY(destroy_4b6600, methods_83c63c, release_487b90)
DESTROY(destroy_4b6620, methods_83c63c, release_487b90)
DESTROY(destroy_4b7640, methods_840a54, release_487b90)
DESTROY(destroy_4bc3d0, methods_8426cc, release_487b90)
DESTROY(destroy_4c2210, methods_842f9c, release_487b90)
DESTROY(destroy_4c33b0, methods_8437bc, release_487b90)
DESTROY(destroy_4c34d0, methods_8437bc, release_487b90)
DESTROY(destroy_4c4750, methods_843a30, release_487b90)
DESTROY(destroy_4c4980, methods_843a34, release_487b90)
DESTROY(destroy_4cd900, methods_844480, release_487b90)
DESTROY(destroy_4d2f20, methods_7d8cdc, release_487b90)
DESTROY(destroy_4d5d80, methods_83e7bc, release_487b90)
DESTROY(destroy_4d6bc0, methods_84520c, release_487b90)
DESTROY(destroy_4db370, methods_83c63c, release_487b90)
DESTROY(destroy_4dbec0, methods_844480, release_487b90)
DESTROY(destroy_4e00a0, methods_844480, release_487b90)
DESTROY(destroy_4e93f0, methods_847da8, release_487b90)
DESTROY(destroy_4e9720, methods_847f0c, release_487b90)
DESTROY(destroy_4e9bd0, methods_848008, release_487b90)
DESTROY(destroy_4f1890, methods_847f0c, release_487b90)
DESTROY(destroy_4f4840, methods_7d8cdc, release_487b90)
DESTROY(destroy_4f5080, methods_83cfe4, release_487b90)
DESTROY(destroy_4f51d0, methods_7d8cdc, release_487b90)
DESTROY(destroy_4f60b0, methods_7d8cdc, release_487b90)
DESTROY(destroy_4f60d0, methods_7d8cdc, release_487b90)
DESTROY(destroy_4f6f20, methods_849478, release_487b90)
DESTROY(destroy_4f6f60, methods_849478, release_487b90)
DESTROY(destroy_4fa8e0, methods_8496a0, release_487b90)
DESTROY(destroy_4fa920, methods_8496a4, release_487b90)
DESTROY(destroy_4fa980, methods_8496a0, release_487b90)
DESTROY(destroy_4fa9a0, methods_8496a4, release_487b90)
DESTROY(destroy_4fc5e0, methods_849704, release_0014a0)
DESTROY(destroy_52f2f0, methods_84af5c, release_0014a0)
DESTROY(destroy_530320, methods_84af5c, release_0014a0)
DESTROY(destroy_550ad0, methods_84bc9c, release_0014a0)
DESTROY(destroy_550b30, methods_84bc9c, release_0014a0)
DESTROY(destroy_5525f0, methods_84bc9c, release_0014a0)
DESTROY(destroy_552620, methods_84bc9c, release_0014a0)
DESTROY(destroy_57a590, methods_84eb50, release_0014a0)
DESTROY(destroy_73a380, methods_7d8cdc, release_487b90)
DESTROY(destroy_747ab0, methods_8736b0, release_0014a0)
DESTROY(destroy_7544d0, methods_874e54, release_0014a0)
DESTROY(destroy_761bd0, methods_876228, release_767a10)
DESTROY(destroy_7634b0, methods_876380, release_767a10)
DESTROY(destroy_7641b0, methods_876428, release_767a10)
DESTROY(destroy_764c40, methods_876468, release_767a10)
