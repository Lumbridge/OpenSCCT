#[compute]
#version 450

// The display-space post pass of the normal view when it has nothing else to do (docs/RENDERING.md#display-space):
// the glow composite's gate and glow, then the exact sRGB decode, with the arithmetic of svm_display_post.glsl
// (stages 4 and 2 in a mode without a vision pass). That shader also holds the fallback fog and every vision mode;
// a shader that holds less code runs faster on integrated GPUs whether or not the code runs, and this is the pass
// every normal frame pays for.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(set = 0, binding = 1) uniform sampler2D glow_texture;    // the glow composite, bilinear, clamp

layout(push_constant, std430) uniform Params {
	vec4 gate;       // x on, y gain, z offset, w 1 to add the glow
	vec4 gate_luma;  // rgb weights, a = 1 to linearize at the end
	vec4 frame;      // xy size in pixels
} p;

float srgb_to_linear(float c) {
	return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.frame.xy);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	vec4 color = imageLoad(color_image, xy);
	vec3 c = clamp(color.rgb, vec3(0.0), vec3(1.0));
	if (p.gate.x > 0.5) {
		// ps_1_1 0x10c4ea78: mad r0, gate, scene, glow, written to an 8-bit target.
		c *= clamp(p.gate.y * (dot(c, p.gate_luma.rgb) - p.gate.z), 0.0, 1.0);
		if (p.gate.w > 0.5) {
			vec2 uv = (vec2(xy) + 0.5) / vec2(size);
			c = clamp(c + texture(glow_texture, uv).rgb, vec3(0.0), vec3(1.0));
		}
	}

	if (p.gate_luma.a > 0.5) {
		c = vec3(srgb_to_linear(c.r), srgb_to_linear(c.g), srgb_to_linear(c.b));
	}

	imageStore(color_image, xy, vec4(c, color.a));
}
