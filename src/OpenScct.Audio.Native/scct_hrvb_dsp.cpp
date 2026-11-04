// OpenSCCT adapter for the independently reconstructed Creative ALchemy HRvb
// kernel in reAL (MIT, Copyright 2026 Neth). The pinned upstream sources and
// license are supplied by CMake. No proprietary binaries are redistributed.
#include "scct_reverb_dsp.h"
extern "C" {
#include "alrv.h"
#include "eax.h"
#include "virt.h"
}
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <string>
#include "scct_hrvb_kernel.h"

namespace {
constexpr int chunk_frames = 1024;
thread_local std::string create_error;
static_assert(sizeof(scct_eax3_environment) == 112);
static_assert(sizeof(scct_eax3_environment) == sizeof(eax3_reverb));

bool finite_packet(const scct_eax3_environment &packet) {
    std::array<float, 28> words;
    std::memcpy(words.data(), &packet, sizeof(packet));
    for (int i = 0; i < 28; ++i) {
        if (i == 0 || i == 3 || i == 4 || i == 5 || i == 9 || i == 14 || i == 27) continue;
        if (!std::isfinite(words[i])) return false;
    }
    return true;
}

bool validate(const scct_eax3_environment &packet) {
    if (!finite_packet(packet)) return false;
    eax3_listener listener;
    eax3l_reset(&listener);
    return eax3l_set(&listener, EAX3L_ALL, &packet, sizeof(packet)) == EAX_OK;
}

scct_eax3_environment from_efx(const scct_reverb_environment &p) {
    auto millibels = [](float gain) { return gain <= 0 ? -10000 : static_cast<int>(std::lround(2000.0 * std::log10(gain))); };
    scct_eax3_environment raw{};
    raw.environment_size = p.density >= 1 ? 7.5f : std::cbrt(p.density * 16);
    raw.environment_diffusion = p.diffusion;
    raw.room = millibels(p.gain); raw.room_hf = millibels(p.gain_hf); raw.room_lf = millibels(p.gain_lf);
    raw.decay_time = p.decay_time; raw.decay_hf_ratio = p.decay_hf_ratio; raw.decay_lf_ratio = p.decay_lf_ratio;
    raw.reflections = millibels(p.reflections_gain); raw.reflections_delay = p.reflections_delay;
    std::copy_n(p.reflections_pan, 3, raw.reflections_pan);
    raw.reverb = millibels(p.late_reverb_gain); raw.reverb_delay = p.late_reverb_delay;
    std::copy_n(p.late_reverb_pan, 3, raw.reverb_pan);
    raw.echo_time = p.echo_time; raw.echo_depth = p.echo_depth;
    raw.modulation_time = p.modulation_time; raw.modulation_depth = p.modulation_depth;
    raw.air_absorption_hf = p.air_absorption_gain_hf <= 0 ? -100 : 2000.0f * std::log10(p.air_absorption_gain_hf);
    raw.hf_reference = p.hf_reference; raw.lf_reference = p.lf_reference; raw.room_rolloff_factor = p.room_rolloff_factor;
    raw.flags = p.decay_hf_limit ? 0x3f : 0x1f;
    return raw;
}

struct Reverb {
    int rate;
    alrv engine{};
    rl_virt virtualizer{};
    eax3_listener listener{};
    alrv_props properties{};
    scct_eax3_environment packet{};
    std::array<float, chunk_frames> mono{};
    std::array<std::array<float, chunk_frames>, ALRV_NOUT> channels{};
    std::array<float, chunk_frames * 2> temporary{};
    std::array<float, chunk_frames * 2> warm_output{};
    std::string error;
    bool first_environment{true};
    bool constructor_shelf{true};
    ScctHrvbTransition transition;

    void apply_parameters(bool animate = false) {
        alrv_params params;
        alrv_compute_params(&params, &properties, rate);
        // reAL deliberately corrects two Creative quirks. Restore the shipped
        // routing and first-apply shelf here; see upstream tests/test_alrv.c.
        std::swap(params.late_out[2], params.late_out[3]);
        if (animate) transition.set(engine, params);
        else alrv_set_params(&engine, &params);
        if (constructor_shelf)
            std::fill_n(engine.shelf_a, 4, 0.5f);
    }

