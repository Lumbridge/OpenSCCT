struct AudioSubsystem;
typedef struct { int value; } AudioArgument;
typedef void (__fastcall *AudioAction)(struct AudioSubsystem *);
struct AudioMethods {
    void *before_reset[0xa0 / 4];
    AudioAction reset;
    void *before_stop[(0xd4 - 0xa4) / 4];
    AudioAction stop;
    AudioAction restart;
};
struct VoiceActor {
    unsigned char prefix[0x2d4];
    unsigned int flags;
};
struct VoiceLevel {
    unsigned char prefix[0xad4];
    struct VoiceActor *first;
    struct VoiceActor *second;
};
struct AudioSubsystem {
    const struct AudioMethods *methods;
    unsigned char prefix[0xcc - 4];
    struct VoiceLevel *level;
    unsigned char settings[0x1a8 - 0xd0];
    int original_voices;
};
struct VoiceControl;
struct ControlMethods {
    void *prefix[0xb8/4];
    void (__fastcall *refresh)(struct VoiceControl *, const struct ControlMethods *, int);
};
struct VoiceControl { const struct ControlMethods *methods; };
struct VoiceClient {
    unsigned char prefix[0x6c];
    struct VoiceControl *control;
};
extern struct VoiceClient *voice_client;
extern const unsigned short *original_voice_language;
const unsigned short *__cdecl game_language(void);
const char *__cdecl ansi_language(const unsigned short *);
void __stdcall SND_fn_vSetCurrentLanguage(const char *);

void __fastcall play_original_voices(struct AudioSubsystem *audio, AudioArgument enabled, AudioArgument skip_refresh)
{
    if (enabled.value != audio->original_voices) {
        const unsigned short *language;
        audio->original_voices = enabled.value;
        audio->methods->stop(audio);
        audio->methods->reset(audio);
        language = audio->original_voices ? original_voice_language : game_language();
        SND_fn_vSetCurrentLanguage(ansi_language(language));
        audio->methods->restart(audio);
        if (audio->level && audio->level->first)
            audio->level->first->flags &= ~1u;
        if (audio->level && audio->level->second)
            audio->level->second->flags &= ~1u;
        if (!skip_refresh.value) {
            struct VoiceControl *control = voice_client->control;
            control->methods->refresh(control, control->methods, 1);
        }
    }
}
