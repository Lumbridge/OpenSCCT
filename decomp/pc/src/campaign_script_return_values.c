#include "campaign_script_events.h"

struct Vector { float x, y, z; };
typedef struct { struct Vector *value; } VectorDestination;

#define VECTOR_QUERY(Name) \
    extern ScriptEventName name_##Name; \
    struct Vector *__fastcall event_result_##Name(struct ScriptObject *object, VectorDestination destination) \
    { \
        struct { struct Vector result; } frame; \
        struct Vector zero = { 0.0f, 0.0f, 0.0f }; \
        ParameterReference address; \
        frame.result = zero; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
        *destination.value = frame.result; \
        return destination.value; \
    }

VECTOR_QUERY(GetBasePos)
VECTOR_QUERY(GetFireStart)
VECTOR_QUERY(GetSplashPos)
VECTOR_QUERY(GetTargetPosition)

typedef union { unsigned int number; float stack; } StackInteger;
struct WideString { unsigned short *data; int count, capacity; };
typedef struct { struct WideString *value; } StringPointer;
typedef struct { unsigned short *value; } WideBuffer;
typedef struct { const unsigned short *value; } AllocationTag;
typedef struct { int value; } ElementCount;
struct Allocator;
struct AllocatorMethods {
    void *allocate;
    unsigned short *(__fastcall *resize)(struct Allocator *, WideBuffer, float, AllocationTag);
    void (__fastcall *release)(struct Allocator *, WideBuffer);
};
struct Allocator { const struct AllocatorMethods *methods; };
extern struct Allocator *engine_allocator;
extern const unsigned short word_array_tag[];

void __fastcall copy_wide_string(struct WideString *to, StringPointer from);
void __fastcall remove_wide_elements(struct WideString *array, float start, ElementCount count, float element_size);

#define STRING_QUERY(Name) \
    extern ScriptEventName name_##Name; \
    struct WideString *__fastcall event_result_##Name(struct ScriptObject *object, StringPointer destination) \
    { \
        struct WideString result = { 0, 0, 0 }; \
        ParameterReference parms; \
        StringPointer source; \
        WideBuffer old; \
        AllocationTag tag = { word_array_tag }; \
        ElementCount count; \
        static const StackInteger element_size = { sizeof(unsigned short) }; \
 \
        parms.address = &result; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), parms, 0.0f); \
        source.value = &result; \
        copy_wide_string(destination.value, source); \
        result.capacity = result.count = 0; \
        old.value = result.data; \
        result.data = engine_allocator->methods->resize(engine_allocator, old, 0.0f, tag); \
        count.value = result.count; \
        remove_wide_elements(&result, 0.0f, count, element_size.stack); \
        if (result.data) { \
            old.value = result.data; \
            engine_allocator->methods->release(engine_allocator, old); \
        } \
        return destination.value; \
    }

STRING_QUERY(GetDescription)
STRING_QUERY(GetGameVariableValueStrings)
STRING_QUERY(GetLevelVariableValueStrings)
STRING_QUERY(GetVariableValueStrings)
