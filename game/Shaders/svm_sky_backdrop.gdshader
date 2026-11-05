// The sky zone's picture behind everything (docs/RENDERING.md#sky-zone). The sky SubViewport renders the
// SkyZoneInfo's view straight to a floating-point texture. This full-screen triangle pair sits on the far plane
// and copies the display values wherever the world drew nothing.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, skip_vertex_transform;

uniform sampler2D sky_view : filter_nearest, repeat_disable, hint_default_black;

void vertex() {
	// Reverse Z: depth 0 is the far plane, so any geometry wins the depth test.
	POSITION = vec4(VERTEX.xy * 2.0, 0.0, 1.0);
}

void fragment() {
	// Retain the previous 8-bit backdrop precision for the main view's display-space post processing.
	vec3 sky = clamp(texture(sky_view, SCREEN_UV).rgb, vec3(0.0), vec3(1.0));
	ALBEDO = roundEven(sky * 255.0) / 255.0;
}
