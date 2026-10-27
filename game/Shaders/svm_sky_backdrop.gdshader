// The sky zone's picture behind everything (docs/RENDERING.md#sky-zone). The sky SubViewport renders the
// SkyZoneInfo's view and ends in display values (its own display-space pass); this full-screen triangle pair sits
// on the far plane and copies them, raw, wherever the world drew nothing.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, skip_vertex_transform;

uniform sampler2D sky_view : filter_nearest, repeat_disable, hint_default_black;

void vertex() {
	// Reverse Z: depth 0 is the far plane, so any geometry wins the depth test.
	POSITION = vec4(VERTEX.xy * 2.0, 0.0, 1.0);
}

void fragment() {
	ALBEDO = texture(sky_view, SCREEN_UV).rgb;
}
