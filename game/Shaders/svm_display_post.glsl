#[compute]
#version 450

// The display-space post pass (docs/RENDERING.md#display-space, #vision-modes). Until here the colour buffer holds the
// original's display values (in thermal its heat buffer: G heat, B depth fade). Three stages (bits), run together or
// apart:
//   1: the view's distance fog (not where nothing was drawn);
//   4: the glow composite of the normal view: frame x gate + the blurred bright pass (svm_glow.glsl);
//   2: the vision pass of PlayerController.RendMap, then the exact sRGB decode, so the tonemapper's sRGB encode
//      (Linear, exposure 1) puts the display value on screen.
// Thermal, motion and EMF read the blur pyramid (svm_vision_blur.glsl), which runs before stage 2; the glow runs
// between stages 1 and 4.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(set = 0, binding = 1) uniform sampler2D depth_texture;
layout(set = 0, binding = 2) uniform sampler2D noise_texture;   // Engine.Noise0_gauss, bilinear, repeat
layout(set = 0, binding = 3, std140) uniform Params {
	vec4 fog_color;    // rgb display colour, a = 1 when the zone has distance fog
	vec4 fog_range;    // x start, y end (metres of view depth), z near, w far (metres)
	vec4 gate;         // x on, y gain, z offset, w 1 to add the glow
	vec4 gate_luma;    // rgb weights
	vec4 frame;        // x RendMap, y stage bits, zw size in pixels
	vec4 noise;        // x cos, y sin of the grain angle, zw grain offset
	vec4 tiles;        // xy pixels per grain texel (1 / 128), zw sonar tiles over the frame (80, 60)
	vec4 night;        // rgb tint, a gain (4)
	vec4 luma;         // rgb display luminance (0.3, 0.59, 0.11), a = 1 to linearize at the end
	vec4 thermal;      // x composite alpha (200/255), y level mix (128/255), z table gain (2)
	vec4 motion;       // x 2 s, y MTVOffset / 255, z ramp scale, w fold threshold
	vec4 motion_low;   // rgb navy
	vec4 motion_high;  // rgb red
	vec4 motion_fold;  // rgb fold gains
	vec4 emf;          // x gain (2), y clean view rectangles in use (motion)
	vec4 clean_tint;   // rgb tint (0.675, 0.75, 0.675), a gain of each x2 stage (2)
	vec4 clean_bias;   // rgb bias (0.09, 0.1, 0.09)
	vec4 clean_rect[8]; // xy top left, zw bottom right, as parts of the picture
} p;
layout(set = 0, binding = 4) uniform sampler2D lut_texture;     // Engine.heat_and_attenuation_2d, bilinear, clamp
layout(set = 0, binding = 5) uniform sampler2D sonar_texture;   // Engine.sonar_background, bilinear, repeat
layout(set = 0, binding = 6) uniform sampler2D blur1_texture;   // 256 x 256 level of the pyramid, bilinear, clamp
layout(set = 0, binding = 7) uniform sampler2D blur2_texture;   // 128 x 128 level
layout(set = 0, binding = 8) uniform sampler2D glow_texture;    // the glow composite (512 x 512), bilinear, clamp

