#include "versus_script_frame.h"

struct LogFile;
struct LogMethods {
    void (__fastcall *destroy)(struct LogFile *, unsigned reserved_edx, unsigned release);
};
struct LogFile {
    const struct LogMethods *methods;
};
struct FileLog {
    unsigned char unknown[0x2f8];
    struct LogFile *file;
};
struct EventLogManager {
    unsigned char unknown[0x310];
    struct LogFile *file;
    unsigned flags;
};
void __fastcall versus_close_file_log(struct FileLog *self, unsigned reserved_edx,
                                      struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    if (self->file) {
        self->file->methods->destroy(self->file, reserved_edx, 1);
    }
    self->file = 0;
}
void __fastcall versus_close_event_log(struct EventLogManager *self, unsigned reserved_edx,
                                       struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    if (self->flags & 1) {
        if (self->file) {
            self->file->methods->destroy(self->file, reserved_edx, 1);
        }
        self->file = 0;
    }
}
