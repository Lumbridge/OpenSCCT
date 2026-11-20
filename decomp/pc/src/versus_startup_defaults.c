extern const float factor_2ff1a4;
extern const float factor_310a10;
extern float product_3b7858;
extern int __cdecl atexit(void (__cdecl *callback)(void));
extern unsigned char storage_3a7020[];
extern unsigned char storage_3cac00[];
extern unsigned char storage_3cac0c[];
extern unsigned char storage_3cac18[];
extern unsigned char storage_3cac24[];
extern unsigned char storage_3cac30[];
extern unsigned char storage_3cac5c[];
extern void *selected_3a7060;
extern void *selected_3cabf4;
extern void *selected_3cabf8;
extern void *selected_3cacb8;
extern void *selected_3cacbc;
extern void *selected_3cacc0;
extern void *selected_3cacc4;
extern void __cdecl finalize_2dc650(void);
extern void __cdecl finalize_2dd500(void);
extern void __cdecl finalize_2dd5d0(void);

void __cdecl initialize_2d8600(void)
{
    atexit(finalize_2dc650);
}

void __cdecl initialize_2d8640(void)
{
    selected_3cacc0 = storage_3cac00;
}

void __cdecl initialize_2d8650(void)
{
    selected_3cacc4 = storage_3cac0c;
}

void __cdecl initialize_2d8660(void)
{
    selected_3cacb8 = storage_3cac18;
}

void __cdecl initialize_2d8670(void)
{
    selected_3cacbc = storage_3cac24;
}

void __cdecl initialize_2d8680(void)
{
    selected_3cabf4 = storage_3cac5c;
}

void __cdecl initialize_2d8690(void)
{
    selected_3cabf8 = storage_3cac30;
}

void __cdecl initialize_2da3f0(void)
{
    atexit(finalize_2dd500);
}

void __cdecl initialize_2da430(void)
{
    product_3b7858 = factor_310a10 * factor_2ff1a4;
}

void __cdecl initialize_2da640(void)
{
    selected_3a7060 = storage_3a7020;
}

void __cdecl initialize_2da660(void)
{
    atexit(finalize_2dd5d0);
}
