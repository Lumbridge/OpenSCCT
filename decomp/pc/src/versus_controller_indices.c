struct Controller {
    unsigned prefix[22];
    int first_index, second_index;
};

struct Index { int value; };
extern void __fastcall refresh_index(struct Controller *, struct Index);

#define SET_INDEX(Name) \
    void __fastcall set_##Name(struct Controller *controller, struct Index next) \
    { \
        struct Index previous; \
        previous.value = controller->Name; \
        controller->Name = next.value; \
        if (next.value != previous.value) { \
            refresh_index(controller, previous); \
            next.value = controller->Name; \
            refresh_index(controller, next); \
        } \
    }

SET_INDEX(first_index)
SET_INDEX(second_index)
