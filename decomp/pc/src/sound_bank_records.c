typedef struct SoundBankManager SoundBankManager;
typedef struct SoundBank {
    unsigned char header[0x34];
    unsigned char *events;
    unsigned char *resources;
} SoundBank;
SoundBank *__fastcall sound_current_bank(SoundBankManager *manager);
#define BANK_MANAGER (*(SoundBankManager **)0x1132e934)
#define RECORD_SIZE 0x68

void *__stdcall sound_bin_res_mode0(unsigned long index)
{
    SoundBank *bank;
    if (BANK_MANAGER) {
        bank = sound_current_bank(BANK_MANAGER);
        if (bank && bank->resources)
            return bank->resources + index * RECORD_SIZE;
    }
    return 0;
}

void *__cdecl sound_bin_event_mode0(unsigned long index)
{
    SoundBank *bank;
    if (BANK_MANAGER) {
        bank = sound_current_bank(BANK_MANAGER);
        if (bank && bank->events)
            return bank->events + index * RECORD_SIZE;
    }
    return 0;
}
