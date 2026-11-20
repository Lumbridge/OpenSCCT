struct Controller;
struct Index { int value; };
extern void __fastcall refresh_item(struct Controller *, struct Index, float);
extern void __fastcall refresh_minus_two(struct Controller *);
extern void __fastcall refresh_minus_three(struct Controller *);

void __fastcall refresh_index(struct Controller *controller, struct Index index)
{
    if (index.value >= 0)
        refresh_item(controller, index, 0.0f);
    else if (index.value == -2)
        refresh_minus_two(controller);
    else if (index.value == -3)
        refresh_minus_three(controller);
}
