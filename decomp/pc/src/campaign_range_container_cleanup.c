struct Node { struct Node *next; };
struct Container { unsigned reserved; struct Node *head; unsigned count; };
struct Pointer { struct Node *ptr; };
struct Result { struct Node **ptr; };
extern void __fastcall erase_48a6c0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_48d990(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_497030(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_499e10(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4a0150(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4a3e90(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4a6890(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4ac700(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4afff0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4b0bd0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4c07f0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4c6390(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4cb790(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4cb890(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4d06e0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4d4a50(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4d83f0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4dee30(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4eceb0(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void __fastcall erase_4ee800(struct Container *, struct Result, struct Pointer, struct Pointer);
extern void *__cdecl prepare_release(struct Pointer, unsigned);
extern void __cdecl release_prepared(void *);

#define DESTROY_CONTAINER(Entry, Erase) \
    void __fastcall Entry(struct Container *self) \
    { \
        struct Node *unused; \
        struct Result result; \
        struct Pointer first, end; \
        end.ptr = self->head; \
        first.ptr = end.ptr->next; \
        result.ptr = &unused; \
        Erase(self, result, first, end); \
        end.ptr = self->head; \
        release_prepared(prepare_release(end, 7)); \
        self->head = 0; \
        self->count = 0; \
    }

DESTROY_CONTAINER(cleanup_48a780, erase_48a6c0)
DESTROY_CONTAINER(cleanup_48a880, erase_48a6c0)
DESTROY_CONTAINER(cleanup_48aae0, erase_48a6c0)
DESTROY_CONTAINER(cleanup_48ac90, erase_48a6c0)
DESTROY_CONTAINER(cleanup_48dcf0, erase_48d990)
DESTROY_CONTAINER(cleanup_48e200, erase_48d990)
DESTROY_CONTAINER(cleanup_48e240, erase_48d990)
DESTROY_CONTAINER(cleanup_48e4a0, erase_48d990)
DESTROY_CONTAINER(cleanup_497300, erase_497030)
DESTROY_CONTAINER(cleanup_497400, erase_497030)
DESTROY_CONTAINER(cleanup_4975b0, erase_497030)
DESTROY_CONTAINER(cleanup_4977b0, erase_497030)
DESTROY_CONTAINER(cleanup_49a0d0, erase_499e10)
DESTROY_CONTAINER(cleanup_49a2a0, erase_499e10)
DESTROY_CONTAINER(cleanup_49a2e0, erase_499e10)
DESTROY_CONTAINER(cleanup_49a3d0, erase_499e10)
DESTROY_CONTAINER(cleanup_4a0380, erase_4a0150)
DESTROY_CONTAINER(cleanup_4a0440, erase_4a0150)
DESTROY_CONTAINER(cleanup_4a0480, erase_4a0150)
DESTROY_CONTAINER(cleanup_4a0590, erase_4a0150)
DESTROY_CONTAINER(cleanup_4a40f0, erase_4a3e90)
DESTROY_CONTAINER(cleanup_4a41a0, erase_4a3e90)
DESTROY_CONTAINER(cleanup_4a41e0, erase_4a3e90)
DESTROY_CONTAINER(cleanup_4a4340, erase_4a3e90)
DESTROY_CONTAINER(cleanup_4a6ba0, erase_4a6890)
DESTROY_CONTAINER(cleanup_4a6df0, erase_4a6890)
DESTROY_CONTAINER(cleanup_4a6fb0, erase_4a6890)
DESTROY_CONTAINER(cleanup_4a7150, erase_4a6890)
DESTROY_CONTAINER(cleanup_4ac7c0, erase_4ac700)
DESTROY_CONTAINER(cleanup_4acb40, erase_4ac700)
DESTROY_CONTAINER(cleanup_4acd30, erase_4ac700)
DESTROY_CONTAINER(cleanup_4ace30, erase_4ac700)
DESTROY_CONTAINER(cleanup_4b03a0, erase_4afff0)
DESTROY_CONTAINER(cleanup_4b0800, erase_4afff0)
DESTROY_CONTAINER(cleanup_4b0870, erase_4afff0)
DESTROY_CONTAINER(cleanup_4b0ce0, erase_4afff0)
DESTROY_CONTAINER(cleanup_4b0d80, erase_4b0bd0)
DESTROY_CONTAINER(cleanup_4b0e20, erase_4b0bd0)
DESTROY_CONTAINER(cleanup_4b1150, erase_4b0bd0)
DESTROY_CONTAINER(cleanup_4c0ac0, erase_4c07f0)
DESTROY_CONTAINER(cleanup_4c0bc0, erase_4c07f0)
DESTROY_CONTAINER(cleanup_4c0d70, erase_4c07f0)
DESTROY_CONTAINER(cleanup_4c0f70, erase_4c07f0)
DESTROY_CONTAINER(cleanup_4c6630, erase_4c6390)
DESTROY_CONTAINER(cleanup_4c6800, erase_4c6390)
DESTROY_CONTAINER(cleanup_4c6840, erase_4c6390)
DESTROY_CONTAINER(cleanup_4c6910, erase_4c6390)
DESTROY_CONTAINER(cleanup_4c6950, erase_4c6390)
DESTROY_CONTAINER(cleanup_4cb850, erase_4cb790)
DESTROY_CONTAINER(cleanup_4cb950, erase_4cb890)
DESTROY_CONTAINER(cleanup_4cbcc0, erase_4cb790)
DESTROY_CONTAINER(cleanup_4cbd60, erase_4cb890)
DESTROY_CONTAINER(cleanup_4cc0f0, erase_4cb790)
DESTROY_CONTAINER(cleanup_4cc130, erase_4cb890)
DESTROY_CONTAINER(cleanup_4cc650, erase_4cb790)
DESTROY_CONTAINER(cleanup_4cc6c0, erase_4cb890)
DESTROY_CONTAINER(cleanup_4d09e0, erase_4d06e0)
DESTROY_CONTAINER(cleanup_4d0ba0, erase_4d06e0)
DESTROY_CONTAINER(cleanup_4d0c60, erase_4d06e0)
DESTROY_CONTAINER(cleanup_4d1370, erase_4d06e0)
DESTROY_CONTAINER(cleanup_4d13e0, erase_4d06e0)
DESTROY_CONTAINER(cleanup_4d4df0, erase_4d4a50)
DESTROY_CONTAINER(cleanup_4d4ef0, erase_4d4a50)
DESTROY_CONTAINER(cleanup_4d4f30, erase_4d4a50)
DESTROY_CONTAINER(cleanup_4d5290, erase_4d4a50)
DESTROY_CONTAINER(cleanup_4d8690, erase_4d83f0)
DESTROY_CONTAINER(cleanup_4d8750, erase_4d83f0)
DESTROY_CONTAINER(cleanup_4d8790, erase_4d83f0)
DESTROY_CONTAINER(cleanup_4d88b0, erase_4d83f0)
DESTROY_CONTAINER(cleanup_4def50, erase_4dee30)
DESTROY_CONTAINER(cleanup_4defd0, erase_4dee30)
DESTROY_CONTAINER(cleanup_4df300, erase_4dee30)
DESTROY_CONTAINER(cleanup_4ecf70, erase_4eceb0)
DESTROY_CONTAINER(cleanup_4ed160, erase_4eceb0)
DESTROY_CONTAINER(cleanup_4ed3a0, erase_4eceb0)
DESTROY_CONTAINER(cleanup_4ed510, erase_4eceb0)
DESTROY_CONTAINER(cleanup_4eec80, erase_4ee800)
DESTROY_CONTAINER(cleanup_4eed30, erase_4ee800)
DESTROY_CONTAINER(cleanup_4eed70, erase_4ee800)
DESTROY_CONTAINER(cleanup_4eeeb0, erase_4ee800)
