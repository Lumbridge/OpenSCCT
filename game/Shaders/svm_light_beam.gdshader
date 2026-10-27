// A light's beam (docs/RENDERING.md#light-beams): the LightBeam slice mesh (planes across the beam, x 0..1 along it,
// y and z -1..1 across) stretched along the light's forward axis to EchelonRange x LightBeamFalloffScale, opening from
// NearPlaneRatio x the spot at the light to the spot further out. Every slice multiplies the frame by
// 1 + boost x (1 - x): it brightens what is behind it, black stays black. Thermal and EMF draw no beams.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, blend_mul, world_vertex_coords;

global uniform int svm_vision;

// Light frame in Godot space: origin (m) and its forward (x), right (y, SpotWidth) and up (z, SpotHeight) axes.
uniform vec3 beam_origin = vec3(0.0);
uniform vec3 beam_x = vec3(1.0, 0.0, 0.0);
uniform vec3 beam_y = vec3(0.0, 0.0, 1.0);
uniform vec3 beam_z = vec3(0.0, 1.0, 0.0);
// x length (m), y SpotWidth (m), z SpotHeight (m), w near ratio.
uniform vec4 beam_shape = vec4(1.0, 1.0, 1.0, 0.1);
// rgb: the per-slice boost; a: LightBeamFalloffScale.
uniform vec4 beam_boost = vec4(0.0, 0.0, 0.0, 1.0);

varying float v_along;

void vertex() {
	// The mesh is drawn with an identity transform, so VERTEX holds the slice mesh's own coordinates.
	vec3 m = VERTEX;
	float grow = beam_shape.w + (1.0 - beam_shape.w) * beam_boost.a * m.x;
	VERTEX = beam_origin + beam_x * (m.x * beam_shape.x) + beam_y * (m.y * beam_shape.y * grow)
		+ beam_z * (m.z * beam_shape.z * grow);
	v_along = m.x;
}

void fragment() {
	if (svm_vision == 36 || svm_vision == 38) {
		discard;
	}
	ALBEDO = vec3(1.0) + max(beam_boost.rgb, vec3(0.0)) * clamp(1.0 - v_along, 0.0, 1.0);
	ALPHA = 1.0;
}
