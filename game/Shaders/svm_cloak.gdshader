// The spy's camo suit (Actor.GlassEffect, docs/RENDERING.md#cloak): an overlay on every mesh of the pawn that blends
// the scene behind it over the pawn's own shading, out = lerp(shaded, behind, GlassEffect / 255). "Behind" is the
// frame drawn so far at the pixel, moved by up to 10 of 640 x 480 reference pixels along the view-space normal, the
// shift fading to 0 at 1500 uu from the camera (glass policy 0x109771f0). The pawn draws in the transparent pass while
// cloaked, so the screen copy holds the scene without it. No tint, no texture. Normal vision only (the view gates it).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, blend_mix, depth_draw_never;

// GlassEffect / 255.
instance uniform float svm_glass = 0.0;
uniform sampler2D svm_behind : hint_screen_texture, filter_linear, repeat_disable;

varying float v_shift;

void vertex() {
	// 1 m is 100 uu.
	float dist = length(NODE_POSITION_VIEW) * 100.0;
	v_shift = 10.0 * max(0.0, 1.0 - dist / 1500.0);
}

void fragment() {
	vec2 n = normalize(NORMAL).xy;
	vec2 uv = SCREEN_UV + vec2(-v_shift * n.x / 640.0, v_shift * n.y / 480.0);
	ALBEDO = texture(svm_behind, clamp(uv, vec2(0.0), vec2(1.0))).rgb;
	ALPHA = clamp(svm_glass, 0.0, 1.0);
}
