#include "scct_reverb_dsp.h"
#include "scct_binaural_dsp.h"
#include <godot_cpp/godot.hpp>
#include <godot_cpp/classes/audio_effect.hpp>
#include <godot_cpp/classes/audio_effect_instance.hpp>
#include <godot_cpp/classes/audio_server.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/packed_float32_array.hpp>
#include <godot_cpp/variant/packed_float64_array.hpp>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
#include <algorithm>
#include <limits>
#include <chrono>
#include <utility>

using namespace godot;

static bool decode_eax(const float *values, scct_eax3_environment &p) {
    for (int i = 0; i < 28; ++i) if (!std::isfinite(values[i])) return false;
    for (int index : {0, 27}) if (values[index] < 0 || static_cast<double>(values[index]) > UINT32_MAX ||
        std::trunc(values[index]) != values[index]) return false;
    for (int index : {3,4,5,9,14}) if (static_cast<double>(values[index]) < INT32_MIN ||
        static_cast<double>(values[index]) > INT32_MAX || std::trunc(values[index]) != values[index]) return false;
    p.environment = static_cast<uint32_t>(values[0]);
    p.environment_size = values[1]; p.environment_diffusion = values[2];
    p.room = static_cast<int32_t>(values[3]); p.room_hf = static_cast<int32_t>(values[4]); p.room_lf = static_cast<int32_t>(values[5]);
    p.decay_time = values[6]; p.decay_hf_ratio = values[7]; p.decay_lf_ratio = values[8];
    p.reflections = static_cast<int32_t>(values[9]); p.reflections_delay = values[10];
    std::copy(values+11, values+14, p.reflections_pan);
    p.reverb = static_cast<int32_t>(values[14]); p.reverb_delay = values[15];
    std::copy(values+16, values+19, p.reverb_pan);
    p.echo_time = values[19]; p.echo_depth = values[20]; p.modulation_time = values[21]; p.modulation_depth = values[22];
    p.air_absorption_hf = values[23]; p.hf_reference = values[24]; p.lf_reference = values[25]; p.room_rolloff_factor = values[26];
    p.flags = static_cast<uint32_t>(values[27]);
    return true;
}

// Parameters cross from the scene thread to the mixer without a lock, allocations,
// or invoking managed code. An interrupted snapshot is deferred until the next mix.
template<size_t Count> class Parameters {
    std::array<std::atomic<float>, Count> values{};
    std::atomic<uint64_t> revision{0};
public:
    void store(const float *source) {
        revision.fetch_add(1, std::memory_order_acq_rel);
        for (size_t i = 0; i < Count; ++i) values[i].store(source[i], std::memory_order_relaxed);
        revision.fetch_add(1, std::memory_order_release);
    }
    bool load(std::array<float, Count> &destination, uint64_t &seen) const {
        auto before = revision.load(std::memory_order_acquire);
        if ((before & 1) || before == seen) return false;
        for (size_t i = 0; i < Count; ++i) destination[i] = values[i].load(std::memory_order_relaxed);
        if (revision.load(std::memory_order_acquire) != before) return false;
        seen = before;
        return true;
    }
};

class ScctEaxReverbEffect;
class ScctEaxSourceFilter;

class ScctEaxReverbInstance : public AudioEffectInstance {
    GDCLASS(ScctEaxReverbInstance, AudioEffectInstance)
    Ref<ScctEaxReverbEffect> effect;
    void *backend = nullptr;
    uint64_t revision = 0;
    uint64_t raw_revision = 0, reset_seen = 0;
    bool raw_mode = false;
    int sample_rate = 48000;
    int64_t tail_remaining = 0;
    int64_t tail_duration = 0;
protected:
    static void _bind_methods() {}
public:
    void initialize(const Ref<ScctEaxReverbEffect> &owner);
    ~ScctEaxReverbInstance() override { scct_reverb_destroy(backend); }
    void _process(const void *source, AudioFrame *destination, int32_t count) override;
    // Godot queries this before processing AND before swapping its scratch buffer.
    // A tail flag that becomes false in _process skips that swap on an inactive bus,
    // leaving stale input to be fed through this chain on subsequent mixer blocks.
    bool _process_silence() const override { return true; }
};

