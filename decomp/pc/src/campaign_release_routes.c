struct buffer_argument { void *value; };
struct engine_allocator;
struct allocator_methods {
    void *reserved[2];
    void (__fastcall *release)(struct engine_allocator *, struct buffer_argument);
};
struct engine_allocator { const struct allocator_methods *methods; };
extern struct engine_allocator *array_allocator;

extern void __stdcall release_73c870(void *pointer);

void __cdecl release_route_0014a0(void *pointer)
{
    struct buffer_argument storage;
    storage.value = pointer;
    array_allocator->methods->release(array_allocator, storage);
}

void __cdecl release_route_0014e0(void *pointer)
{
    struct buffer_argument storage;
    storage.value = pointer;
    array_allocator->methods->release(array_allocator, storage);
}

void __cdecl release_route_469670(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_747ae0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75ae00(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75c0f0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75cbc0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75d300(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75eac0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_75ee20(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_760fe0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_7653d0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_765e50(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_767150(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_767a10(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_76f7b0(void *pointer)
{
    release_73c870(pointer);
}

void __cdecl release_route_77c720(void *pointer)
{
    release_73c870(pointer);
}
