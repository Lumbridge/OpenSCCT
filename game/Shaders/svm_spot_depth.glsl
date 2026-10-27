#[compute]
#version 450

// The shadow pass of a spot shadow light (docs/RENDERING.md#spot-shadow-lights): the light's camera drew the opaque
// world; this copies its depth buffer (reverse Z) into the light's tile of the shared shadow atlas as the linear
// distance along the light's axis from the camera, which the world template compares against.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D depth_texture;
layout(r32f, set = 0, binding = 1) uniform writeonly image2D distance_image;

layout(push_constant, std430) uniform Params {
	vec4 range;  // x near, y far (metres), zw size
	vec4 tile;   // xy the tile's first texel in the atlas, z 1 for an orthographic camera
} p;

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.range.zw);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	float d = texelFetch(depth_texture, xy, 0).r;
	float n = p.range.x;
	float f = p.range.y;
	// Nothing drawn (depth 0) reads as the far plane: lit to the end of the light's range.
	float z = p.tile.z > 0.5 ? f - d * (f - n) : (d > 0.0 ? n * f / (d * (f - n) + n) : f);
	imageStore(distance_image, xy + ivec2(p.tile.xy), vec4(z, 0.0, 0.0, 0.0));
}
