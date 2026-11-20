struct Node { struct Node *next, *previous; };
struct List { unsigned reserved; struct Node *head; unsigned count; };

extern struct Node *__fastcall make_head_05f4c0(struct List *self);
extern struct Node *__fastcall make_head_1dbdd0(struct List *self);
extern struct Node *__fastcall make_head_2042a0(struct List *self);
extern struct Node *__fastcall make_head_204340(struct List *self);
extern struct Node *__fastcall make_head_2082f0(struct List *self);
extern struct Node *__fastcall make_head_208390(struct List *self);
extern struct Node *__fastcall make_head_208440(struct List *self);
extern struct Node *__fastcall make_head_2084e0(struct List *self);
extern struct Node *__fastcall make_head_23cae0(struct List *self);
extern struct Node *__fastcall make_head_480c50(struct List *self);
extern struct Node *__fastcall make_head_480cf0(struct List *self);
extern struct Node *__fastcall make_head_480d90(struct List *self);
extern struct Node *__fastcall make_head_48c210(struct List *self);
extern struct Node *__fastcall make_head_48c340(struct List *self);
extern struct Node *__fastcall make_head_494580(struct List *self);
extern struct Node *__fastcall make_head_494650(struct List *self);
extern struct Node *__fastcall make_head_49aa40(struct List *self);
extern struct Node *__fastcall make_head_4a1350(struct List *self);
extern struct Node *__fastcall make_head_4a13f0(struct List *self);
extern struct Node *__fastcall make_head_4a7ef0(struct List *self);
extern struct Node *__fastcall make_head_4b1af0(struct List *self);
extern struct Node *__fastcall make_head_4b6820(struct List *self);
extern struct Node *__fastcall make_head_4b94d0(struct List *self);
extern struct Node *__fastcall make_head_4b95b0(struct List *self);
extern struct Node *__fastcall make_head_4ba130(struct List *self);
extern struct Node *__fastcall make_head_4bdef0(struct List *self);
extern struct Node *__fastcall make_head_4c2a10(struct List *self);
extern struct Node *__fastcall make_head_4c56a0(struct List *self);
extern struct Node *__fastcall make_head_4c6b90(struct List *self);
extern struct Node *__fastcall make_head_4cd270(struct List *self);
extern struct Node *__fastcall make_head_4cf200(struct List *self);
extern struct Node *__fastcall make_head_4d6030(struct List *self);
extern struct Node *__fastcall make_head_4db5a0(struct List *self);
extern struct Node *__fastcall make_head_4dd000(struct List *self);
extern struct Node *__fastcall make_head_4dde10(struct List *self);
extern struct Node *__fastcall make_head_4e2df0(struct List *self);
extern struct Node *__fastcall make_head_4e2ec0(struct List *self);
extern struct Node *__fastcall make_head_4e4980(struct List *self);
extern struct Node *__fastcall make_head_4e6c40(struct List *self);
extern struct Node *__fastcall make_head_4f7830(struct List *self);
extern struct Node *__fastcall make_head_73aa40(struct List *self);

#define INITIALIZE_LIST(Entry, MakeHead) \
    struct List *__fastcall Entry(struct List *self) \
    { \
        self->head = MakeHead(self); \
        self->count = 0; \
        return self; \
    }

