/* x86 Win32 ABI used by the campaign's audio compatibility queries. */
typedef unsigned long Dword;
typedef unsigned short Word;

struct VersionInfoA {
    Dword size;
    Dword major;
    Dword minor;
    Dword build;
    Dword platform;
    char service_pack[128];
};

struct WaveOutputCapsA {
    Word manufacturer;
    Word product;
    Dword driver_version;
    char product_name[32];
    Dword formats;
    Word channels;
    Word reserved;
    Dword support;
};

struct WaveFormat {
    Word format_tag;
    Word channels;
    Dword samples_per_second;
    Dword average_bytes_per_second;
    Word block_alignment;
    Word bits_per_sample;
};

__declspec(dllimport) int __stdcall GetVersionExA(struct VersionInfoA *info);
__declspec(dllimport) unsigned __stdcall waveOutGetDevCapsA(
    unsigned device, struct WaveOutputCapsA *caps, unsigned size);
extern void __stdcall SND_fn_vMemsetSnd(void *dest, int fill, unsigned size);

int __stdcall SND_fn_bTestSnd_Win32(void)
{
    struct VersionInfoA version;
    SND_fn_vMemsetSnd(&version, 0, sizeof(version));
    version.size = sizeof(version);
    GetVersionExA(&version);
    return version.platform != 0;
}

int __stdcall SND_fn_bTestSnd_WinNT4(void)
{
    struct VersionInfoA version;
    SND_fn_vMemsetSnd(&version, 0, sizeof(version));
    version.size = sizeof(version);
    GetVersionExA(&version);
    return version.platform == 2 && version.major == 4;
}

int __stdcall SND_fn_bIsScriptModeUsed(void)
{
    return 0;
}

int __stdcall SND_fn_bTestSnd_WinMM(const struct WaveFormat *format)
{
    struct WaveOutputCapsA caps;
    Dword flags[12] = {
        1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048
    };
    unsigned index;
    if (waveOutGetDevCapsA((unsigned)-1, &caps, sizeof(caps)) == 0) {
        if (format->samples_per_second <= 11025)
            index = 0;
        else if (format->samples_per_second <= 22050)
            index = 4;
        else
            index = 8;
        index += (format->bits_per_sample / 8 - 1) * 2 + format->channels - 1;
        if (caps.formats & flags[index])
            return 1;
    }
    return 0;
}
