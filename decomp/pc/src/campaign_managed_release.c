struct AllocationManager;
extern struct AllocationManager *__cdecl get_manager(void);
extern void __cdecl release_allocation(struct AllocationManager *manager, void *pointer, unsigned category);

void __cdecl release_managed_487b90(void *pointer)
{
    release_allocation(get_manager(), pointer, 3);
}

void __cdecl release_managed_487bb0(void *pointer)
{
    release_allocation(get_manager(), pointer, 4);
}
