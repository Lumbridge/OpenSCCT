#include "scct_binaural_dsp.h"
#include <phonon.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>

namespace {
constexpr int block_size = 128;
struct Runtime {
    IPLContext context{};
    IPLHRTF hrtf{};
    IPLAudioSettings audio{};
    explicit Runtime(int rate) {
        IPLContextSettings settings{};
        settings.version = STEAMAUDIO_VERSION;
        if (iplContextCreate(&settings, &context) != IPL_STATUS_SUCCESS)
            throw std::runtime_error("Steam Audio context initialization failed");
        audio.samplingRate = rate; audio.frameSize = block_size;
        IPLHRTFSettings hrtf_settings{};
        hrtf_settings.type = IPL_HRTFTYPE_DEFAULT;
        hrtf_settings.volume = 1;
        hrtf_settings.normType = IPL_HRTFNORMTYPE_RMS;
        if (iplHRTFCreate(context, &audio, &hrtf_settings, &hrtf) != IPL_STATUS_SUCCESS) {
            iplContextRelease(&context);
            throw std::runtime_error("Steam Audio built-in HRTF initialization failed");
        }
    }
    ~Runtime() { iplHRTFRelease(&hrtf); iplContextRelease(&context); }
};

// HRTF construction is expensive and non-thread-safe. Serialize it outside DSP
// processing, and share one dataset per mix rate across all per-source effects.
std::shared_ptr<Runtime> runtime_for(int rate) {
    static std::mutex gate;
    static std::map<int, std::weak_ptr<Runtime>> runtimes;
    std::lock_guard<std::mutex> lock(gate);
    auto runtime = runtimes[rate].lock();
    if (!runtime) { runtime = std::make_shared<Runtime>(rate); runtimes[rate] = runtime; }
    return runtime;
}

struct Source {
    std::shared_ptr<Runtime> runtime;
    IPLBinauralEffect effect{};
    IPLBinauralEffectParams params{};
    std::array<float, block_size> mono{}, left{}, right{};
    int cursor = 0;
    int remaining = 0;
    bool active_block = false;
    IPLAudioEffectState tail = IPL_AUDIOEFFECTSTATE_TAILCOMPLETE;
    explicit Source(int rate) : runtime(runtime_for(rate)) {
        IPLBinauralEffectSettings settings{}; settings.hrtf = runtime->hrtf;
        if (iplBinauralEffectCreate(runtime->context, &runtime->audio, &settings, &effect) != IPL_STATUS_SUCCESS)
            throw std::runtime_error("Steam Audio source initialization failed");
        params.direction = {0, 0, -1};
        params.interpolation = IPL_HRTFINTERPOLATION_BILINEAR;
        params.spatialBlend = 1;
        params.hrtf = runtime->hrtf;
    }
    ~Source() { iplBinauralEffectRelease(&effect); }
    void reset() {
        iplBinauralEffectReset(effect);
        mono.fill(0); left.fill(0); right.fill(0);
        cursor = remaining = 0; active_block = false;
        tail = IPL_AUDIOEFFECTSTATE_TAILCOMPLETE;
    }
    void render_block() {
        float *input_channels[] = {mono.data()};
        float *output_channels[] = {left.data(), right.data()};
        IPLAudioBuffer input{1, block_size, input_channels};
        IPLAudioBuffer output{2, block_size, output_channels};
        if (active_block)
            tail = iplBinauralEffectApply(effect, &params, &input, &output);
        else if (tail == IPL_AUDIOEFFECTSTATE_TAILREMAINING)
            tail = iplBinauralEffectGetTail(effect, &output);
        else { left.fill(0); right.fill(0); }
        for (int i = 0; i < block_size; ++i)
            if (std::abs(left[i]) > 1e-10f || std::abs(right[i]) > 1e-10f) { remaining = std::max(remaining, block_size); break; }
        active_block = false;
    }
};
}

void *scct_binaural_create(int rate) {
    if (rate < 8000 || rate > 192000) return nullptr;
    try { return new Source(rate); } catch (...) { return nullptr; }
}
void scct_binaural_destroy(void *handle) { delete static_cast<Source *>(handle); }
void scct_binaural_reset(void *handle) { if (handle) static_cast<Source *>(handle)->reset(); }
int scct_binaural_direction(void *handle, float x, float y, float z) {
    if (!handle || !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(z)) return 0;
    auto &self = *static_cast<Source *>(handle);
    const float scale = std::max({std::abs(x), std::abs(y), std::abs(z)});
    if (scale > 1e-6f) {
        x /= scale; y /= scale; z /= scale;
        const float length = std::sqrt(x*x + y*y + z*z);
        self.params.direction = {x/length, y/length, z/length};
    } else self.params.direction = {0,0,-1};
    return 1;
}
int scct_binaural_process(void *handle, const float *input, float *output, int frames) {
    if (!handle || !output || frames < 0) return 0;
    auto &self = *static_cast<Source *>(handle);
    for (int i = 0; i < frames; ++i) {
        // Read before writing to support in-place Godot buffers. The one-block
        // pipeline has constant latency independent of callback partition sizes.
        const float sample = input ? (input[2*i] + input[2*i+1]) * 0.7071067811865475f : 0;
        output[2*i] = self.left[self.cursor]; output[2*i+1] = self.right[self.cursor];
        self.mono[self.cursor] = std::isfinite(sample) ? sample : 0;
        self.active_block |= std::abs(sample) > 1e-10f;
        if (std::abs(sample) > 1e-10f) self.remaining = block_size * 2;
        else self.remaining = std::max(0, self.remaining - 1);
        if (++self.cursor == block_size) { self.render_block(); self.cursor = 0; }
    }
    return 1;
}
int scct_binaural_pending(void *handle) {
    if (!handle) return 0;
    auto &self = *static_cast<Source *>(handle);
    return self.remaining > 0 || self.tail == IPL_AUDIOEFFECTSTATE_TAILREMAINING;
}
int scct_binaural_latency_frames(void) { return block_size; }
const char *scct_binaural_version(void) { return "Steam Audio 4.8.1 / built-in RMS HRTF / 128-frame adapter"; }