float srgb_to_linear(float c) {
	return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

void main() {
	ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(p.frame.zw);
	if (xy.x >= size.x || xy.y >= size.y) {
		return;
	}

	vec4 color = imageLoad(color_image, xy);
	vec3 c = clamp(color.rgb, vec3(0.0), vec3(1.0));
	int mode = int(p.frame.x + 0.5);
	int stage = int(p.frame.y + 0.5);

	if ((stage & 1) != 0 && p.fog_color.a > 0.5) {
		// Linear table fog on view depth (D3D W fog, 0x1095d640) over what was drawn. Reverse Z: 1 at the near plane, 0
		// at the far plane and where nothing was drawn (the clear colour stays).
		float d = texelFetch(depth_texture, xy, 0).r;
		if (d > 0.0) {
			float n = p.fog_range.z;
			float f = p.fog_range.w;
			float z = n * f / (d * (f - n) + n);
			float k = clamp((z - p.fog_range.x) / max(p.fog_range.y - p.fog_range.x, 1e-4), 0.0, 1.0);
			c = mix(c, p.fog_color.rgb, k);
		}
	}

	if ((stage & 4) != 0 && p.gate.x > 0.5) {
		// ps_1_1 0x10c4ea78: mad r0, gate, scene, glow, written to an 8-bit target.
		c *= clamp(p.gate.y * (dot(c, p.gate_luma.rgb) - p.gate.z), 0.0, 1.0);
		if (p.gate.w > 0.5) {
			vec2 uv = (vec2(xy) + 0.5) / vec2(size);
			c = clamp(c + texture(glow_texture, uv).rgb, vec3(0.0), vec3(1.0));
		}
	}

	if ((stage & 2) != 0) {
		vec2 uv = (vec2(xy) + 0.5) / vec2(size);
		// The grain: one texel a pixel, turned and moved by the frame's random angle and offset.
		vec2 g = (vec2(xy) + 0.5) * p.tiles.xy;
		vec2 q = vec2(g.x * p.noise.x - g.y * p.noise.y, g.x * p.noise.y + g.y * p.noise.x) + p.noise.zw;
		float n = texture(noise_texture, q).r;

		if (mode == 34) {
			// Night vision: saturate(4 n (L tint + glow)); the glow buffer is not fed in yet.
			float l = dot(c, p.luma.rgb);
			c = clamp(p.night.a * n * (l * p.night.rgb), 0.0, 1.0);
		} else if (mode == 36) {
			// Thermal: the two blurred levels over the sharp heat, then 2 x table(heat, fade) x grain.
			vec3 b = mix(texture(blur1_texture, uv).rgb, texture(blur2_texture, uv).rgb, p.thermal.y);
			vec3 h = mix(c, b, p.thermal.x);
			vec3 t = texture(lut_texture, vec2(h.g, h.b)).rgb;
			c = clamp(p.thermal.z * t * n, 0.0, 1.0);
		} else if (mode == 37) {
			// Motion: a false-colour ramp of the blurred scene's luminance x grain, then the sonar backdrop.
			vec3 s = texture(blur2_texture, uv).rgb;
			float lum = clamp(p.motion.x * dot(s * n, p.luma.rgb) + p.motion.y, -1.0, 1.0);
			vec3 ramp = mix(p.motion_low.rgb, p.motion_high.rgb, clamp(p.motion.z * lum, 0.0, 1.0));
			vec3 fold = p.motion_fold.rgb * (1.0 - lum);
			vec3 col = lum > p.motion.w ? fold : ramp;
			float sonar = texture(sonar_texture, uv * p.tiles.zw).r;
			vec3 outc = clamp(col * sonar, 0.0, 1.0);
			// The clean view rectangles of the tracker's held locks: the sharp scene in pale green monochrome, each
			// ps_1_1 stage saturated.
			int rects = int(p.emf.y + 0.5);
			for (int i = 0; i < rects; i++) {
				vec4 r = p.clean_rect[i];
				if (uv.x >= r.x && uv.x < r.z && uv.y >= r.y && uv.y < r.w) {
					float l = dot(c, p.luma.rgb);
					vec3 m = clamp(p.clean_tint.a * (l * p.clean_tint.rgb + p.clean_bias.rgb), 0.0, 1.0);
					m = clamp(p.clean_tint.a * m * n, 0.0, 1.0);
					outc = clamp(m * sonar, 0.0, 1.0);
					break;
				}
			}
			c = outc;
		} else if (mode == 38) {
			// EMF: the two blurred levels over the sharp scene (as thermal), then 2 x (1 - scene) x grain x sonar.
			vec3 b = mix(texture(blur1_texture, uv).rgb, texture(blur2_texture, uv).rgb, p.thermal.y);
			vec3 h = mix(c, b, p.thermal.x);
			float sonar = texture(sonar_texture, uv * p.tiles.zw).r;
			c = clamp(p.emf.x * (vec3(1.0) - h) * n * sonar, 0.0, 1.0);
		}

		if (p.luma.a > 0.5) {
			c = vec3(srgb_to_linear(c.r), srgb_to_linear(c.g), srgb_to_linear(c.b));
		}
	}

	imageStore(color_image, xy, vec4(c, color.a));
}
