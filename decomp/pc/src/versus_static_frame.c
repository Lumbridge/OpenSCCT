struct StaticRecord;

#define CLEANUP(Entry, Object, Destroy) \
void Entry(void) \
{ \
    Destroy(&Object); \
}

extern struct StaticRecord static_frame_3be700;
extern struct StaticRecord static_frame_3be818;
extern struct StaticRecord static_frame_3be930;
extern void __fastcall finalize_static_00ceb0(struct StaticRecord *object);

CLEANUP(cleanup_frame_2dd270, static_frame_3be700, finalize_static_00ceb0)
CLEANUP(cleanup_frame_2dd280, static_frame_3be818, finalize_static_00ceb0)
CLEANUP(cleanup_frame_2dd290, static_frame_3be930, finalize_static_00ceb0)
