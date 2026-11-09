#[compute]
#version 450

// The bright pass of the normal view's glow (svm_glow.glsl mode 0, ps_1_1 0x10c4e898) on its own: the frame read
// through a bilinear sampler, one fetch a texel where svm_glow.glsl filters four image loads by hand. The sampler's
// weights have 8 fractional bits, so a value can differ from the hand filter's in its last bits.
// docs/PERFORMANCE-LOWEND-2026-11.md has the measurements.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D frame_texture;
layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D target_image;

layout(push_constant, std430) uniform Params {
	vec4 cut;   // x threshold, y gain, zw target size
	vec4 luma;  // rgb luminance weights
} p;

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.cut.zw);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	vec2 uv = (vec2(xy) + 0.5) / vec2(size);
	vec3 s = clamp(textureLod(frame_texture, uv, 0.0).rgb, vec3(0.0), vec3(1.0));
	float l = dot(s, p.luma.rgb);
	vec4 c = l > p.cut.x
		? clamp(vec4(vec3(p.cut.y * l), p.cut.y * (l - p.cut.x)), vec4(0.0), vec4(1.0))
		: vec4(0.0);
	imageStore(target_image, xy, c);
}
