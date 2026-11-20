struct Object;
extern int __cdecl register_exit(void (__cdecl *)(void));

extern struct Object object_374e2c;
extern struct Object object_375054;
extern struct Object object_397760;
extern struct Object object_3a7780;
extern struct Object object_3a778c;
extern struct Object object_3a7798;
extern struct Object object_3a77a4;
extern struct Object object_3a77b0;
extern struct Object object_3a77bc;
extern struct Object object_3a77c8;
extern struct Object object_3a77d4;
extern struct Object object_3a77e0;
extern struct Object object_3b785c;
extern struct Object object_3bcdcc;
extern struct Object object_3c9e68;
extern struct Object object_3c9e8c;
extern void __fastcall construct_004630(struct Object *);
extern void __fastcall construct_22fdf0(struct Object *);
extern void __fastcall construct_283f0c(struct Object *);
extern void __cdecl cleanup_2dc6f0(void);
extern void __cdecl cleanup_2dc810(void);
extern void __cdecl cleanup_2dd420(void);
extern void __cdecl cleanup_2dd520(void);
extern void __cdecl cleanup_2dd540(void);
extern void __cdecl cleanup_2dd550(void);
extern void __cdecl cleanup_2dd560(void);
extern void __cdecl cleanup_2dd570(void);
extern void __cdecl cleanup_2dd580(void);
extern void __cdecl cleanup_2dd590(void);
extern void __cdecl cleanup_2dd5a0(void);
extern void __cdecl cleanup_2dd5b0(void);
extern void __cdecl cleanup_2dd5c0(void);
extern void __cdecl cleanup_2de470(void);
extern void __cdecl cleanup_2de490(void);
extern void __cdecl cleanup_2de4a0(void);

#define INITIALIZE_OBJECT(Entry, Object, Construct, Cleanup) \
    void __cdecl Entry(void) \
    { \
        Construct(&Object); \
        register_exit(Cleanup); \
    }

INITIALIZE_OBJECT(init_2d8700, object_3c9e8c, construct_004630, cleanup_2dc6f0)
INITIALIZE_OBJECT(init_2d8940, object_3c9e68, construct_004630, cleanup_2dc810)
INITIALIZE_OBJECT(init_2da230, object_3bcdcc, construct_004630, cleanup_2dd420)
INITIALIZE_OBJECT(init_2da410, object_3b785c, construct_004630, cleanup_2dd520)
INITIALIZE_OBJECT(init_2da470, object_3a7780, construct_004630, cleanup_2dd540)
INITIALIZE_OBJECT(init_2da490, object_3a778c, construct_004630, cleanup_2dd550)
INITIALIZE_OBJECT(init_2da4b0, object_3a7798, construct_004630, cleanup_2dd560)
INITIALIZE_OBJECT(init_2da4d0, object_3a77a4, construct_004630, cleanup_2dd570)
INITIALIZE_OBJECT(init_2da4f0, object_3a77b0, construct_004630, cleanup_2dd580)
INITIALIZE_OBJECT(init_2da510, object_3a77bc, construct_004630, cleanup_2dd590)
INITIALIZE_OBJECT(init_2da530, object_3a77c8, construct_004630, cleanup_2dd5a0)
INITIALIZE_OBJECT(init_2da550, object_3a77d4, construct_004630, cleanup_2dd5b0)
INITIALIZE_OBJECT(init_2da570, object_3a77e0, construct_004630, cleanup_2dd5c0)
INITIALIZE_OBJECT(init_2dc590, object_397760, construct_004630, cleanup_2de470)
INITIALIZE_OBJECT(init_2dc5d0, object_374e2c, construct_22fdf0, cleanup_2de490)
INITIALIZE_OBJECT(init_2dc5f0, object_375054, construct_283f0c, cleanup_2de4a0)