    explicit Reverb(int sample_rate) : rate(sample_rate) {
        if (rate < 8000 || rate > 192000) throw std::runtime_error("Unsupported HRvb sample rate");
        if (!alrv_init(&engine, rate)) throw std::runtime_error("Could not allocate HRvb delay network");
        rl_virt_init(&virtualizer, rate);
        eax3l_reset(&listener);
        alrv_props_default(&properties);
        std::memcpy(&packet, &listener.p, sizeof(packet));
        eax3l_apply(&listener, &properties);
        apply_parameters();
        // Prime the fixed-capacity stereo converter history before the mixer
        // callback; this also settles the original 128-frame input-start ramp.
        if (!process(nullptr, warm_output.data(), chunk_frames)) {
            rl_virt_free(&virtualizer); alrv_free(&engine);
            throw std::runtime_error("Could not allocate HRvb stereo virtualizer");
        }
    }
    ~Reverb() { rl_virt_free(&virtualizer); alrv_free(&engine); }

    bool configure(const scct_eax3_environment &next) {
        if (!validate(next)) { error = "Original EAX3 listener rejects this whole parameter block"; return false; }
        if (eax3l_set(&listener, EAX3L_ALL, &next, sizeof(next)) != EAX_OK) return false;
        if (eax3l_apply(&listener, &properties)) {
            // Update the original retained network using HRvb's coefficient
            // ramps and staggered tap crossfades, without resetting its tail.
            constructor_shelf = first_environment;
            apply_parameters(!first_environment);
            first_environment = false;
        }
        packet = next;
        error.clear();
        return true;
    }

    bool process(const float *input, float *output, int frames) {
        std::array<float *, ALRV_NOUT> pointers;
        for (int i = 0; i < ALRV_NOUT; ++i) pointers[i] = channels[i].data();
        for (int position = 0; position < frames;) {
            const int count = std::min(chunk_frames, frames - position);
            for (int i = 0; i < count; ++i) {
                mono[i] = input ? (input[(position + i) * 2] + input[(position + i) * 2 + 1]) * 0.5f : 0;
                // The Generic driver environment can already be sounding before
                // a first authored zone is entered. That is a live transition,
                // whereas constructor priming and silent setup remain immediate.
                if (mono[i] != 0) first_environment = false;
            }
            scct_hrvb_process(&engine, mono.data(), pointers.data(), count, &transition);
            // Creative's stereo-effect bus copies its centre into the rear
            // pair before the mode-3 speaker virtualizer (reAL reverb.c).
            for (int i = 0; i < count; ++i) {
                channels[ALRV_BL][i] += channels[ALRV_CX][i] * 0.7070000171661377f;
                channels[ALRV_BR][i] += channels[ALRV_CX][i] * 0.7070000171661377f;
            }
            std::memset(output + position * 2, 0, count * 2 * sizeof(float));
            if (!rl_virt_process(&virtualizer, pointers.data(), output + position * 2, count, temporary.data())) return false;
            position += count;
        }
        return true;
    }

    void reset() {
        alrv_reset(&engine);
        rl_virt_reset(&virtualizer);
        transition = ScctHrvbTransition{};
        apply_parameters();
        process(nullptr, warm_output.data(), chunk_frames);
    }
};

struct SourceFilter {
    int rate;
    float gain{1}, coefficient{1};
    std::array<float, 2> history{};
    explicit SourceFilter(int sample_rate) : rate(sample_rate) {}
};
} // namespace

