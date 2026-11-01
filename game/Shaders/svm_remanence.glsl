#[compute]
#version 450

// The flash-bang after-image (spec/equipment/flash-bang.md rule 5): mode 1 copies the frame into the capture, mode 2
// draws the capture over the frame by the factor (PlayerController.RemanenceBlendFactor). The blend is done on
// display values (sRGB encoded), as the original's frame buffer holds them.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(rgba16f, set = 0, binding = 1) uniform image2D capture_image;

layout(push_constant, std430) uniform Params {
	vec4 p; // x mode (1 capture, 2 blend), y factor, zw size
} u;

vec3 encode(vec3 c) {
	c = max(c, vec3(0.0));
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec3 decode(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	if (xy.x >= int(u.p.z) || xy.y >= int(u.p.w)) {
		return;
	}

	vec4 frame = imageLoad(color_image, xy);
	if (u.p.x < 1.5) {
		imageStore(capture_image, xy, frame);
		return;
	}

	if (u.p.x > 2.5) {
		// Read a separate, fresh capture: neighbouring reads of the image being
		// written would race between workgroups. Separation fades with dizziness.
		float strength = clamp(u.p.y, 0.0, 1.0);
		vec2 at = clamp(vec2(xy) + vec2(u.p.z * 0.015, u.p.w * 0.003) * strength,
			vec2(0.0), u.p.zw - vec2(1.0));
		ivec2 a = ivec2(floor(at));
		ivec2 b = min(a + ivec2(1), ivec2(u.p.zw) - ivec2(1));
		vec2 f = fract(at);
		vec3 ghost = mix(mix(encode(imageLoad(capture_image, a).rgb),
			encode(imageLoad(capture_image, ivec2(b.x, a.y)).rgb), f.x),
			mix(encode(imageLoad(capture_image, ivec2(a.x, b.y)).rgb),
			encode(imageLoad(capture_image, b).rgb), f.x), f.y);
		vec3 shown = mix(encode(frame.rgb), ghost, 0.45 * strength);
		imageStore(color_image, xy, vec4(decode(shown), frame.a));
		return;
	}

	vec3 shown = mix(encode(frame.rgb), encode(imageLoad(capture_image, xy).rgb), clamp(u.p.y, 0.0, 1.0));
	imageStore(color_image, xy, vec4(decode(shown), frame.a));
}
