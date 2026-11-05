// The body of svm_laser.gdshader (see there). SVM_FIRST_PERSON_MASK: the merc laser sight's vision pass, drawn in the
// transparent pass (after the first-person gun has marked the stencil) so the gun covers it as it covers the beam.
global uniform int svm_vision;
global uniform sampler2D svm_emf_env;
#include "res://Shaders/svm_fog.gdshaderinc"
uniform float heat = 1.0;
uniform bool thermal_only = false;
uniform sampler2D mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec2 uv_offset = vec2(0.0);

void fragment() {
#ifdef SVM_FIRST_PERSON_MASK
	ALPHA = 1.0;
#endif
	vec4 m = texture(mask, UV + uv_offset);
	// Keep weapon beams to their textured core in both vision modes, including the muzzle fin.
	if (max(m.r, max(m.g, m.b)) * m.a <= 0.3) {
		discard;
	}
	if (svm_vision == 36) {
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		ALBEDO = vec3(0.0, clamp(heat, 0.0, 1.0), fade);
	} else if (svm_vision == 38 && !thermal_only) {
		// Zero EMF intensity means a dark surface after inversion, not an invisible beam.
		// Use the same environment reflection and depth fog as other non-glowing actors.
		vec3 r = reflect(normalize(VERTEX), normalize(NORMAL));
		vec2 emf_uv = vec2(0.25 * (r.x + r.y) + 0.5, 0.5 * r.y + 0.5);
		vec3 env = texture(svm_emf_env, emf_uv).rgb;
		vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
		ALBEDO = env * fog.a + fog.rgb;
	} else {
		discard;
	}
}
