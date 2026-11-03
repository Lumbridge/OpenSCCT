// Fallback for imported meshes without Faithful material metadata. Converted game models use SVM_CLOAK in
// svm_world.gdshaderinc to preserve their full actor lighting. "Behind" is the
// frame drawn so far at the pixel, moved by up to 10 of 640 x 480 reference pixels along the view-space normal, the
// shift fading to 0 at 1500 uu from the camera (glass policy 0x109771f0). The pawn draws in the transparent pass while
// cloaked, so the screen copy holds the scene without it. Normal vision only (the view gates it).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, blend_mix, depth_draw_always;
#include "res://Shaders/svm_fog.gdshaderinc"

// GlassEffect / 255.
uniform float svm_glass = 0.0;
uniform sampler2D svm_behind : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D tex_diffuse : filter_linear_mipmap, hint_default_white;
uniform vec4 base_color = vec4(1.0);
uniform bool masked = false;

varying float v_shift;

void vertex() {
	// 1 m is 100 uu.
	float dist = length(NODE_POSITION_VIEW) * 100.0;
	v_shift = 10.0 * max(0.0, 1.0 - dist / 1500.0);
}

void fragment() {
	vec2 n = normalize(NORMAL).xy;
	vec2 uv = SCREEN_UV + vec2(-v_shift * n.x / 640.0, v_shift * n.y / 480.0);
	vec4 surface = texture(tex_diffuse, UV) * base_color;
	if (masked && surface.a < 0.5) { discard; }
	vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
	surface.rgb = surface.rgb * fog.a + fog.rgb;
	ALBEDO = mix(surface.rgb, texture(svm_behind, clamp(uv, vec2(0.0), vec2(1.0))).rgb, clamp(svm_glass, 0.0, 1.0));
	ALPHA = 1.0;
}
