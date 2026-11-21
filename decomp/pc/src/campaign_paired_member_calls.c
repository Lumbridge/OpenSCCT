struct Receiver;
extern void __fastcall first_method(struct Receiver *);
extern unsigned __fastcall final_method(struct Receiver *);

unsigned __fastcall call_pair(struct Receiver *receiver)
{
    first_method(receiver);
    return final_method(receiver);
}
