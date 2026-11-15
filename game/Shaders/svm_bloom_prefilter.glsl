#[compute]
#version 450

// 13-tap reduction and optional Karis weighting adapted from Dominik Wojtasik's
// MIT-licensed SCCT-Repack remaster/shaders.inc (b1bc848). See packaging/licenses/SCCT-Repack-LICENSE.txt.
// Only filters the input; the game's existing threshold, gain and glow weights remain in their original passes.
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(set = 0, binding = 0) uniform sampler2D source_texture;
layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D target_image;
layout(push_constant, std430) uniform Params { vec4 sizes; vec4 mode; } p;

float weight(vec4 c) { return 1.0 / (1.0 + dot(c.rgb, vec3(0.2126, 0.7152, 0.0722))); }
vec4 sample_at(vec2 uv, vec2 offset) { return textureLod(source_texture, uv + offset * p.sizes.xy, 0.0); }

void main() {
    ivec2 xy = ivec2(gl_GlobalInvocationID.xy), size = ivec2(p.sizes.zw);
    if (any(greaterThanEqual(xy, size))) return;
    vec2 uv = (vec2(xy) + 0.5) / vec2(size);
    vec4 e = sample_at(uv, vec2(0, 0));
    vec4 a = sample_at(uv, vec2(-2, -2)), b = sample_at(uv, vec2(0, -2)), c = sample_at(uv, vec2(2, -2));
    vec4 d = sample_at(uv, vec2(-2, 0)), f = sample_at(uv, vec2(2, 0));
    vec4 g = sample_at(uv, vec2(-2, 2)), h = sample_at(uv, vec2(0, 2)), i = sample_at(uv, vec2(2, 2));
    vec4 j = sample_at(uv, vec2(-1, -1)), k = sample_at(uv, vec2(1, -1));
    vec4 l = sample_at(uv, vec2(-1, 1)), m = sample_at(uv, vec2(1, 1));
    vec4 result = e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
    if (p.mode.x > 0.5) {
        vec4 b0 = (j + k + l + m) * 0.25, b1 = (a + b + d + e) * 0.25, b2 = (b + c + e + f) * 0.25;
        vec4 b3 = (d + e + g + h) * 0.25, b4 = (e + f + h + i) * 0.25;
        float w0 = 0.5 * weight(b0), w1 = 0.125 * weight(b1), w2 = 0.125 * weight(b2), w3 = 0.125 * weight(b3), w4 = 0.125 * weight(b4);
        result.rgb = (b0.rgb * w0 + b1.rgb * w1 + b2.rgb * w2 + b3.rgb * w3 + b4.rgb * w4) / (w0 + w1 + w2 + w3 + w4);
    }
    imageStore(target_image, xy, result);
}