class ScctEaxReverbEffect : public AudioEffect {
    GDCLASS(ScctEaxReverbEffect, AudioEffect)
    friend class ScctEaxReverbInstance;
    Parameters<27> parameters;
    Parameters<28> raw_parameters;
    std::atomic<bool> raw_mode{false};
    std::atomic<int> errors{0};
    std::atomic<int> latency{0};
    std::atomic<uint64_t> reset_generation{0};
protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("configure", "environment"), &ScctEaxReverbEffect::configure);
        ClassDB::bind_method(D_METHOD("configure_eax", "environment"), &ScctEaxReverbEffect::configure_eax);
        ClassDB::bind_method(D_METHOD("source_gains", "distance", "radius", "distance_factor", "rolloff"), &ScctEaxReverbEffect::source_gains);
        ClassDB::bind_method(D_METHOD("backend_version"), &ScctEaxReverbEffect::backend_version);
        ClassDB::bind_method(D_METHOD("get_error"), &ScctEaxReverbEffect::get_error);
        ClassDB::bind_method(D_METHOD("get_latency_samples"), &ScctEaxReverbEffect::get_latency_samples);
        ClassDB::bind_method(D_METHOD("reset"), &ScctEaxReverbEffect::reset);
    }
public:
    ScctEaxReverbEffect() {
        const float generic[27] = {1,1,0.3162f,0.8913f,1,1.49f,0.83f,1,0.05f,0.007f,0,0,0,
            1.2589f,0.011f,0,0,0,0.25f,0,0.25f,0,0.9943f,5000,250,0,1};
        parameters.store(generic);
    }
    bool configure(const PackedFloat32Array &environment) {
        if (environment.size() != 27) return false;
        for (int i = 0; i < 27; ++i) if (!std::isfinite(environment[i])) return false;
        if (environment[26] != 0 && environment[26] != 1) return false;
        scct_reverb_environment native;
        std::memcpy(&native, environment.ptr(), 26 * sizeof(float));
        native.decay_hf_limit = static_cast<int>(environment[26]);
        if (!scct_reverb_validate_environment(&native)) return false;
        parameters.store(environment.ptr());
        raw_mode.store(false, std::memory_order_release);
        return true;
    }
    bool configure_eax(const PackedFloat32Array &environment) {
#ifdef SCCT_REVERB_HRVB
        if (environment.size() != 28) return false;
        scct_eax3_environment native;
        if (!decode_eax(environment.ptr(), native) || !scct_reverb_validate_eax_environment(&native)) return false;
        raw_parameters.store(environment.ptr());
        raw_mode.store(true, std::memory_order_release);
        return true;
#else
        return false;
#endif
    }
    PackedFloat32Array source_gains(float distance, float radius, float distance_factor, float rolloff) const {
        PackedFloat32Array gains;
        gains.resize(4);
        gains[0] = 1; gains[1] = 1; gains[2] = 0; gains[3] = 1;
#ifdef SCCT_REVERB_HRVB
        if (!raw_mode.load(std::memory_order_acquire)) return gains;
        std::array<float, 28> snapshot;
        uint64_t seen = 0;
        scct_eax3_environment native;
        if (raw_parameters.load(snapshot, seen) && decode_eax(snapshot.data(), native))
            scct_eax3_source_gains(&native, distance, radius, distance_factor, rolloff, gains.ptrw());
#endif
        return gains;
    }
    String backend_version() const {
#ifdef SCCT_REVERB_HRVB
        return String(scct_reverb_backend_version());
#else
        return "OpenAL Soft 1.24.3 / EFX EAXReverb / native Godot mixer";
#endif
    }
    String get_error() const { return errors.load() == 0 ? String() : String("Native EAX reverb initialization, configuration or processing failed"); }
    int get_latency_samples() const { return latency.load(); }
    void reset() { reset_generation.fetch_add(1, std::memory_order_release); }
    Ref<AudioEffectInstance> _instantiate() override {
        Ref<ScctEaxReverbInstance> instance;
        instance.instantiate();
        instance->initialize(Ref<ScctEaxReverbEffect>(this));
        return instance;
    }
};

void ScctEaxReverbInstance::initialize(const Ref<ScctEaxReverbEffect> &owner) {
    effect = owner;
    sample_rate = static_cast<int>(AudioServer::get_singleton()->get_mix_rate());
    backend = scct_reverb_create(sample_rate);
    if (!backend) effect->errors.fetch_add(1);
    else effect->latency.store(scct_reverb_latency_frames(backend));
}

