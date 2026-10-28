// A laser beam in the vision modes that show it (docs/RENDERING.md#vision-modes): thermal draws the beam at its actor's
// HeatIntensity (SLaserBeam default 1) with the depth fade, EMF draws it at full EMF glow (the inverting post pass
// makes it white); the other modes never show it (bHideInNormalVision). The weapon lasers (spy pistol, merc laser
// sight) use it as the thermal pass of their additive look: thermal_only leaves EMF to that look, and mask keeps the
// heat to the texels the beam texture lights (its bright core, not the whole quad).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_opaque;

global uniform int svm_vision;
uniform float heat = 1.0;
uniform bool thermal_only = false;
uniform sampler2D mask : hint_default_white, filter_linear_mipmap;

void fragment() {
	vec4 m = texture(mask, UV);
	if (svm_vision == 36 && max(m.r, max(m.g, m.b)) * m.a > 0.3) {
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		ALBEDO = vec3(0.0, clamp(heat, 0.0, 1.0), fade);
	} else if (svm_vision == 38 && !thermal_only) {
		ALBEDO = vec3(0.0);
	} else {
		discard;
	}
}
