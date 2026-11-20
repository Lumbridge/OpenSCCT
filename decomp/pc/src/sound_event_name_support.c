#define EVENT_NAMES_ENABLED (*(unsigned long *)0x1132c7e8)

void __cdecl sound_set_event_name_support(unsigned long setting)
{
    EVENT_NAMES_ENABLED = setting;
}

unsigned long __cdecl sound_get_event_name_support(void)
{
    return EVENT_NAMES_ENABLED;
}