void ScctEaxReverbInstance::_process(const void *source, AudioFrame *destination, int32_t count) {
    auto output = reinterpret_cast<float *>(destination);
    if (!backend) { std::memset(output, 0, count * 2 * sizeof(float)); return; }
    auto generation=effect->reset_generation.load(std::memory_order_acquire);
    if (generation!=reset_seen) {
        reset_seen=generation; scct_reverb_reset(backend); tail_remaining=0;
    }
    std::array<float, 27> snapshot;
    bool wants_raw = effect->raw_mode.load(std::memory_order_acquire);
    if (wants_raw != raw_mode) { raw_mode = wants_raw; revision = 0; raw_revision = 0; }
#ifdef SCCT_REVERB_HRVB
    if (raw_mode) {
        std::array<float, 28> raw;
        scct_eax3_environment native;
        if (effect->raw_parameters.load(raw, raw_revision) && decode_eax(raw.data(), native)) {
            if (!scct_reverb_set_eax_environment(backend, &native)) effect->errors.fetch_add(1);
            double decay = std::max({native.decay_time, native.decay_time * native.decay_hf_ratio,
                native.decay_time * native.decay_lf_ratio});
            tail_duration = static_cast<int64_t>(std::ceil(sample_rate * (2 * decay + native.reflections_delay +
                native.reverb_delay + native.echo_time + native.modulation_time)));
        }
    } else
#endif
    if (effect->parameters.load(snapshot, revision)) {
        scct_reverb_environment environment;
        // The first 26 scalar/pan entries are consecutive floats; the final EFX flag is an integer.
        static_assert(offsetof(scct_reverb_environment, decay_hf_limit) == 26 * sizeof(float));
        std::memcpy(&environment, snapshot.data(), 26 * sizeof(float));
        environment.decay_hf_limit = snapshot[26] != 0;
        if (!scct_reverb_set_environment(backend, &environment)) effect->errors.fetch_add(1);
        double decay = std::max({environment.decay_time, environment.decay_time * environment.decay_hf_ratio,
            environment.decay_time * environment.decay_lf_ratio});
        tail_duration = static_cast<int64_t>(std::ceil(sample_rate * (2 * decay + environment.reflections_delay +
            environment.late_reverb_delay + environment.echo_time + environment.modulation_time)));
    }
    const float *input = static_cast<const float *>(source);
    bool active = false;
    if (input) for (int i = 0; i < count * 2; ++i) if (std::abs(input[i]) > 1e-10f) { active = true; break; }
    tail_remaining = active ? tail_duration : std::max<int64_t>(0, tail_remaining - count);
    if (!scct_reverb_process(backend, static_cast<const float *>(source), output, count)) {
        std::memset(output, 0, count * 2 * sizeof(float));
        effect->errors.fetch_add(1);
    }
}

class ScctEaxSourceFilterInstance : public AudioEffectInstance {
    GDCLASS(ScctEaxSourceFilterInstance, AudioEffectInstance)
    Ref<ScctEaxSourceFilter> effect;
    void *backend = nullptr;
    uint64_t revision = 0;
    int tail_duration = 0;
    int tail_remaining = 0;
    std::array<AudioFrame, 128> delay{};
    int delay_cursor = 0, delay_samples = 0;
    uint64_t reset_seen = 0;
    AudioFrame last_output{}, ramp_origin{};
    int ramp_position = 64;
    bool had_input = false;
protected:
    static void _bind_methods() {}
public:
    void initialize(const Ref<ScctEaxSourceFilter> &owner);
    ~ScctEaxSourceFilterInstance() override { scct_source_filter_destroy(backend); }
    void _process(const void *source, AudioFrame *destination, int32_t count) override;
    bool _process_silence() const override { return true; }
};

class ScctEaxSourceFilter : public AudioEffect {
    GDCLASS(ScctEaxSourceFilter, AudioEffect)
    friend class ScctEaxSourceFilterInstance;
    Parameters<3> parameters;
    std::atomic<int> delay_samples{0};
    std::atomic<uint64_t> reset_generation{0};
protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("configure", "gains"), &ScctEaxSourceFilter::configure);
        ClassDB::bind_method(D_METHOD("set_delay_samples", "samples"), &ScctEaxSourceFilter::set_delay_samples);
        ClassDB::bind_method(D_METHOD("reset"), &ScctEaxSourceFilter::reset);
    }
public:
    void set_delay_samples(int samples) { delay_samples.store(std::clamp(samples, 0, 128)); }
    void reset() { reset_generation.fetch_add(1, std::memory_order_release); }
    ScctEaxSourceFilter() { const float unity[3] = {1,1,5000}; parameters.store(unity); }
    bool configure(const PackedFloat32Array &gains) {
        if (gains.size() != 3) return false;
        float low_gain = gains[0], high_gain = gains[1], hf_reference = gains[2];
        if (!std::isfinite(low_gain) || !std::isfinite(high_gain) || !std::isfinite(hf_reference) ||
            low_gain < 0 || low_gain > 1 || high_gain < 0 || high_gain > 1 || hf_reference <= 0) return false;
        const float values[3] = {low_gain, high_gain, hf_reference};
        parameters.store(values);
        return true;
    }
    Ref<AudioEffectInstance> _instantiate() override {
        Ref<ScctEaxSourceFilterInstance> instance;
        instance.instantiate();
        instance->initialize(Ref<ScctEaxSourceFilter>(this));
        return instance;
    }
};

