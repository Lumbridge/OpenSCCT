#include "scct_reverb_dsp.h"

#include <AL/al.h>
#include <AL/alc.h>
#include <AL/alext.h>
#include <AL/efx.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>

namespace {
thread_local std::string create_error;
constexpr int chunk_frames = 1024;

template<class T> T alc_proc(ALCdevice *device, const char *name) {
    auto function = reinterpret_cast<T>(alcGetProcAddress(device, name));
    if (!function) throw std::runtime_error(std::string("Missing OpenAL extension: ") + name);
    return function;
}
template<class T> T al_proc(const char *name) {
    auto function = reinterpret_cast<T>(alGetProcAddress(name));
    if (!function) throw std::runtime_error(std::string("Missing OpenAL extension: ") + name);
    return function;
}

struct ContextScope {
    PFNALCSETTHREADCONTEXTPROC set;
    ALCcontext *previous;
    ContextScope(ALCcontext *context, PFNALCSETTHREADCONTEXTPROC setter, PFNALCGETTHREADCONTEXTPROC getter)
        : set(setter), previous(getter()) {
        if (!set(context)) throw std::runtime_error("Could not select OpenAL thread context");
    }
    ~ContextScope() { set(previous); }
};

struct Reverb {
    ALCdevice *device{};
    ALCcontext *context{};
    PFNALCSETTHREADCONTEXTPROC set_context{};
    PFNALCGETTHREADCONTEXTPROC get_context{};
    LPALCRENDERSAMPLESSOFT render{};
    LPALBUFFERCALLBACKSOFT buffer_callback{};
    LPALGENEFFECTS gen_effects{};
    LPALDELETEEFFECTS delete_effects{};
    LPALEFFECTI effect_i{};
    LPALEFFECTF effect_f{};
    LPALEFFECTFV effect_fv{};
    LPALGENAUXILIARYEFFECTSLOTS gen_slots{};
    LPALDELETEAUXILIARYEFFECTSLOTS delete_slots{};
    LPALAUXILIARYEFFECTSLOTI slot_i{};
    LPALAUXILIARYEFFECTSLOTF slot_f{};
    LPALGENFILTERS gen_filters{};
    LPALDELETEFILTERS delete_filters{};
    LPALFILTERI filter_i{};
    LPALFILTERF filter_f{};
    ALuint source{}, buffer{}, effect{}, slot{}, dry_mute{};
    int rate{};
    const float *input{};
    int available{}, consumed{};
    bool callback_overread{};
    bool configured{};
    std::string error;
    std::array<float, chunk_frames * 2> input_copy{};

    static ALsizei AL_APIENTRY callback(void *opaque, void *destination, ALsizei bytes) noexcept {
        auto &self = *static_cast<Reverb *>(opaque);
        const int frames = bytes / static_cast<int>(sizeof(float));
        const int count = std::min(frames, self.available - self.consumed);
        auto *mono = static_cast<float *>(destination);
        for (int i = 0; i < count; ++i) {
            const int frame = self.consumed + i;
            mono[i] = self.input ? (self.input[frame * 2] + self.input[frame * 2 + 1]) * 0.5f : 0.0f;
        }
        if (frames > count) {
            self.callback_overread = true;
            std::memset(mono + count, 0, (frames - count) * sizeof(float));
        }
        self.consumed += count;
        return bytes;
    }

