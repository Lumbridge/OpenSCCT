struct SoundBank;
typedef struct SoundBankManager {
    unsigned char reserved[12];
    struct SoundBank *current;
} SoundBankManager;

struct SoundBank *__fastcall sound_current_bank(SoundBankManager *manager)
{
    return manager->current;
}
