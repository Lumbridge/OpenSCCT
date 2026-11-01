// The body of svm_laser.gdshader (see there). SVM_FIRST_PERSON_MASK: the merc laser sight's thermal pass, drawn in the
// transparent pass (after the first-person gun has marked the stencil) so the gun covers it as it covers the beam.
global uniform int svm_vision;
uniform float heat = 1.0;
uniform bool thermal_only = false;
uniform sampler2D mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec2 uv_offset = vec2(0.0);

void fragment() {
#ifdef SVM_FIRST_PERSON_MASK
	ALPHA = 1.0;
#endif
	vec4 m = texture(mask, UV + uv_offset);
	if (svm_vision == 36 && max(m.r, max(m.g, m.b)) * m.a > 0.3) {
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		ALBEDO = vec3(0.0, clamp(heat, 0.0, 1.0), fade);
	} else if (svm_vision == 38 && !thermal_only) {
		ALBEDO = vec3(0.0);
	} else {
		discard;
	}
}
