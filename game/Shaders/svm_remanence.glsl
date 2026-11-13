#[compute]
#version 450

// The flash-bang after-image (spec/equipment/flash-bang.md rule 5): mode 1 copies the frame into the capture, mode 2
// draws the capture over the frame by the factor (PlayerController.RemanenceBlendFactor). The blend is done on
// display values (sRGB encoded), as the original's frame buffer holds them.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(rgba16f, set = 0, binding = 1) uniform image2D capture_image;

layout(push_constant, std430) uniform Params {
	vec4 p; // x mode (1 capture, 2 blend, 3 double vision), y factor, zw size
	vec4 q; // xy the double vision's two U offsets (RemanenceEffect.DoubleVisionSpread)
} u;

vec3 encode(vec3 c) {
	c = max(c, vec3(0.0));
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec3 decode(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

// The capture's display value at a pixel position, between its four nearest pixels.
vec3 ghost(vec2 at) {
	at = clamp(at, vec2(0.0), u.p.zw - vec2(1.0));
	ivec2 a = ivec2(floor(at));
	ivec2 b = min(a + ivec2(1), ivec2(u.p.zw) - ivec2(1));
	vec2 f = fract(at);
	return mix(mix(encode(imageLoad(capture_image, a).rgb), encode(imageLoad(capture_image, ivec2(b.x, a.y)).rgb), f.x),
		mix(encode(imageLoad(capture_image, ivec2(a.x, b.y)).rgb), encode(imageLoad(capture_image, b).rgb), f.x), f.y);
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
		// Cross-eyed double vision (PlayerController.CrossEyedFactor), as the original's final draw of the frame does it
		// (game 0x10966010 with the pixel shader at 0x10c4ebd4: tex t0, tex t1, add_d2 r0, t0, t1): the picture twice,
		// half each. One copy shows U 0 to 1 - q.x across the screen, the other U q.y to 1, so each is stretched from
		// its own side. Read from a separate, fresh capture: neighbouring reads of the image being written would race
		// between workgroups.
		float x = (float(xy.x) + 0.5) / u.p.z;
		vec3 shown = 0.5 * (ghost(vec2(x * (1.0 - u.q.x) * u.p.z - 0.5, float(xy.y)))
			+ ghost(vec2((u.q.y + x * (1.0 - u.q.y)) * u.p.z - 0.5, float(xy.y))));
		imageStore(color_image, xy, vec4(decode(shown), frame.a));
		return;
	}

	vec3 shown = mix(encode(frame.rgb), encode(imageLoad(capture_image, xy).rgb), clamp(u.p.y, 0.0, 1.0));
	imageStore(color_image, xy, vec4(decode(shown), frame.a));
}
