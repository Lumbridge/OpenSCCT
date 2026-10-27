#[compute]
#version 450

// The glow driver of the normal view (docs/RENDERING.md#display-space, glow driver 0x10967740 in mode 0). Two passes:
//   mode 0, bright pass: the frame drawn into the 512 x 512 bright target (bilinear); where the display luminance
//           L = dot(scene, (0.3, 0.59, 0.11)) is above GlowThreshold / 255 it writes rgb saturate(8 L) and alpha
//           saturate(8 (L - T)), elsewhere 0 (ps_1_1 0x10c4e898).
//   mode 1, composite: 0.4 L1 + 0.6 L2 + 0.7 L3 + 0.9 L4 of the blur pyramid (bilinear) back into the bright target
//           (ps_1_1 0x10c4ed00), saturated like the original's 8-bit target.
// The pyramid between them is svm_vision_blur.glsl.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D level1_texture;   // 256 x 256
layout(set = 0, binding = 1) uniform sampler2D level2_texture;   // 128 x 128
layout(set = 0, binding = 2) uniform sampler2D level3_texture;   // 64 x 64
layout(set = 0, binding = 3) uniform sampler2D level4_texture;   // 32 x 32
layout(rgba16f, set = 0, binding = 4) uniform readonly image2D frame_image;
layout(rgba16f, set = 0, binding = 5) uniform writeonly image2D target_image;

layout(push_constant, std430) uniform Params {
	vec4 mode;    // x 0 bright pass, 1 composite; y threshold (GlowThreshold / 255); z gain (8)
	vec4 size;    // xy frame size, zw target size
	vec4 weight;  // the four level weights
	vec4 luma;    // rgb bright pass luminance weights
} p;

vec4 fetch_clamped(ivec2 xy) {
	ivec2 size = ivec2(p.size.xy);
	return imageLoad(frame_image, clamp(xy, ivec2(0), size - 1));
}

// Bilinear filtering of the frame at a coordinate in its texels (D3D texel centres at +0.5).
vec4 frame_bilinear(vec2 at) {
	vec2 q = at - 0.5;
	ivec2 i = ivec2(floor(q));
	vec2 f = q - vec2(i);
	vec4 a = mix(fetch_clamped(i), fetch_clamped(i + ivec2(1, 0)), f.x);
	vec4 b = mix(fetch_clamped(i + ivec2(0, 1)), fetch_clamped(i + ivec2(1, 1)), f.x);
	return mix(a, b, f.y);
}

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.size.zw);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	vec2 uv = (vec2(xy) + 0.5) / vec2(size);
	vec4 c;
	if (p.mode.x < 0.5) {
		vec3 s = clamp(frame_bilinear(uv * p.size.xy).rgb, vec3(0.0), vec3(1.0));
		float l = dot(s, p.luma.rgb);
		c = l > p.mode.y
			? clamp(vec4(vec3(p.mode.z * l), p.mode.z * (l - p.mode.y)), vec4(0.0), vec4(1.0))
			: vec4(0.0);
	} else {
		c = p.weight.x * texture(level1_texture, uv)
			+ p.weight.y * texture(level2_texture, uv)
			+ p.weight.z * texture(level3_texture, uv)
			+ p.weight.w * texture(level4_texture, uv);
		c = clamp(c, vec4(0.0), vec4(1.0));
	}
	imageStore(target_image, xy, c);
}
