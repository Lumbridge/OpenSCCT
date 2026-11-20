struct Actor;
struct Music;
struct AIPawn;
struct Level {
    unsigned char object_header[0x28];
    struct Actor **actors;
    int actor_count;
};
struct Music {
    unsigned char object_header[0x30];
    unsigned int flags;
    unsigned char actor_fields[0x7c - 0x34];
    struct Level *level;
    unsigned char settings[0x2b8 - 0x80];
    struct AIPawn **ai_pawns;
    int count;
    int capacity;
};

struct Allocator;
struct AllocatorMethods {
    void *unused;
    void *(__fastcall *resize)(struct Allocator *, const struct AllocatorMethods *,
                               void *, int, const unsigned short *);
};
struct Allocator { const struct AllocatorMethods *methods; };
extern struct Allocator *engine_allocator;
extern const unsigned short array_allocation_name[];
void __fastcall actor_post_begin_play(struct Music *music);
struct AIPawn *__cdecl cast_ai_pawn(struct Actor *actor);

void __fastcall music_post_begin_play(struct Music *music)
{
    int i;
    actor_post_begin_play(music);
    music->flags |= 0x200;
    for (i = 0; i < music->level->actor_count; ++i) {
        struct AIPawn *pawn = cast_ai_pawn(music->level->actors[i]);
        if (pawn) {
            int index = music->count++;
            if (music->count > music->capacity) {
                music->capacity = music->count + 3 * music->count / 8 + 2;
                music->ai_pawns = engine_allocator->methods->resize(
                    engine_allocator, engine_allocator->methods, music->ai_pawns,
                    music->capacity * sizeof(*music->ai_pawns), array_allocation_name);
            }
            music->ai_pawns[index] = pawn;
        }
    }
}