    explicit Reverb(int sample_rate) : rate(sample_rate) {
        if (rate < 8000 || rate > 192000) throw std::runtime_error("Unsupported reverb sample rate");
        auto open = alc_proc<LPALCLOOPBACKOPENDEVICESOFT>(nullptr, "alcLoopbackOpenDeviceSOFT");
        set_context = alc_proc<PFNALCSETTHREADCONTEXTPROC>(nullptr, "alcSetThreadContext");
        get_context = alc_proc<PFNALCGETTHREADCONTEXTPROC>(nullptr, "alcGetThreadContext");
        device = open(nullptr);
        if (!device) throw std::runtime_error("Could not open OpenAL loopback renderer");
        try {
            const ALCint attributes[] = {
                ALC_FORMAT_CHANNELS_SOFT, ALC_STEREO_SOFT, ALC_FORMAT_TYPE_SOFT, ALC_FLOAT_SOFT,
                ALC_FREQUENCY, rate, ALC_MAX_AUXILIARY_SENDS, 1,
                ALC_HRTF_SOFT, ALC_FALSE, ALC_OUTPUT_LIMITER_SOFT, ALC_FALSE,
                ALC_OUTPUT_MODE_SOFT, ALC_STEREO_BASIC_SOFT, 0
            };
            context = alcCreateContext(device, attributes);
            if (!context) throw std::runtime_error("Could not create stereo float OpenAL loopback context");
            ContextScope scope(context, set_context, get_context);
            render = alc_proc<LPALCRENDERSAMPLESSOFT>(device, "alcRenderSamplesSOFT");
            buffer_callback = al_proc<LPALBUFFERCALLBACKSOFT>("alBufferCallbackSOFT");
            gen_effects = al_proc<LPALGENEFFECTS>("alGenEffects");
            delete_effects = al_proc<LPALDELETEEFFECTS>("alDeleteEffects");
            effect_i = al_proc<LPALEFFECTI>("alEffecti");
            effect_f = al_proc<LPALEFFECTF>("alEffectf");
            effect_fv = al_proc<LPALEFFECTFV>("alEffectfv");
            gen_slots = al_proc<LPALGENAUXILIARYEFFECTSLOTS>("alGenAuxiliaryEffectSlots");
            delete_slots = al_proc<LPALDELETEAUXILIARYEFFECTSLOTS>("alDeleteAuxiliaryEffectSlots");
            slot_i = al_proc<LPALAUXILIARYEFFECTSLOTI>("alAuxiliaryEffectSloti");
            slot_f = al_proc<LPALAUXILIARYEFFECTSLOTF>("alAuxiliaryEffectSlotf");
            gen_filters = al_proc<LPALGENFILTERS>("alGenFilters");
            delete_filters = al_proc<LPALDELETEFILTERS>("alDeleteFilters");
            filter_i = al_proc<LPALFILTERI>("alFilteri");
            filter_f = al_proc<LPALFILTERF>("alFilterf");
            alDistanceModel(AL_NONE);
            alListenerf(AL_GAIN, 1.0f);
            alGenSources(1, &source);
            alGenBuffers(1, &buffer);
            gen_effects(1, &effect);
            effect_i(effect, AL_EFFECT_TYPE, AL_EFFECT_EAXREVERB);
            gen_slots(1, &slot);
            slot_f(slot, AL_EFFECTSLOT_GAIN, 1.0f);
            slot_i(slot, AL_EFFECTSLOT_AUXILIARY_SEND_AUTO, AL_FALSE);
            gen_filters(1, &dry_mute);
            filter_i(dry_mute, AL_FILTER_TYPE, AL_FILTER_LOWPASS);
            filter_f(dry_mute, AL_LOWPASS_GAIN, 0.0f);
            filter_f(dry_mute, AL_LOWPASS_GAINHF, 1.0f);
            alSourcei(source, AL_SOURCE_RELATIVE, AL_TRUE);
            alSourcef(source, AL_GAIN, 1.0f);
            alSourcef(source, AL_ROLLOFF_FACTOR, 0.0f);
            alSourcef(source, AL_ROOM_ROLLOFF_FACTOR, 0.0f);
            alSourcef(source, AL_AIR_ABSORPTION_FACTOR, 0.0f);
            alSourcei(source, AL_SOURCE_SPATIALIZE_SOFT, AL_FALSE);
            alSourcei(source, AL_DIRECT_FILTER, static_cast<ALint>(dry_mute));
            alSourcei(source, AL_DIRECT_FILTER_GAINHF_AUTO, AL_FALSE);
            alSourcei(source, AL_AUXILIARY_SEND_FILTER_GAIN_AUTO, AL_FALSE);
            alSourcei(source, AL_AUXILIARY_SEND_FILTER_GAINHF_AUTO, AL_FALSE);
            alSource3i(source, AL_AUXILIARY_SEND_FILTER, static_cast<ALint>(slot), 0, AL_FILTER_NULL);
            buffer_callback(buffer, AL_FORMAT_MONO_FLOAT32, rate, callback, this);
            alSourcei(source, AL_BUFFER, static_cast<ALint>(buffer));
            slot_i(slot, AL_EFFECTSLOT_EFFECT, static_cast<ALint>(effect));
            alSourcePlay(source);
            check_al("Initialize EAX reverb");
            // Settle the provider's initial source-gain ramp using silence only;
            // otherwise an impulse at the first requested frame can be faded out.
            std::array<float, chunk_frames * 2> silent_output{};
            if (!process(nullptr, silent_output.data(), chunk_frames)) throw std::runtime_error(error);
        } catch (...) { close(); throw; }
    }

