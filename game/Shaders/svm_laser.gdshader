// A laser detector beam in the vision modes that show it (docs/RENDERING.md#vision-modes): thermal draws the beam at
// its actor's HeatIntensity (SLaserBeam default 1) with the depth fade, EMF draws it at full EMF glow (the inverting
// post pass makes it white); the other modes never show it (bHideInNormalVision).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_opaque;

global uniform int svm_vision;
uniform float heat = 1.0;

void fragment() {
	if (svm_vision == 36) {
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		ALBEDO = vec3(0.0, clamp(heat, 0.0, 1.0), fade);
	} else if (svm_vision == 38) {
		ALBEDO = vec3(0.0);
	} else {
		discard;
	}
}
