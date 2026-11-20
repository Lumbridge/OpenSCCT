struct Object { void *methods; unsigned short allocation_size; };
struct Manager;
struct Argument { void *ptr; };
union StackWord { unsigned value; float argument; };
struct Methods {
    void *reserved[4];
    struct Object *(__fastcall *allocate)(struct Manager *, float, float);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

extern struct Object *__fastcall construct_1bc710(struct Object *, struct Argument);
extern struct Object *__fastcall construct_4ff310(struct Object *, struct Argument);
extern struct Object *__fastcall construct_506c20(struct Object *, struct Argument);
extern struct Object *__fastcall construct_507130(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5079c0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_508b30(struct Object *, struct Argument);
extern struct Object *__fastcall construct_50a110(struct Object *, struct Argument);
extern struct Object *__fastcall construct_50b760(struct Object *, struct Argument);
extern struct Object *__fastcall construct_50cb30(struct Object *, struct Argument);
extern struct Object *__fastcall construct_50cf40(struct Object *, struct Argument);
extern struct Object *__fastcall construct_50ddd0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55a680(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55b580(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55b940(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55bc60(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55c130(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55c240(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55c400(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55c530(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55c720(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55d5d0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55df10(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55f2b0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_55f8c0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_560570(struct Object *, struct Argument);
extern struct Object *__fastcall construct_561350(struct Object *, struct Argument);
extern struct Object *__fastcall construct_561f30(struct Object *, struct Argument);
extern struct Object *__fastcall construct_562b00(struct Object *, struct Argument);
extern struct Object *__fastcall construct_56cf80(struct Object *, struct Argument);
extern struct Object *__fastcall construct_571600(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5719f0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_577300(struct Object *, struct Argument);
extern struct Object *__fastcall construct_57a780(struct Object *, struct Argument);
extern struct Object *__fastcall construct_57ade0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_57aed0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_57c030(struct Object *, struct Argument);
extern struct Object *__fastcall construct_57c500(struct Object *, struct Argument);
extern struct Object *__fastcall construct_59bd20(struct Object *, struct Argument);
extern struct Object *__fastcall construct_59c1d0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_59e280(struct Object *, struct Argument);
extern struct Object *__fastcall construct_59f350(struct Object *, struct Argument);
extern struct Object *__fastcall construct_59f6f0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a01a0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a0a90(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a1010(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a13b0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a1760(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a19e0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a4000(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a4100(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a42c0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a43f0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a4540(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a56e0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_5a5aa0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_6ffcf0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_700130(struct Object *, struct Argument);
extern struct Object *__fastcall construct_700990(struct Object *, struct Argument);
extern struct Object *__fastcall construct_701290(struct Object *, struct Argument);
extern struct Object *__fastcall construct_71c030(struct Object *, struct Argument);
extern struct Object *__fastcall construct_71caa0(struct Object *, struct Argument);
extern struct Object *__fastcall construct_71e330(struct Object *, struct Argument);

#define CREATE_OBJECT(Entry, Bytes, Category, Construct) \
    struct Object *__cdecl Entry(void *context) \
    { \
        static const union StackWord size = {Bytes}, category = {Category}; \
        struct Argument arg; \
        struct Object *object = manager->methods->allocate(manager, size.argument, category.argument); \
        object->allocation_size = Bytes; \
        arg.ptr = context; \
        return Construct(object, arg); \
    }

CREATE_OBJECT(create_1b6f50, 156, 34, construct_1bc710)
CREATE_OBJECT(create_502ca0, 40, 25, construct_4ff310)
CREATE_OBJECT(create_507250, 48, 34, construct_507130)
CREATE_OBJECT(create_50cfa0, 156, 34, construct_506c20)
CREATE_OBJECT(create_50f790, 20, 34, construct_5079c0)
CREATE_OBJECT(create_5125c0, 52, 34, construct_508b30)
CREATE_OBJECT(create_53be40, 32, 34, construct_50a110)
CREATE_OBJECT(create_53bfd0, 224, 34, construct_50b760)
CREATE_OBJECT(create_53c1d0, 48, 34, construct_50cb30)
CREATE_OBJECT(create_53c290, 16, 34, construct_50cf40)
CREATE_OBJECT(create_53c3f0, 56, 34, construct_50ddd0)
CREATE_OBJECT(create_55ada0, 160, 40, construct_55a680)
CREATE_OBJECT(create_55b0f0, 52, 39, construct_55b940)
CREATE_OBJECT(create_55b1c0, 44, 39, construct_55b580)
CREATE_OBJECT(create_55bdd0, 96, 39, construct_55bc60)
CREATE_OBJECT(create_55be90, 16, 39, construct_55c130)
CREATE_OBJECT(create_55bf40, 16, 39, construct_55c400)
CREATE_OBJECT(create_55bff0, 24, 39, construct_55c530)
CREATE_OBJECT(create_55c340, 24, 39, construct_55c240)
CREATE_OBJECT(create_55d1b0, 240, 39, construct_55c720)
CREATE_OBJECT(create_55dc90, 240, 39, construct_55d5d0)
CREATE_OBJECT(create_55e070, 80, 39, construct_55df10)
CREATE_OBJECT(create_55f3e0, 288, 39, construct_55f2b0)
CREATE_OBJECT(create_55ffd0, 224, 39, construct_55f8c0)
CREATE_OBJECT(create_561040, 224, 39, construct_560570)
CREATE_OBJECT(create_561ca0, 288, 39, construct_561350)
CREATE_OBJECT(create_562840, 224, 39, construct_561f30)
CREATE_OBJECT(create_562fc0, 160, 39, construct_562b00)
CREATE_OBJECT(create_56e640, 96, 38, construct_56cf80)
CREATE_OBJECT(create_571a80, 64, 36, construct_5719f0)
CREATE_OBJECT(create_5785e0, 64, 34, construct_577300)
CREATE_OBJECT(create_5786d0, 96, 36, construct_57a780)
CREATE_OBJECT(create_5787f0, 96, 36, construct_57ade0)
CREATE_OBJECT(create_5788d0, 64, 36, construct_57aed0)
CREATE_OBJECT(create_579510, 40, 34, construct_571600)
CREATE_OBJECT(create_579710, 156, 34, construct_57c030)
CREATE_OBJECT(create_5797b0, 40, 34, construct_57c500)
CREATE_OBJECT(create_5995e0, 76, 10, construct_59bd20)
CREATE_OBJECT(create_599700, 64, 10, construct_59c1d0)
CREATE_OBJECT(create_599800, 432, 10, construct_59e280)
CREATE_OBJECT(create_5999c0, 104, 10, construct_59f350)
CREATE_OBJECT(create_599bf0, 80, 10, construct_59f6f0)
CREATE_OBJECT(create_599cd0, 84, 10, construct_5a01a0)
CREATE_OBJECT(create_599ec0, 96, 10, construct_5a0a90)
CREATE_OBJECT(create_59a100, 60, 10, construct_5a1010)
CREATE_OBJECT(create_59a170, 56, 10, construct_5a13b0)
CREATE_OBJECT(create_59a400, 912, 10, construct_5a1760)
CREATE_OBJECT(create_59a620, 64, 10, construct_5a19e0)
CREATE_OBJECT(create_59f470, 64, 10, construct_5a4000)
CREATE_OBJECT(create_59f9a0, 48, 10, construct_5a4100)
CREATE_OBJECT(create_5a0370, 40, 10, construct_5a42c0)
CREATE_OBJECT(create_5a0bb0, 44, 10, construct_5a43f0)
CREATE_OBJECT(create_5a14a0, 32, 10, construct_5a4540)
CREATE_OBJECT(create_5a17e0, 912, 10, construct_5a56e0)
CREATE_OBJECT(create_5a1930, 64, 10, construct_5a5aa0)
CREATE_OBJECT(create_6fdd50, 144, 34, construct_6ffcf0)
CREATE_OBJECT(create_6fdea0, 160, 34, construct_700130)
CREATE_OBJECT(create_6fe190, 20, 34, construct_700990)
CREATE_OBJECT(create_6fe260, 64, 34, construct_701290)
CREATE_OBJECT(create_718ac0, 40, 39, construct_71e330)
CREATE_OBJECT(create_71d150, 144, 44, construct_71caa0)
CREATE_OBJECT(create_71eb20, 108, 39, construct_71c030)