    ~Reverb() { close(); }
    void check_al(const char *operation) {
        if (alGetError() != AL_NO_ERROR) throw std::runtime_error(operation);
    }
    void close() noexcept {
        if (context && set_context && get_context) {
            ALCcontext *previous = get_context();
            set_context(context);
            if (source) alDeleteSources(1, &source);
            if (buffer) alDeleteBuffers(1, &buffer);
            if (slot && delete_slots) delete_slots(1, &slot);
            if (effect && delete_effects) delete_effects(1, &effect);
            if (dry_mute && delete_filters) delete_filters(1, &dry_mute);
            set_context(previous == context ? nullptr : previous);
            alcDestroyContext(context);
            context = nullptr;
        }
        if (device) { alcCloseDevice(device); device = nullptr; }
    }

    void configure(const scct_reverb_environment &p) {
        // Reject the whole update before mutating any provider property.
        if (!scct_reverb_validate_environment(&p)) throw std::runtime_error("EAX parameter outside native range");
        effect_f(effect, AL_EAXREVERB_DENSITY, p.density);
        effect_f(effect, AL_EAXREVERB_DIFFUSION, p.diffusion);
        effect_f(effect, AL_EAXREVERB_GAIN, p.gain);
        effect_f(effect, AL_EAXREVERB_GAINHF, p.gain_hf);
        effect_f(effect, AL_EAXREVERB_GAINLF, p.gain_lf);
        effect_f(effect, AL_EAXREVERB_DECAY_TIME, p.decay_time);
        effect_f(effect, AL_EAXREVERB_DECAY_HFRATIO, p.decay_hf_ratio);
        effect_f(effect, AL_EAXREVERB_DECAY_LFRATIO, p.decay_lf_ratio);
        // The EFX API rounds these two limits (3.16 versus 10^(1000/2000),
        // .892 versus 10^(-100/2000)); valid EAX endpoints otherwise fail the
        // entire zone update. Preserve the EAX range and bound only its small
        // unrepresentable edge when passing through the public EFX interface.
        effect_f(effect, AL_EAXREVERB_REFLECTIONS_GAIN,
            std::min(p.reflections_gain, AL_EAXREVERB_MAX_REFLECTIONS_GAIN));
        effect_f(effect, AL_EAXREVERB_REFLECTIONS_DELAY, p.reflections_delay);
        effect_fv(effect, AL_EAXREVERB_REFLECTIONS_PAN, p.reflections_pan);
        effect_f(effect, AL_EAXREVERB_LATE_REVERB_GAIN, p.late_reverb_gain);
        effect_f(effect, AL_EAXREVERB_LATE_REVERB_DELAY, p.late_reverb_delay);
        effect_fv(effect, AL_EAXREVERB_LATE_REVERB_PAN, p.late_reverb_pan);
        effect_f(effect, AL_EAXREVERB_ECHO_TIME, p.echo_time);
        effect_f(effect, AL_EAXREVERB_ECHO_DEPTH, p.echo_depth);
        effect_f(effect, AL_EAXREVERB_MODULATION_TIME, p.modulation_time);
        effect_f(effect, AL_EAXREVERB_MODULATION_DEPTH, p.modulation_depth);
        effect_f(effect, AL_EAXREVERB_AIR_ABSORPTION_GAINHF,
            std::max(p.air_absorption_gain_hf, AL_EAXREVERB_MIN_AIR_ABSORPTION_GAINHF));
        effect_f(effect, AL_EAXREVERB_HFREFERENCE, p.hf_reference);
        effect_f(effect, AL_EAXREVERB_LFREFERENCE, p.lf_reference);
        effect_f(effect, AL_EAXREVERB_ROOM_ROLLOFF_FACTOR, p.room_rolloff_factor);
        effect_i(effect, AL_EAXREVERB_DECAY_HFLIMIT, p.decay_hf_limit ? AL_TRUE : AL_FALSE);
        check_al("EAX parameter outside supported range");
        slot_i(slot, AL_EFFECTSLOT_EFFECT, static_cast<ALint>(effect));
        check_al("Apply EAX environment");
        if (!configured) {
            // The first map environment is installed before any input. Flush its
            // parameter transition with silence; later zone changes retain tails.
            std::array<float, chunk_frames * 2> silent_output{};
            if (!process(nullptr, silent_output.data(), chunk_frames)) throw std::runtime_error(error);
            configured = true;
        }
    }

