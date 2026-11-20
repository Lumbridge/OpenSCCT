/* Single-word aggregate arguments are passed on the stack by x86 fastcall. */
struct Argument { unsigned value; };

extern unsigned __fastcall invoke_member(void *receiver, struct Argument first, struct Argument second);

unsigned __stdcall dispatch_member(void *receiver, struct Argument first, struct Argument second)
{
    return invoke_member(receiver, first, second);
}
