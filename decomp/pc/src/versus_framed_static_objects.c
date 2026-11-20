struct Object;
extern int __cdecl atexit(void (__cdecl *)(void));
extern struct Object object_3be700;
extern struct Object object_3be818;
extern struct Object object_3be930;
extern void __fastcall construct_00cfa0(struct Object *, float);
void __cdecl finalize_2dd270(void);
void __cdecl finalize_2dd280(void);
void __cdecl finalize_2dd290(void);

void __cdecl initialize_2d9ed0(void)
{
    construct_00cfa0(&object_3be700, 0.0f);
    atexit(finalize_2dd270);
}

void __cdecl initialize_2d9ef0(void)
{
    construct_00cfa0(&object_3be818, 0.0f);
    atexit(finalize_2dd280);
}

void __cdecl initialize_2d9f10(void)
{
    construct_00cfa0(&object_3be930, 0.0f);
    atexit(finalize_2dd290);
}
