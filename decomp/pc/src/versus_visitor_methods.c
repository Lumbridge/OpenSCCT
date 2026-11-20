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

FORWARD(1)
FORWARD(2)
FORWARD(3)
FORWARD(4)
FORWARD(5)
FORWARD(6)
FORWARD(7)
FORWARD(8)
FORWARD(9)
FORWARD(10)
FORWARD(11)
FORWARD(12)
FORWARD(13)
FORWARD(14)
FORWARD(15)
FORWARD(16)
FORWARD(17)
FORWARD(18)
FORWARD(20)
FORWARD(21)
FORWARD(22)
FORWARD(23)
FORWARD(24)
FORWARD(25)
FORWARD(26)
FORWARD(27)
FORWARD(28)
FORWARD(29)
FORWARD(30)
FORWARD(31)
FORWARD(32)
FORWARD(33)
FORWARD(34)
FORWARD(36)
FORWARD(37)
