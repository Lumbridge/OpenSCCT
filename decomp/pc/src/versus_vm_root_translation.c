#include "versus_vm_argument.h"
typedef struct Vector { float x, y, z; } Vector;
typedef struct Class {
    char unknown[0x28];
    struct Class *parent;
} Class;
typedef struct Mesh {
    char unknown[0x24];
    Class *type;
} Mesh;
typedef struct Actor {
    char before_mesh[0x1bc];
    Mesh *mesh;
    char before_instance[0x14];
    void *mesh_instance;
} Actor;
extern void __fastcall bind_mesh(Mesh *, void *, Actor *);
extern Vector *__stdcall root_delta(void *, Vector *);

void __fastcall execGetRootLocationDelta(Actor *actor, void *unused, struct ArgumentFrame *frame, Vector *result)
{
    Mesh *mesh;
    Class *type;
    Vector delta;
    ++frame->code;
    mesh = actor->mesh;
    if (!mesh)
        return;
    for (type = mesh->type; type; type = type->parent) {
        if (type == (Class *)0x10cc0a78u) {
            bind_mesh(mesh, unused, actor);
            *result = *root_delta(actor->mesh_instance, &delta);
            return;
        }
    }
}