void *scct_reverb_create(int rate) {
    try { create_error.clear(); return new Reverb(rate); }
    catch (const std::exception &e) { create_error = e.what(); return nullptr; }
    catch (...) { create_error = "Unexpected HRvb initialization error"; return nullptr; }
}
const char *scct_reverb_backend_version() { return "reAL HRvb / Creative ALchemy software reverb / b68e4d12c526"; }
int scct_reverb_validate_eax_environment(const scct_eax3_environment *p) { return p && validate(*p); }
int scct_reverb_set_eax_environment(void *handle, const scct_eax3_environment *p) {
    return handle && p && static_cast<Reverb *>(handle)->configure(*p);
}
int scct_reverb_validate_environment(const scct_reverb_environment *p) {
    if (!p) return 0;
    std::array<float, 26> values;
    std::memcpy(values.data(), p, values.size() * sizeof(float));
    for (float value : values) if (!std::isfinite(value)) return 0;
    if (p->density < 0 || p->density > 1 || (p->decay_hf_limit != 0 && p->decay_hf_limit != 1)) return 0;
    return validate(from_efx(*p));
}
int scct_reverb_set_environment(void *handle, const scct_reverb_environment *p) {
    if (!handle || !scct_reverb_validate_environment(p)) return 0;
    const auto raw = from_efx(*p);
    return scct_reverb_set_eax_environment(handle, &raw);
}
int scct_reverb_process(void *handle, const float *input, float *output, int frames) {
    return handle && output && frames >= 0 && static_cast<Reverb *>(handle)->process(input, output, frames);
}
void scct_reverb_reset(void *handle) { if (handle) static_cast<Reverb *>(handle)->reset(); }
void scct_reverb_destroy(void *handle) { delete static_cast<Reverb *>(handle); }
const char *scct_reverb_last_error(void *handle) { return handle ? static_cast<Reverb *>(handle)->error.c_str() : create_error.c_str(); }
int scct_reverb_latency_frames(void *) { return 0; }

int scct_eax3_source_gains(const scct_eax3_environment *p, float distance, float radius,
    float distance_factor, float rolloff, float output[4]) {
    if (!p || !output || !validate(*p) || !std::isfinite(distance) || !std::isfinite(radius)
        || !std::isfinite(distance_factor) || !std::isfinite(rolloff) || radius <= 0) return 0;
    eax3_listener listener;
    eax3l_reset(&listener);
    eax3l_set(&listener, EAX3L_ALL, p, sizeof(*p));
    alrv_props properties;
    alrv_props_default(&properties);
    eax3l_apply(&listener, &properties);
    eax3_buffer source;
    eax3b_defaults(&source);
    eax_geom geometry{std::max((distance - radius) / radius, 0.0f), 0, 360, 360, 0, distance_factor, rolloff};
    eax_gains gains;
    eax3_source_gains(&source, &listener, &geometry, &gains);
    output[0] = gains.direct; output[1] = gains.direct_hf; output[2] = gains.room; output[3] = gains.room_hf;
    return 1;
}

void *scct_source_filter_create(int rate) {
    if (rate < 8000 || rate > 192000) return nullptr;
    try { return new SourceFilter(rate); } catch (...) { return nullptr; }
}
int scct_source_filter_configure(void *handle, float gain, float relative_hf, float reference) {
    if (!handle || !std::isfinite(gain) || !std::isfinite(relative_hf) || !std::isfinite(reference)
        || gain < 0 || gain > 1 || relative_hf < 0 || relative_hf > 1 || reference <= 0) return 0;
    auto &self = *static_cast<SourceFilter *>(handle);
    self.gain = gain;
    self.coefficient = eax_lp_coef(relative_hf, reference / self.rate);
    return 1;
}
int scct_source_filter_process(void *handle, const float *input, float *output, int frames) {
    if (!handle || !output || frames < 0) return 0;
    auto &self = *static_cast<SourceFilter *>(handle);
    for (int i = 0; i < frames; ++i) {
        for (int channel = 0; channel < 2; ++channel) {
            const float sample = input ? input[i * 2 + channel] : 0;
            self.history[channel] = sample * self.coefficient + self.history[channel] * (1 - self.coefficient);
            output[i * 2 + channel] = self.history[channel] * self.gain;
        }
    }
    return 1;
}
void scct_source_filter_reset(void *handle) { if (handle) static_cast<SourceFilter *>(handle)->history.fill(0); }
void scct_source_filter_destroy(void *handle) { delete static_cast<SourceFilter *>(handle); }