void ScctEaxSourceFilterInstance::initialize(const Ref<ScctEaxSourceFilter> &owner) {
    effect = owner;
    int sample_rate = static_cast<int>(AudioServer::get_singleton()->get_mix_rate());
    backend = scct_source_filter_create(sample_rate);
    tail_duration = sample_rate / 50;
}
void ScctEaxSourceFilterInstance::_process(const void *source, AudioFrame *destination, int32_t count) {
    auto generation = effect->reset_generation.load(std::memory_order_acquire);
    int requested_delay = effect->delay_samples.load();
    if (generation != reset_seen || requested_delay != delay_samples) {
        ramp_origin = last_output; ramp_position = 0;
        reset_seen = generation; delay_samples = requested_delay; delay_cursor = 0;
        delay.fill(AudioFrame{});
        if (backend) scct_source_filter_reset(backend);
    }
    std::array<float, 3> snapshot;
    if (backend && effect->parameters.load(snapshot, revision))
        scct_source_filter_configure(backend, snapshot[0], snapshot[1], snapshot[2]);
    const float *input = static_cast<const float *>(source);
    bool active = false;
    if (input) for (int i = 0; i < count * 2; ++i) if (std::abs(input[i]) > 1e-10f) { active = true; break; }
    if (!active && had_input) { ramp_origin = last_output; ramp_position = 0; }
    had_input = active;
    tail_remaining = active ? tail_duration : std::max(0, tail_remaining - count);
    if (!backend || !scct_source_filter_process(backend, static_cast<const float *>(source), reinterpret_cast<float *>(destination), count))
        std::memset(destination, 0, count * 2 * sizeof(float));
    if (delay_samples > 0) for (int i = 0; i < count; ++i) {
        auto sample = destination[i]; destination[i] = delay[delay_cursor]; delay[delay_cursor] = sample;
        if (++delay_cursor == delay_samples) delay_cursor = 0;
    }
    for (int i = 0; i < count && ramp_position < 64; ++i) {
        const float amount = static_cast<float>(++ramp_position) / 64;
        destination[i].left = destination[i].left * amount + ramp_origin.left * (1 - amount);
        destination[i].right = destination[i].right * amount + ramp_origin.right * (1 - amount);
    }
    if (count > 0) last_output = destination[count - 1];
    if (backend && tail_remaining == 0) scct_source_filter_reset(backend);
}

class ScctBinauralEffect;
class ScctBinauralInstance : public AudioEffectInstance {
    GDCLASS(ScctBinauralInstance, AudioEffectInstance)
    Ref<ScctBinauralEffect> effect;
    void *backend = nullptr;
    uint64_t revision = 0, reset_seen = 0;
    bool enabled = false;
    int sample_rate = 48000;
protected:
    static void _bind_methods() {}
public:
    void initialize(const Ref<ScctBinauralEffect> &owner);
    ~ScctBinauralInstance() override { scct_binaural_destroy(backend); }
    void _process(const void *source, AudioFrame *destination, int32_t count) override;
    bool _process_silence() const override { return true; }
};

class ScctBinauralEffect : public AudioEffect {
    GDCLASS(ScctBinauralEffect, AudioEffect)
    friend class ScctBinauralInstance;
    Parameters<4> parameters;
    std::atomic<uint64_t> reset_generation{0};
    std::atomic<uint64_t> calls{0}, frames{0}, elapsed_ns{0}, max_ns{0}, deadline_misses{0};
    std::atomic<int> errors{0};
    void *prepared = nullptr;
protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("configure", "direction"), &ScctBinauralEffect::configure);
        ClassDB::bind_method(D_METHOD("reset"), &ScctBinauralEffect::reset);
        ClassDB::bind_method(D_METHOD("get_latency_samples"), &ScctBinauralEffect::get_latency_samples);
        ClassDB::bind_method(D_METHOD("get_metrics"), &ScctBinauralEffect::get_metrics);
        ClassDB::bind_method(D_METHOD("get_error"), &ScctBinauralEffect::get_error);
    }