    bool process(const float *in, float *out, int frames) {
        ContextScope scope(context, set_context, get_context);
        for (int offset = 0; offset < frames;) {
            const int count = std::min(frames - offset, chunk_frames);
            // Copying also permits in-place operation without the callback seeing
            // samples that alcRenderSamples has already overwritten.
            if (in) std::memcpy(input_copy.data(), in + offset * 2, count * 2 * sizeof(float));
            input = in ? input_copy.data() : nullptr;
            available = count; consumed = 0; callback_overread = false;
            render(device, out + offset * 2, count);
            if (callback_overread || consumed != count) {
                error = "OpenAL callback scheduling mismatch; pinned unity-rate adapter required";
                return false;
            }
            offset += count;
        }
        input = nullptr;
        return true;
    }
};

struct SourceFilter {
    int rate;
    float base{1}, relative_hf{1};
    double b0{1}, b1{}, b2{}, a1{}, a2{};
    std::array<double, 2> z1{}, z2{};
    explicit SourceFilter(int sample_rate) : rate(sample_rate) {}
    bool configure(float low, float high, float reference) {
        if (!std::isfinite(low) || !std::isfinite(high) || !std::isfinite(reference)
            || low < 0 || low > 1 || high < 0 || high > 1 || reference <= 0) return false;
        base = low; relative_hf = high;
        if (high == 1) { b0 = 1; b1 = b2 = a1 = a2 = 0; return true; }
        // RBJ high shelf, slope 1. EFX defines gain at the reference
        // frequency, so A is high (not sqrt(high), which would specify the
        // asymptotic shelf gain). This matches the provider's source filter.
        const double gain = std::max(static_cast<double>(high), 0.001);
        const double omega = 6.2831853071795864769 * std::min(static_cast<double>(reference) / rate, 0.49);
        const double cosine = std::cos(omega);
        const double beta = std::sqrt(2.0 * gain) * std::sin(omega);
        const double denominator = (gain + 1) - (gain - 1) * cosine + beta;
        b0 = gain * ((gain + 1) + (gain - 1) * cosine + beta) / denominator;
        b1 = -2 * gain * ((gain - 1) + (gain + 1) * cosine) / denominator;
        b2 = gain * ((gain + 1) + (gain - 1) * cosine - beta) / denominator;
        a1 = 2 * ((gain - 1) - (gain + 1) * cosine) / denominator;
        a2 = ((gain + 1) - (gain - 1) * cosine - beta) / denominator;
        return true;
    }
};
} // namespace

