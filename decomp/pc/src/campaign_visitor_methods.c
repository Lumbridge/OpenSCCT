struct Receiver;
struct Visitor;
struct ReceiverRef { struct Receiver *receiver; };
struct VisitorRef { struct Visitor *visitor; };
typedef unsigned (__fastcall *Visit)(struct Visitor *, struct ReceiverRef);
struct Visitor { Visit *methods; };

#define FORWARD(Slot) \
    unsigned __fastcall forward_##Slot(struct Receiver *receiver, struct VisitorRef target) \
    { \
        struct ReceiverRef argument; \
        argument.receiver = receiver; \
        return target.visitor->methods[Slot](target.visitor, argument); \
    }

FORWARD(19)
FORWARD(35)
FORWARD(38)
FORWARD(39)
