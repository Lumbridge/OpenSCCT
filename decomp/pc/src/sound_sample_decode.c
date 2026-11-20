typedef unsigned Word;
typedef struct {
    Word id, type, field08, data_offset, alternate_offset, field14, field18, data_size;
    Word field20, field24, field28, use_external, field30, field34, field38;
    Word external_field, external_offset;
} SerializedSample;
typedef struct {
    Word id, type, field08, field0c, field10, data_size, field18, field1c;
    Word field20, use_external, payload_address, external_field;
    Word field30, field34, field38, data_address, buffer_size;
} DecodedSample;
extern void __stdcall EnterCriticalSectionTool_(void);
extern void __stdcall QuitCriticalSectionTool(void);
extern void __stdcall MouchardThreadTool(const char *site, int line);
extern const char MixerLockSite[];

#pragma code_seg(".text$V758280")
__declspec(noinline) void __cdecl decode_sample_fields(const SerializedSample *src, DecodedSample *dst,
                                                       Word internal_base, Word external_base)
{
    dst->id = src->id;
    dst->type = src->type;
    dst->field08 = src->field14;
    dst->field10 = src->field18;
    dst->data_size = src->data_size;
    dst->field18 = src->field20;
    dst->field1c = src->field24;
    dst->field20 = src->field28;
    dst->field34 = src->field34;
    dst->field38 = src->field38;
    dst->use_external = src->use_external;
    dst->field30 = src->field30;
    if (src->use_external) {
        dst->payload_address = src->external_offset + external_base;
        dst->external_field = src->external_field;
    } else {
        dst->payload_address = src->alternate_offset + internal_base;
    }
}
#pragma code_seg(".text$V7591e0")
int __cdecl decode_sound_sample(const SerializedSample *src, DecodedSample *dst,
                               Word internal_base, Word external_base)
{
    EnterCriticalSectionTool_();
    MouchardThreadTool(MixerLockSite, 0xd1d);
    dst->data_address = src->data_offset + external_base;
    dst->buffer_size = src->data_size;
    decode_sample_fields(src, dst, internal_base, external_base);
    QuitCriticalSectionTool();
    return 1;
}
