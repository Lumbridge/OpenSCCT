#[compute]
#version 450

// One pass of the vision blur pyramid (docs/RENDERING.md#vision-modes): the source drawn into a smaller target with a
// 4-tap filter along one axis, taps at +-1 and +-3 source texels from the target texel's centre (bilinear), weights
// 0.332 and 0.168. Two runs of vertical then horizontal passes make the 256 x 256 and 128 x 128 levels that thermal
// (heat) and motion (scene) vision read. The first pass reads the frame as an image (filtered here), the others read
// the previous level through a sampler.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D source_texture;
layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D target_image;
layout(rgba16f, set = 0, binding = 2) uniform readonly image2D source_image;

layout(push_constant, std430) uniform Params {
	vec4 axis;    // xy tap direction in source texels (1, 0) or (0, 1), zw source size
	vec4 target;  // xy target size, z near weight, w far weight
	vec4 mode;    // x 1 to read source_image, 0 to read source_texture
} p;

vec4 fetch_clamped(ivec2 xy) {
	ivec2 size = ivec2(p.axis.zw);
	return imageLoad(source_image, clamp(xy, ivec2(0), size - 1));
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

vec4 tap(vec2 uv) {
	return p.mode.x > 0.5 ? frame_bilinear(uv * p.axis.zw) : texture(source_texture, uv);
}

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.target.xy);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	vec2 uv = (vec2(xy) + 0.5) / vec2(size);
	vec2 step = p.axis.xy / p.axis.zw;
	vec4 c = p.target.z * tap(uv + step)
		+ p.target.w * tap(uv + 3.0 * step)
		+ p.target.z * tap(uv - step)
		+ p.target.w * tap(uv - 3.0 * step);
	imageStore(target_image, xy, c);
}
