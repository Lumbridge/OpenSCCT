typedef unsigned long Word;
typedef long Result;
/* The float formal occupies a stack word; the retail signature consumes its raw bits. */
typedef union {
    Word value;
    float stack_word;
} StackWord;
typedef struct
{
    Word value;
} WordArg;
typedef struct
{
    Word *value;
} OutArg;
typedef struct Buffer Buffer;
typedef struct Spatial Spatial;
typedef struct Unknown Unknown;

/* Method positions follow IDirectSoundBuffer8, IDirectSound3DBuffer and IUnknown. */
typedef struct
{
    void *query_interface, *add_ref;
    Word(__stdcall *release)(Buffer *);
    void *get_caps;
    Result(__stdcall *get_position)(Buffer *, Word *, Word *);
    void *get_format, *get_volume, *get_pan, *get_frequency;
    Result(__stdcall *get_status)(Buffer *, Word *);
    void *initialize, *lock;
    Result(__stdcall *play)(Buffer *, Word, Word, Word);
    Result(__stdcall *set_position)(Buffer *, Word);
    void *set_format;
    Result(__stdcall *set_volume)(Buffer *, long);
    Result(__stdcall *set_pan)(Buffer *, long);
    Result(__stdcall *set_frequency)(Buffer *, Word);
    Result(__stdcall *stop)(Buffer *);
    void *unlock, *restore, *set_fx;
    Result(__stdcall *acquire_resources)(Buffer *, Word, Word, Word *);
} BufferMethods;
struct Buffer
{
    const BufferMethods *methods;
};
typedef struct
{
    void *query_interface, *add_ref;
    Word(__stdcall *release)(Spatial *);
    void *parameters[15];
    Result(__stdcall *set_mode)(Spatial *, Word, Word);
} SpatialMethods;
struct Spatial
{
    const SpatialMethods *methods;
};
typedef struct
{
    void *query_interface, *add_ref;
    Word(__stdcall *release)(Unknown *);
} UnknownMethods;
struct Unknown
{
    const UnknownMethods *methods;
};
typedef struct
{
    const void *vtable;
    Word played, has_spatial, spatial_enabled, field10;
    Buffer *buffer;
    Spatial *spatial;
    Word field1c, field20, field24, field28, field2c, field30, field34, field38, field3c;
    Unknown *other_interface;
    long timer;
    Word field48, field4c;
} SoundBuffer;
extern const char SoundBufferMethods[];
extern void __stdcall SND_fn_vDestroyTimer(long timer);
enum
{
    DS3D_NORMAL = 0,
    DS3D_DISABLE = 2,
    DS3D_DEFERRED = 1,
    DS_NO_VIRTUALIZATION = 0x0878000a
};
#define DSERR_INVALIDCALL ((Result)0x88780032)

#pragma code_seg(".text$V75e000")
SoundBuffer *__fastcall initialize_sound_buffer(SoundBuffer *self)
{
    self->vtable = SoundBufferMethods;
    self->played = 0;
    self->has_spatial = 0;
    self->spatial_enabled = 0;
    self->field10 = 0;
    self->buffer = 0;
    self->spatial = 0;
    self->other_interface = 0;
    self->field1c = 0;
    self->field20 = 0;
    self->field24 = 0;
    self->field2c = 0;
    self->field30 = 0;
    self->field34 = 0;
    self->field38 = 0;
    self->timer = -2;
    self->field48 = 0;
    self->field4c = 0;
    return self;
}

#pragma code_seg(".text$V75e050")
void __fastcall release_sound_buffer(SoundBuffer *self)
{
    self->vtable = SoundBufferMethods;
    if (self->spatial)
    {
        self->spatial->methods->release(self->spatial);
        self->spatial = 0;
    }
    if (self->buffer)
    {
        self->buffer->methods->release(self->buffer);
        self->buffer = 0;
    }
    if (self->other_interface)
    {
        self->other_interface->methods->release(self->other_interface);
        self->other_interface = 0;
    }
    if (self->timer != -2)
    {
        SND_fn_vDestroyTimer(self->timer);
        self->timer = -2;
    }
}

#pragma code_seg(".text$V75e0b0")
Word __fastcall buffer_spatial_mode(SoundBuffer *self)
{
    return self->spatial_enabled;
}

#pragma code_seg(".text$V75e0c0")
void __fastcall set_buffer_spatial_mode(SoundBuffer *self, float mode_word)
{
    Word enabled = ((const StackWord *)&mode_word)->value;
    if (self->spatial_enabled != enabled)
    {
        self->spatial_enabled = enabled;
        if (enabled)
            self->spatial->methods->set_mode(self->spatial, DS3D_NORMAL, DS3D_DEFERRED);
        else
            self->spatial->methods->set_mode(self->spatial, DS3D_DISABLE, DS3D_DEFERRED);
    }
}

#pragma code_seg(".text$V75e0f0")
Result __fastcall play_sound_buffer(SoundBuffer *self, WordArg reserved, WordArg priority,
                                    WordArg flags)
{
    Result hr;
    if (self->buffer)
    {
        hr = self->buffer->methods->play(self->buffer, reserved.value, priority.value, flags.value);
        if (hr == 0 || hr == DS_NO_VIRTUALIZATION)
            self->played = 1;
    }
    else
    {
        hr = DSERR_INVALIDCALL;
    }
    return hr;
}

#pragma code_seg(".text$V75e130")
Result __fastcall set_buffer_pan(SoundBuffer *self, WordArg value)
{
    return self->buffer->methods->set_pan(self->buffer, value.value);
}

#pragma code_seg(".text$V75e150")
Result __fastcall set_buffer_volume(SoundBuffer *self, WordArg value)
{
    return self->buffer->methods->set_volume(self->buffer, value.value);
}

#pragma code_seg(".text$V75e170")
Result __fastcall set_buffer_frequency(SoundBuffer *self, WordArg value)
{
    return self->buffer->methods->set_frequency(self->buffer, value.value);
}

#pragma code_seg(".text$V75e190")
Result __fastcall set_buffer_position(SoundBuffer *self, float position_word)
{
    Word position = ((const StackWord *)&position_word)->value;
    return self->buffer->methods->set_position(self->buffer, position);
}

#pragma code_seg(".text$V75e1b0")
Result __fastcall get_buffer_cursors(SoundBuffer *self, OutArg play, OutArg write)
{
    return self->buffer->methods->get_position(self->buffer, play.value, write.value);
}

#pragma code_seg(".text$V75e1d0")
Result __fastcall stop_sound_buffer(SoundBuffer *self)
{
    return self->buffer->methods->stop(self->buffer);
}

#pragma code_seg(".text$V75e1e0")
Result __fastcall acquire_buffer_resources(SoundBuffer *self, WordArg flags, WordArg count,
                                           OutArg result)
{
    return self->buffer->methods->acquire_resources(self->buffer, flags.value, count.value,
                                                    result.value);
}

#pragma code_seg(".text$V75e200")
Result __fastcall get_buffer_status(SoundBuffer *self, OutArg status)
{
    return self->buffer->methods->get_status(self->buffer, status.value);
}