public:
    ScctBinauralEffect() {
        const float defaults[4] = {0,0,-1,0}; parameters.store(defaults);
        // ClassDB creates resources on the scene thread, before they are added to
        // a bus. The expensive shared HRTF and source buffers are ready to mix.
        prepared = scct_binaural_create(static_cast<int>(AudioServer::get_singleton()->get_mix_rate()));
        if (!prepared) errors.fetch_add(1);
    }
    ~ScctBinauralEffect() override { scct_binaural_destroy(prepared); }
    bool configure(const PackedFloat32Array &values) {
        if (values.size() != 4 || (values[3] != 0 && values[3] != 1)) return false;
        for (int i = 0; i < 4; ++i) if (!std::isfinite(values[i])) return false;
        parameters.store(values.ptr()); return true;
    }
    void reset() { reset_generation.fetch_add(1, std::memory_order_release); }
    int get_latency_samples() const { return scct_binaural_latency_frames(); }
    String get_error() const { return errors.load() == 0 ? String() : String("Steam Audio binaural initialization or processing failed"); }
    PackedFloat64Array get_metrics() const {
        PackedFloat64Array result; result.resize(5);
        result[0] = static_cast<double>(calls.load()); result[1] = static_cast<double>(frames.load());
        result[2] = static_cast<double>(elapsed_ns.load()); result[3] = static_cast<double>(max_ns.load());
        result[4] = static_cast<double>(deadline_misses.load()); return result;
    }
    Ref<AudioEffectInstance> _instantiate() override {
        Ref<ScctBinauralInstance> instance; instance.instantiate();
        instance->initialize(Ref<ScctBinauralEffect>(this)); return instance;
    }
};
void ScctBinauralInstance::initialize(const Ref<ScctBinauralEffect> &owner) {
    effect = owner; sample_rate = static_cast<int>(AudioServer::get_singleton()->get_mix_rate());
    backend = std::exchange(effect->prepared, nullptr);
    if (!backend) { backend = scct_binaural_create(sample_rate); if (!backend) effect->errors.fetch_add(1); }
}
void ScctBinauralInstance::_process(const void *source, AudioFrame *destination, int32_t count) {
    std::array<float, 4> values;
    if (effect->parameters.load(values, revision)) {
        bool requested = values[3] != 0;
        if (requested != enabled) { scct_binaural_reset(backend); enabled = requested; }
        if (backend) scct_binaural_direction(backend, values[0], values[1], values[2]);
    }
    auto generation = effect->reset_generation.load(std::memory_order_acquire);
    if (generation != reset_seen) { reset_seen = generation; scct_binaural_reset(backend); }
    if (!enabled) {
        if (source) std::memmove(destination, source, count * sizeof(AudioFrame));
        else std::memset(destination, 0, count * sizeof(AudioFrame));
        return;
    }
    auto start = std::chrono::steady_clock::now();
    if (!scct_binaural_process(backend, static_cast<const float *>(source), reinterpret_cast<float *>(destination), count)) {
        std::memset(destination, 0, count * sizeof(AudioFrame)); effect->errors.fetch_add(1);
    }
    auto duration = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - start).count());
    effect->calls.fetch_add(1, std::memory_order_relaxed); effect->frames.fetch_add(count, std::memory_order_relaxed);
    effect->elapsed_ns.fetch_add(duration, std::memory_order_relaxed);
    auto maximum = effect->max_ns.load(std::memory_order_relaxed);
    while (duration > maximum && !effect->max_ns.compare_exchange_weak(maximum, duration, std::memory_order_relaxed)) {}
    if (duration * sample_rate > static_cast<uint64_t>(count) * 1000000000ULL)
        effect->deadline_misses.fetch_add(1, std::memory_order_relaxed);
}

static void initialize_audio(ModuleInitializationLevel level) {
    if (level != MODULE_INITIALIZATION_LEVEL_SCENE) return;
    GDREGISTER_INTERNAL_CLASS(ScctEaxReverbInstance);
    GDREGISTER_CLASS(ScctEaxReverbEffect);
    GDREGISTER_INTERNAL_CLASS(ScctEaxSourceFilterInstance);
    GDREGISTER_CLASS(ScctEaxSourceFilter);
    GDREGISTER_INTERNAL_CLASS(ScctBinauralInstance);
    GDREGISTER_CLASS(ScctBinauralEffect);
}
static void uninitialize_audio(ModuleInitializationLevel) {}
extern "C" GDExtensionBool GDE_EXPORT scct_audio_library_init(GDExtensionInterfaceGetProcAddress get_proc,
    GDExtensionClassLibraryPtr library, GDExtensionInitialization *initialization) {
    GDExtensionBinding::InitObject init(get_proc, library, initialization);
    init.register_initializer(initialize_audio);
    init.register_terminator(uninitialize_audio);
    init.set_minimum_library_initialization_level(MODULE_INITIALIZATION_LEVEL_SCENE);
    return init.init();
}
