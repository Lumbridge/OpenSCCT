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
	vec4 mode;    // x 1 to read source_image, 0 to read source_texture; y 1 to fuse both axes for glow
} p;

// An 8-pixel output row needs 16 columns of the vertical half-pass and a 3-column halo on each side.
const int TILE_COLUMNS = 22;
shared vec4 vertical[8][TILE_COLUMNS];

vec4 round_to_half(vec4 value) {
	// The separate pass would round these values to RGBA16F before the horizontal filter read them.
	return vec4(unpackHalf2x16(packHalf2x16(value.xy)), unpackHalf2x16(packHalf2x16(value.zw)));
}

void glow_pair(ivec2 xy, ivec2 size) {
	int row = int(gl_LocalInvocationID.y);
	int first_column = int(gl_WorkGroupID.x) * 16 - 3;
	vec2 step_y = vec2(0.0, 1.0 / p.axis.w);
	for (int col = int(gl_LocalInvocationID.x); col < TILE_COLUMNS; col += 8) {
		float src_x = float(clamp(first_column + col, 0, int(p.axis.z) - 1));
		vec2 uv = vec2((src_x + 0.5) / p.axis.z, (float(xy.y) + 0.5) / p.target.y);
		vec4 value = p.target.z * texture(source_texture, uv + step_y)
			+ p.target.w * texture(source_texture, uv + 3.0 * step_y)
			+ p.target.z * texture(source_texture, uv - step_y)
			+ p.target.w * texture(source_texture, uv - 3.0 * step_y);
		vertical[row][col] = round_to_half(value);
	}
	barrier();
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	// For the exact 2:1 horizontal shrink, every tap falls halfway between two source texels.
	int col = int(gl_LocalInvocationID.x) * 2;
	vec4 c = p.target.z * mix(vertical[row][col + 4], vertical[row][col + 5], 0.5)
		+ p.target.w * mix(vertical[row][col + 6], vertical[row][col + 7], 0.5)
		+ p.target.z * mix(vertical[row][col + 2], vertical[row][col + 3], 0.5)
		+ p.target.w * mix(vertical[row][col], vertical[row][col + 1], 0.5);
	imageStore(target_image, xy, c);
}

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
	if (p.mode.y > 0.5) {
		glow_pair(xy, size);
		return;
	}
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
