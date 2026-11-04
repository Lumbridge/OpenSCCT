#pragma once
#include <stdint.h>
#if defined(_WIN32)
#define SCCT_DSP_API __declspec(dllexport)
#else
#define SCCT_DSP_API __attribute__((visibility("default")))
#endif

// Independent C ABI around the pinned reAL Creative HRvb renderer. All sample
// buffers are interleaved stereo float32 at the rate passed to create. Handles are
// single-consumer: configure/reset/process must not run concurrently on one handle.
#ifdef __cplusplus
extern "C" {
#endif

typedef struct scct_reverb_environment {
    float density;
    float diffusion;
    float gain;
    float gain_hf;
    float gain_lf;
    float decay_time;
    float decay_hf_ratio;
    float decay_lf_ratio;
    float reflections_gain;
    float reflections_delay;
    float reflections_pan[3];
    float late_reverb_gain;
    float late_reverb_delay;
    float late_reverb_pan[3];
    float echo_time;
    float echo_depth;
    float modulation_time;
    float modulation_depth;
    float air_absorption_gain_hf;
    float hf_reference;
    float lf_reference;
    float room_rolloff_factor;
    int decay_hf_limit;
} scct_reverb_environment;

// Lossless EAX 3 listener ALLPARAMETERS packet, exactly 112 bytes. The Godot
// configure_eax array has these 28 four-byte fields in declaration order;
// integers are passed numerically, rather than reinterpreted as float bits.
typedef struct scct_eax3_environment {
    uint32_t environment;
    float environment_size, environment_diffusion;
    int32_t room, room_hf, room_lf;
    float decay_time, decay_hf_ratio, decay_lf_ratio;
    int32_t reflections;
    float reflections_delay, reflections_pan[3];
    int32_t reverb;
    float reverb_delay, reverb_pan[3];
    float echo_time, echo_depth, modulation_time, modulation_depth;
    float air_absorption_hf, hf_reference, lf_reference, room_rolloff_factor;
    uint32_t flags;
} scct_eax3_environment;

SCCT_DSP_API void *scct_reverb_create(int sample_rate);
// Pure, allocation-free range validation; does not require a renderer/context.
SCCT_DSP_API int scct_reverb_validate_environment(const scct_reverb_environment *environment);
SCCT_DSP_API int scct_reverb_validate_eax_environment(const scct_eax3_environment *environment);
SCCT_DSP_API int scct_reverb_set_eax_environment(void *handle, const scct_eax3_environment *environment);
SCCT_DSP_API const char *scct_reverb_backend_version(void);
// Default EAX3 source (no successful source-property changes): direct, directHF,
// room, roomHF. Uses the Creative source conversion, including mB truncation.
SCCT_DSP_API int scct_eax3_source_gains(const scct_eax3_environment *environment,
    float distance, float radius, float distance_factor, float rolloff, float output[4]);
// Returns 1 on success, 0 on failure. Invalid/nonfinite values are rejected.
SCCT_DSP_API int scct_reverb_set_environment(void *handle, const scct_reverb_environment *environment);
// Wet-only output. Input is the unpanned room send: (left + right) / 2 feeds
// the mono environment input. Null input means silence. Input/output may alias.
SCCT_DSP_API int scct_reverb_process(void *handle, const float *input, float *output, int frames);
SCCT_DSP_API void scct_reverb_reset(void *handle);
SCCT_DSP_API void scct_reverb_destroy(void *handle);
// Error text remains valid until the next call on this handle. Null reports create errors.
SCCT_DSP_API const char *scct_reverb_last_error(void *handle);
SCCT_DSP_API int scct_reverb_latency_frames(void *handle);

// Per-source Creative one-pole HF filter. high_gain is the relative POWER gain
// at HFReference (the original EAX convention), low_gain is broadband amplitude.
// A unity-gain filter is bit-preserving.
SCCT_DSP_API void *scct_source_filter_create(int sample_rate);
SCCT_DSP_API int scct_source_filter_configure(void *handle, float low_gain, float high_gain, float hf_reference);
SCCT_DSP_API int scct_source_filter_process(void *handle, const float *input, float *output, int frames);
SCCT_DSP_API void scct_source_filter_reset(void *handle);
SCCT_DSP_API void scct_source_filter_destroy(void *handle);

#ifdef __cplusplus
}
#endif
