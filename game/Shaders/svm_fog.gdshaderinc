// Per-camera data is shared by materials but selected by reserved visibility bits, never a last-written view.
global uniform sampler2D svm_surface_fog : filter_nearest, repeat_disable;

vec4 svm_fog_texel(int index) {
    return texelFetch(svm_surface_fog, ivec2(index % 1024, index / 1024), 0);
}

vec4 svm_fog_row(int fog_base, int index) { return svm_fog_texel(fog_base + index); }

#include "res://Shaders/svm_fog_math.gdshaderinc"

// Returns premultiplied fog colour and transmittance at this fragment's own depth.
vec4 surface_fog(uint camera_layers, vec3 view_position_metres, vec2 pixel, vec2 viewport_size) {
    int slot = int((camera_layers >> 8u) & 127u);
    if (slot == 0) return vec4(0.0, 0.0, 0.0, 1.0);
    vec4 view = svm_fog_texel(slot);
    if (view.y < 0.5) return vec4(0.0, 0.0, 0.0, 1.0);
    int block = int(view.x);
    if (view.y < 1.5) {
        float rate = view.w > view.z ? 1.0 / (view.w - view.z) : 1e6;
        float coverage = clamp((length(view_position_metres) * 100.0 - view.z) * rate, 0.0, 1.0);
        return vec4(svm_fog_texel(block).rgb * coverage, 1.0 - coverage);
    }
    int frame = block + 1;
    vec4 right = svm_fog_row(frame, 1), up = svm_fog_row(frame, 2), forward = svm_fog_row(frame, 3);
    vec3 ray = right.xyz * view_position_metres.x + up.xyz * view_position_metres.y - forward.xyz * view_position_metres.z;
    float distance = length(ray) * forward.w;
    if (distance < 0.0001) return vec4(0.0, 0.0, 0.0, 1.0);
    // Project the fragment itself into the candidate grid; this also avoids API-specific FragCoord Y origins.
    float depth = max(-view_position_metres.z, 0.000001);
    vec2 projected = (vec2(1.0) + vec2(view_position_metres.x * right.w, -view_position_metres.y * up.w) / depth) * 0.5 * viewport_size;
    return svm_fog_evaluate(frame, ivec2(projected), normalize(ray), distance);
}
