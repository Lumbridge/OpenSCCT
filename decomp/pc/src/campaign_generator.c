struct Generator;
struct Actor;
/* A one-word struct is stack-passed even in x86 fastcall. This gives the
   actor method its observed ECX receiver and single stack argument. */
struct GeneratorReference { struct Generator *value; };

struct Generator {
    unsigned char object_header[0x34];
    unsigned long flags;
    unsigned char behavior[0x40c - 0x38];
    struct Actor **connected_actors;
    int connected_count;
};

void __fastcall gameplay_object_post_begin_play(struct Generator *generator);
void __fastcall actor_set_owner(struct Actor *child, struct GeneratorReference owner);

void __fastcall generator_post_begin_play(struct Generator *generator)
{
    int i;
    struct GeneratorReference owner;
    gameplay_object_post_begin_play(generator);
    for (i = 0; i < generator->connected_count; ++i) {
        struct Actor *child = generator->connected_actors[i];
        if (child) {
            owner.value = generator;
            actor_set_owner(child, owner);
        }
    }
    generator->flags |= 0x8000;
}
