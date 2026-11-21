struct Property { unsigned value; };
struct Target;
typedef unsigned (__fastcall *Setter)(struct Target *, struct Property);
struct Target { Setter *methods; };

#define FORWARD_PROPERTY(Field, TargetOffset, Slot) \
    unsigned __fastcall set_##Field##_##TargetOffset##_##Slot(unsigned char *receiver, struct Property property) \
    { \
        struct Target *target; \
        *(struct Property *)(receiver + Field) = property; \
        target = *(struct Target **)(receiver + TargetOffset); \
        return target->methods[Slot](target, property); \
    }

FORWARD_PROPERTY(80, 420, 68)
FORWARD_PROPERTY(84, 420, 58)
FORWARD_PROPERTY(280, 420, 62)
