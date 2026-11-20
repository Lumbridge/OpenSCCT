struct Collector { const void *methods; float distance; void *output; };
struct Arg { void *value; };
struct CollectorArg { struct Collector *value; };

extern const void *const methods_84aa78[];
extern void __fastcall query_5176a0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_517a00(void *, void *, void *, struct Collector *);
extern void __fastcall query_518f40(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_519420(void *, void *, void *, struct Collector *);
extern void __fastcall query_520290(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_521520(void *, void *, void *, struct Collector *);
extern void __fastcall query_529310(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_529740(void *, void *, void *, struct Collector *);
extern void __fastcall query_52a900(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_52ab60(void *, void *, void *, struct Collector *);
extern void __fastcall query_52beb0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_52c230(void *, void *, void *, struct Collector *);
extern void __cdecl query_52fc50(void *, void *, void *, struct Collector *);
extern void __fastcall query_531910(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __fastcall query_5329c0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_532a60(void *, void *, void *, struct Collector *);
extern void __fastcall query_5337f0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_5338d0(void *, void *, void *, struct Collector *);
extern void __fastcall query_5374b0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_5375c0(void *, void *, void *, struct Collector *);
extern void __fastcall query_539030(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_539190(void *, void *, void *, struct Collector *);
extern void __fastcall query_53a7c0(void *, struct Arg, struct Arg, struct Arg, struct CollectorArg);
extern void __cdecl query_53a930(void *, void *, void *, struct Collector *);
extern void __cdecl query_53b3b0(void *, void *, void *, struct Collector *);

#define RUN_QUERY(Entry, Query, Methods, Left, Right) \
    void __cdecl Entry(void *first, void *second, void *input, void *output) \
    { \
        struct Collector collector; \
        collector.output = output; \
        collector.distance = 3.402823466e38F; \
        collector.methods = Methods; \
        Query(Left, Right, input, &collector); \
    }

#define RUN_MEMBER(Entry, Query, Methods, Left, Right) \
    void __fastcall Entry(void *self, struct Arg first, struct Arg second, struct Arg input, struct Arg output) \
    { \
        struct Collector collector; \
        struct CollectorArg arg; \
        collector.output = output.value; \
        collector.distance = 3.402823466e38F; \
        collector.methods = Methods; \
        arg.value = &collector; \
        Query(self, Left, Right, input, arg); \
    }

RUN_QUERY(collect_517de0_1, query_517a00, methods_84aa78, second, first)
RUN_MEMBER(collect_518470_1, query_5176a0, methods_84aa78, second, first)
RUN_MEMBER(collect_51a180_1, query_518f40, methods_84aa78, second, first)
RUN_QUERY(collect_51a1c0_1, query_519420, methods_84aa78, second, first)
RUN_MEMBER(collect_524b10_1, query_520290, methods_84aa78, second, first)
RUN_QUERY(collect_524b50_1, query_521520, methods_84aa78, second, first)
RUN_MEMBER(collect_529e00_1, query_529310, methods_84aa78, second, first)
RUN_QUERY(collect_529e40_1, query_529740, methods_84aa78, second, first)
RUN_MEMBER(collect_52b0b0_1, query_52a900, methods_84aa78, second, first)
RUN_QUERY(collect_52b0f0_1, query_52ab60, methods_84aa78, second, first)
RUN_MEMBER(collect_52c7f0_1, query_52beb0, methods_84aa78, second, first)
RUN_QUERY(collect_52c830_1, query_52c230, methods_84aa78, second, first)
RUN_QUERY(collect_530960_1, query_52fc50, methods_84aa78, second, first)
RUN_QUERY(collect_531bc0_1, query_5338d0, methods_84aa78, second, first)
RUN_MEMBER(collect_532030_1, query_531910, methods_84aa78, second, first)
RUN_QUERY(collect_532d40_1, query_532a60, methods_84aa78, second, first)
RUN_MEMBER(collect_5331f0_1, query_5329c0, methods_84aa78, second, first)
RUN_MEMBER(collect_5342d0_1, query_5337f0, methods_84aa78, second, first)
RUN_QUERY(collect_534730_1, query_5375c0, methods_84aa78, second, first)
RUN_MEMBER(collect_534a70_1, query_5374b0, methods_84aa78, second, first)
RUN_MEMBER(collect_537e60_1, query_5374b0, methods_84aa78, second, first)
RUN_QUERY(collect_537ea0_1, query_5375c0, methods_84aa78, second, first)
RUN_QUERY(collect_539970_1, query_539190, methods_84aa78, second, first)
RUN_MEMBER(collect_539df0_1, query_539030, methods_84aa78, second, first)
RUN_QUERY(collect_53ac90_1, query_53a930, methods_84aa78, second, first)
RUN_MEMBER(collect_53ad50_1, query_53a7c0, methods_84aa78, second, first)
RUN_QUERY(collect_53b5f0_1, query_53b3b0, methods_84aa78, second, first)
