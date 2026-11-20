struct StaticRecord;

#define CLEANUP(Entry, Object, Destroy) \
void Entry(void) \
{ \
    Destroy(&Object); \
}

extern struct StaticRecord static_stack_object_373cac;
extern struct StaticRecord static_stack_object_39776c;
extern struct StaticRecord static_stack_object_397778;
extern struct StaticRecord static_stack_object_397784;
extern struct StaticRecord static_stack_object_397bf0;
extern struct StaticRecord static_stack_object_39cf4c;
extern struct StaticRecord static_stack_object_3b7848;
extern struct StaticRecord static_stack_object_3bc5dc;
extern struct StaticRecord static_stack_object_3bca48;
extern struct StaticRecord static_stack_object_3bf650;
extern struct StaticRecord static_stack_object_3bfd4c;
extern struct StaticRecord static_stack_object_3c9e5c;
extern struct StaticRecord static_stack_object_3c9e74;
extern struct StaticRecord static_stack_object_3c9e80;
extern struct StaticRecord static_stack_object_3c9e98;
extern struct StaticRecord static_stack_object_3c9ea4;
extern struct StaticRecord static_stack_object_3c9eb0;
extern struct StaticRecord static_stack_object_3c9ebc;
extern struct StaticRecord static_stack_object_3c9ec8;
extern void __stdcall finalize_static_01f040(struct StaticRecord *object);
extern void __stdcall finalize_static_01f0f0(struct StaticRecord *object);
extern void __stdcall finalize_static_057780(struct StaticRecord *object);
extern void __stdcall finalize_static_0cdcc0(struct StaticRecord *object);
extern void __stdcall finalize_static_10a7e0(struct StaticRecord *object);
extern void __stdcall finalize_static_14e510(struct StaticRecord *object);
extern void __stdcall finalize_static_17bf30(struct StaticRecord *object);
extern void __stdcall finalize_static_1b4120(struct StaticRecord *object);
extern void __stdcall finalize_static_1edf20(struct StaticRecord *object);
extern void __stdcall finalize_static_208590(struct StaticRecord *object);

CLEANUP(cleanup_stack_object_2dc6d0, static_stack_object_3c9e74, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dc6e0, static_stack_object_3c9e80, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dc700, static_stack_object_3c9e98, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dc710, static_stack_object_3c9ea4, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dc720, static_stack_object_3c9eb0, finalize_static_01f040)
CLEANUP(cleanup_stack_object_2dc730, static_stack_object_3c9ebc, finalize_static_01f0f0)
CLEANUP(cleanup_stack_object_2dc740, static_stack_object_3c9ec8, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dc800, static_stack_object_3c9e5c, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dd180, static_stack_object_3bfd4c, finalize_static_1edf20)
CLEANUP(cleanup_stack_object_2dd190, static_stack_object_3bf650, finalize_static_0cdcc0)
CLEANUP(cleanup_stack_object_2dd440, static_stack_object_3bca48, finalize_static_10a7e0)
CLEANUP(cleanup_stack_object_2dd490, static_stack_object_3bc5dc, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2dd500, static_stack_object_373cac, finalize_static_057780)
CLEANUP(cleanup_stack_object_2dd530, static_stack_object_3b7848, finalize_static_14e510)
CLEANUP(cleanup_stack_object_2ddf50, static_stack_object_39cf4c, finalize_static_1b4120)
CLEANUP(cleanup_stack_object_2de3e0, static_stack_object_397bf0, finalize_static_208590)
CLEANUP(cleanup_stack_object_2de430, static_stack_object_39776c, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2de440, static_stack_object_397778, finalize_static_17bf30)
CLEANUP(cleanup_stack_object_2de450, static_stack_object_397784, finalize_static_17bf30)
