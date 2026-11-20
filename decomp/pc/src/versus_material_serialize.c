struct Material;
struct Archive {
    unsigned char before_mode[0x14];
    int mode;
};
struct ArchiveArg { struct Archive *archive; };
struct Sequence {
    unsigned char before_selected[0x4c];
    struct Material *selected;
};
#define SERIALIZE_FLAGS (*(const unsigned *)0x10c968ecu)
extern void __fastcall versus_object_serialize(void *self, struct ArchiveArg archive);
void __fastcall versus_material_serialize(void *self, struct ArchiveArg archive) {
    if (!(SERIALIZE_FLAGS & 8)) {
        versus_object_serialize(self, archive);
    }
}
void __fastcall versus_sequence_serialize(struct Sequence *self, struct ArchiveArg archive) {
    if (archive.archive->mode) {
        self->selected = 0;
    }
    if (!(SERIALIZE_FLAGS & 8)) {
        versus_object_serialize(self, archive);
    }
}