INITIALIZE_LIST(init_list_0617d0, make_head_05f4c0)
INITIALIZE_LIST(init_list_061eb0, make_head_05f4c0)
INITIALIZE_LIST(init_list_1dc3c0, make_head_1dbdd0)
INITIALIZE_LIST(init_list_1dc640, make_head_1dbdd0)
INITIALIZE_LIST(init_list_2044e0, make_head_2042a0)
INITIALIZE_LIST(init_list_2045c0, make_head_204340)
INITIALIZE_LIST(init_list_204690, make_head_204340)
INITIALIZE_LIST(init_list_2046f0, make_head_2042a0)
INITIALIZE_LIST(init_list_208730, make_head_2082f0)
INITIALIZE_LIST(init_list_208750, make_head_208390)
INITIALIZE_LIST(init_list_208770, make_head_208440)
INITIALIZE_LIST(init_list_208790, make_head_2084e0)
INITIALIZE_LIST(init_list_23cb90, make_head_23cae0)
INITIALIZE_LIST(init_list_481a00, make_head_480c50)
INITIALIZE_LIST(init_list_481ad0, make_head_480cf0)
INITIALIZE_LIST(init_list_481af0, make_head_480d90)
INITIALIZE_LIST(init_list_481c40, make_head_480cf0)
INITIALIZE_LIST(init_list_481c60, make_head_480d90)
INITIALIZE_LIST(init_list_482360, make_head_480c50)
INITIALIZE_LIST(init_list_48d060, make_head_48c210)
INITIALIZE_LIST(init_list_48d0f0, make_head_48c340)
INITIALIZE_LIST(init_list_48d140, make_head_48c210)
INITIALIZE_LIST(init_list_48d160, make_head_48c340)
INITIALIZE_LIST(init_list_494fa0, make_head_494580)
INITIALIZE_LIST(init_list_494fc0, make_head_494650)
INITIALIZE_LIST(init_list_495050, make_head_494580)
INITIALIZE_LIST(init_list_495070, make_head_494650)
INITIALIZE_LIST(init_list_49afb0, make_head_49aa40)
INITIALIZE_LIST(init_list_49afd0, make_head_49aa40)
INITIALIZE_LIST(init_list_4a1be0, make_head_4a1350)
INITIALIZE_LIST(init_list_4a1c00, make_head_4a13f0)
INITIALIZE_LIST(init_list_4a1c20, make_head_4a1350)
INITIALIZE_LIST(init_list_4a1c40, make_head_4a13f0)
INITIALIZE_LIST(init_list_4a9380, make_head_4a7ef0)
INITIALIZE_LIST(init_list_4a9bf0, make_head_4a7ef0)
INITIALIZE_LIST(init_list_4b1fe0, make_head_4b1af0)
INITIALIZE_LIST(init_list_4b2000, make_head_4b1af0)
INITIALIZE_LIST(init_list_4b7030, make_head_4b6820)
INITIALIZE_LIST(init_list_4b7050, make_head_4b6820)
INITIALIZE_LIST(init_list_4b9bf0, make_head_4b94d0)
INITIALIZE_LIST(init_list_4b9c10, make_head_4b95b0)
INITIALIZE_LIST(init_list_4b9c30, make_head_4b94d0)
INITIALIZE_LIST(init_list_4b9c50, make_head_4b95b0)
INITIALIZE_LIST(init_list_4ba510, make_head_4ba130)
INITIALIZE_LIST(init_list_4ba770, make_head_4ba130)
INITIALIZE_LIST(init_list_4be300, make_head_4bdef0)
INITIALIZE_LIST(init_list_4be320, make_head_4bdef0)
INITIALIZE_LIST(init_list_4c3020, make_head_4c2a10)
INITIALIZE_LIST(init_list_4c3040, make_head_4c2a10)
INITIALIZE_LIST(init_list_4c5d20, make_head_4c56a0)
INITIALIZE_LIST(init_list_4c5d70, make_head_4c56a0)
INITIALIZE_LIST(init_list_4c7020, make_head_4c6b90)
INITIALIZE_LIST(init_list_4c7040, make_head_4c6b90)
INITIALIZE_LIST(init_list_4cd650, make_head_4cd270)
INITIALIZE_LIST(init_list_4cd670, make_head_4cd270)
INITIALIZE_LIST(init_list_4cfe40, make_head_4cf200)
INITIALIZE_LIST(init_list_4cfed0, make_head_4cf200)
INITIALIZE_LIST(init_list_4d63c0, make_head_4d6030)
INITIALIZE_LIST(init_list_4d6420, make_head_4d6030)
INITIALIZE_LIST(init_list_4db710, make_head_4db5a0)
INITIALIZE_LIST(init_list_4db8c0, make_head_4db5a0)
INITIALIZE_LIST(init_list_4dd4d0, make_head_4dd000)
INITIALIZE_LIST(init_list_4dd5b0, make_head_4dd000)
INITIALIZE_LIST(init_list_4de480, make_head_4dde10)
INITIALIZE_LIST(init_list_4de570, make_head_4dde10)
INITIALIZE_LIST(init_list_4e3590, make_head_4e2df0)
INITIALIZE_LIST(init_list_4e35e0, make_head_4e2ec0)
INITIALIZE_LIST(init_list_4e3640, make_head_4e2df0)
INITIALIZE_LIST(init_list_4e38b0, make_head_4e2ec0)
INITIALIZE_LIST(init_list_4e3930, make_head_4e2ec0)
INITIALIZE_LIST(init_list_4e4e00, make_head_4e4980)
INITIALIZE_LIST(init_list_4e4ed0, make_head_4e4980)
INITIALIZE_LIST(init_list_4e7060, make_head_4e6c40)
INITIALIZE_LIST(init_list_4e70c0, make_head_4e6c40)
INITIALIZE_LIST(init_list_4f8230, make_head_4f7830)
INITIALIZE_LIST(init_list_4f8270, make_head_4f7830)
INITIALIZE_LIST(init_list_73abf0, make_head_73aa40)
INITIALIZE_LIST(init_list_73add0, make_head_73aa40)
