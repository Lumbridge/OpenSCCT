// A depth-tested second draw of the merc body. It never replaces the character's skins or lighting.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;

uniform vec4 cue_color : source_color = vec4(1.0, 0.72, 0.27, 1.0);
uniform float coverage = 1.0;
uniform float feet_y = 0.0;
uniform float head_y = 1.8;
uniform float pulse = 1.0;
uniform float vision_opacity = 1.0;
varying float body_height;

void vertex() {
	// The input vertex is skinned by Godot. A small shell avoids surface fighting without an outline extrusion.
	VERTEX += NORMAL * 0.001;
	body_height = ((MODEL_MATRIX * vec4(VERTEX, 1.0)).y - feet_y) / max(head_y - feet_y, 0.05);
}

void fragment() {
	// The upper boundary moves downwards: 50% left lights the bottom half of the currently posed body.
	float edge = coverage >= 0.999 ? 1.0 : 1.0 - smoothstep(coverage - 0.012, coverage + 0.012, body_height);
	float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 2.2);
	ALBEDO = cue_color.rgb;
	ALPHA = edge * pulse * vision_opacity * (0.055 + fresnel * 0.42);
}
