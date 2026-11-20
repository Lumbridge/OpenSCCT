/* The retail methods take the object in ECX. For methods with stack
 * arguments, reserving the second fastcall position leaves those arguments
 * on the stack. The dispatch functions pass that unused register through. */
struct VersusData;
struct TextBuffer;

typedef void (__fastcall *IndentedOperation)(
    const struct VersusData *value, unsigned reserved_edx,
    struct TextBuffer *output, unsigned indent);
typedef void (__fastcall *AppendOperation)(
    const struct VersusData *value, unsigned reserved_edx, struct TextBuffer *output);
typedef void (__fastcall *PrintOperation)(const struct VersusData *value);
typedef unsigned (__fastcall *SizeOperation)(const struct VersusData *value);

struct DataMethods {
    void *destructor;
    IndentedOperation format_indented;
    IndentedOperation format_slot_2;
    AppendOperation append_text;
    PrintOperation print;
    SizeOperation byte_size;
};

struct VersusData {
    const struct DataMethods *methods;
    unsigned char unknown[8];
};

struct DataReference {
    struct VersusData base;
    const struct VersusData *target;
};

void __fastcall versus_data_format_via_slot_1(
    const struct VersusData *value, unsigned reserved_edx,
    struct TextBuffer *output, unsigned indent) {
    value->methods->format_indented(value, reserved_edx, output, indent);
}

void __fastcall versus_ref_format_indented(
    const struct DataReference *ref, unsigned reserved_edx,
    struct TextBuffer *output, unsigned indent) {
    const struct VersusData *value = ref->target;
    value->methods->format_indented(value, reserved_edx, output, indent);
}

void __fastcall versus_ref_format_slot_2(
    const struct DataReference *ref, unsigned reserved_edx,
    struct TextBuffer *output, unsigned indent) {
    const struct VersusData *value = ref->target;
    value->methods->format_slot_2(value, reserved_edx, output, indent);
}

void __fastcall versus_ref_append_text(
    const struct DataReference *ref, unsigned reserved_edx, struct TextBuffer *output) {
    const struct VersusData *value = ref->target;
    value->methods->append_text(value, reserved_edx, output);
}

void __fastcall versus_ref_print(const struct DataReference *ref) {
    const struct VersusData *value = ref->target;
    value->methods->print(value);
}

unsigned __fastcall versus_ref_data_size(const struct DataReference *ref) {
    const struct VersusData *value = ref->target;
    return value->methods->byte_size(value);
}
