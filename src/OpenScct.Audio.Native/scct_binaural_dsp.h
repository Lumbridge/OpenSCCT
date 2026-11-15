#pragma once
#include "scct_reverb_dsp.h"
#ifdef __cplusplus
extern "C" {
#endif
// Creation/destruction are control-thread operations. Process/reset/configure are
// single-consumer, allocation-free mixer operations. Input is unpanned Godot stereo
// (mono at sqrt(0.5) per channel); output is independently rendered stereo.
SCCT_DSP_API void *scct_binaural_create(int sample_rate);
SCCT_DSP_API void scct_binaural_destroy(void *handle);
SCCT_DSP_API void scct_binaural_reset(void *handle);
SCCT_DSP_API int scct_binaural_direction(void *handle, float x, float y, float z);
SCCT_DSP_API int scct_binaural_process(void *handle, const float *input, float *output, int frames);
SCCT_DSP_API int scct_binaural_pending(void *handle);
SCCT_DSP_API int scct_binaural_latency_frames(void);
SCCT_DSP_API const char *scct_binaural_version(void);
#ifdef __cplusplus
}
#endif