void *scct_reverb_create(int rate) {
    try { create_error.clear(); return new Reverb(rate); }
    catch (const std::exception &e) { create_error = e.what(); return nullptr; }
    catch (...) { create_error = "Unexpected reverb initialization failure"; return nullptr; }
}
int scct_reverb_validate_environment(const scct_reverb_environment *p) {
    if (!p) return 0;
    std::array<float, 26> values;
    std::memcpy(values.data(), p, values.size() * sizeof(float));
    for (float value : values) if (!std::isfinite(value)) return 0;
    const auto within = [](float value, float minimum, float maximum) { return value >= minimum && value <= maximum; };
    return within(p->density, 0, 1) && within(p->diffusion, 0, 1)
        && within(p->gain, 0, 1) && within(p->gain_hf, 0, 1) && within(p->gain_lf, 0, 1)
        && within(p->decay_time, 0.1f, 20) && within(p->decay_hf_ratio, 0.1f, 2) && within(p->decay_lf_ratio, 0.1f, 2)
        && within(p->reflections_gain, 0, 3.162278f) && within(p->reflections_delay, 0, 0.3f)
        && within(p->late_reverb_gain, 0, 10) && within(p->late_reverb_delay, 0, 0.1f)
        && within(p->echo_time, 0.075f, 0.25f) && within(p->echo_depth, 0, 1)
        && within(p->modulation_time, 0.04f, 4) && within(p->modulation_depth, 0, 1)
        && within(p->air_absorption_gain_hf, 0.8912508f, 1)
        && within(p->hf_reference, 1000, 20000) && within(p->lf_reference, 20, 1000)
        && within(p->room_rolloff_factor, 0, 10) && (p->decay_hf_limit == 0 || p->decay_hf_limit == 1);
}
int scct_reverb_set_environment(void *handle, const scct_reverb_environment *p) {
    if (!handle || !p) return 0;
    auto &self = *static_cast<Reverb *>(handle);
    try { ContextScope scope(self.context, self.set_context, self.get_context); self.configure(*p); self.error.clear(); return 1; }
    catch (const std::exception &e) { self.error = e.what(); return 0; }
}
int scct_reverb_process(void *handle, const float *input, float *output, int frames) {
    if (!handle || !output || frames < 0) return 0;
    auto &self = *static_cast<Reverb *>(handle);
    try { return self.process(input, output, frames) ? 1 : 0; }
    catch (const std::exception &e) { self.error = e.what(); return 0; }
}
void scct_reverb_reset(void *handle) {
    if (!handle) return;
    auto &self = *static_cast<Reverb *>(handle);
    try {
        ContextScope scope(self.context, self.set_context, self.get_context);
        self.slot_i(self.slot, AL_EFFECTSLOT_EFFECT, AL_EFFECT_NULL);
        self.slot_i(self.slot, AL_EFFECTSLOT_EFFECT, static_cast<ALint>(self.effect));
        alSourceRewind(self.source);
        alSourcePlay(self.source);
        self.check_al("Reset EAX reverb");
        std::array<float, chunk_frames * 2> silent_output{};
        if (!self.process(nullptr, silent_output.data(), chunk_frames)) throw std::runtime_error(self.error);
    } catch (const std::exception &e) { self.error = e.what(); }
}
void scct_reverb_destroy(void *handle) { delete static_cast<Reverb *>(handle); }
const char *scct_reverb_last_error(void *handle) {
    return handle ? static_cast<Reverb *>(handle)->error.c_str() : create_error.c_str();
}
int scct_reverb_latency_frames(void *) { return 0; }

void *scct_source_filter_create(int rate) {
    if (rate < 8000 || rate > 192000) return nullptr;
    try { return new SourceFilter(rate); } catch (...) { return nullptr; }
}
int scct_source_filter_configure(void *handle, float low, float high, float reference) {
    return handle && static_cast<SourceFilter *>(handle)->configure(low, high, reference);
}
int scct_source_filter_process(void *handle, const float *input, float *output, int frames) {
    if (!handle || !output || frames < 0) return 0;
    auto &self = *static_cast<SourceFilter *>(handle);
    if (self.relative_hf == 1) {
        for (int i = 0; i < frames * 2; ++i) output[i] = input ? input[i] * self.base : 0;
        return 1;
    }
    for (int i = 0; i < frames; ++i) {
        for (int channel = 0; channel < 2; ++channel) {
            const double sample = input ? input[i * 2 + channel] : 0;
            const double value = self.b0 * sample + self.z1[channel];
            self.z1[channel] = self.b1 * sample - self.a1 * value + self.z2[channel];
            self.z2[channel] = self.b2 * sample - self.a2 * value;
            output[i * 2 + channel] = static_cast<float>(value * self.base);
        }
    }
    return 1;
}
void scct_source_filter_reset(void *handle) {
    if (handle) { auto &self = *static_cast<SourceFilter *>(handle); self.z1.fill(0); self.z2.fill(0); }
}
void scct_source_filter_destroy(void *handle) { delete static_cast<SourceFilter *>(handle); }
