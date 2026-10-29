// A light's beam (docs/RENDERING.md#light-beams): the LightBeam slice mesh (planes across the beam, x 0..1 along it,
// y and z -1..1 across) stretched along the light's forward axis to EchelonRange x LightBeamFalloffScale, opening from
// NearPlaneRatio x the spot at the light to the spot further out. Every slice adds boost x (1 - x) to the frame
// (MissP's lamp beam shows over the black sky). Thermal and EMF draw no beams.
// A beam of an LE_ESpotShadow light is cut by that light's shadow map and frustum and tinted by its projected texture
// (the game hands the beam's vertex shader the light's projection, 0x1097bbc1): a slice shows only where the light
// reaches, so a sun beam through a window draws as shafts between the bars and stops at the walls, and RiveE's caustic
// light throws teal shafts.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, blend_add, world_vertex_coords;

global uniform int svm_vision;
// The spot shadow lights' slot rows and depth atlas (SpotShadowLight; layout in svm_world.gdshaderinc).
global uniform sampler2D svm_spots;
global uniform sampler2D svm_spot_depth;
global uniform sampler2DArray svm_spot_cookies;

// Light frame in Godot space: origin (m) and its forward (x), right (y, SpotWidth) and up (z, SpotHeight) axes.
uniform vec3 beam_origin = vec3(0.0);
uniform vec3 beam_x = vec3(1.0, 0.0, 0.0);
uniform vec3 beam_y = vec3(0.0, 0.0, 1.0);
uniform vec3 beam_z = vec3(0.0, 1.0, 0.0);
// x length (m), y SpotWidth (m), z SpotHeight (m), w near ratio.
uniform vec4 beam_shape = vec4(1.0, 1.0, 1.0, 0.1);
// rgb: the per-slice boost; a: LightBeamFalloffScale.
uniform vec4 beam_boost = vec4(0.0, 0.0, 0.0, 1.0);
// The light's spot shadow slot, or -1: no cut (a light without a shadow map).
uniform int beam_slot = -1;

varying float v_along;
varying vec3 v_world;

void vertex() {
	// The mesh is drawn with an identity transform, so VERTEX holds the slice mesh's own coordinates.
	vec3 m = VERTEX;
	float grow = beam_shape.w + (1.0 - beam_shape.w) * beam_boost.a * m.x;
	VERTEX = beam_origin + beam_x * (m.x * beam_shape.x) + beam_y * (m.y * beam_shape.y * grow)
		+ beam_z * (m.z * beam_shape.z * grow);
	v_along = m.x;
	v_world = VERTEX;
}

// What the slot's light throws at the world point: its projected texture (cookie) where it reaches, black outside its
// frustum or behind an occluder. The same projection and filtered depth comparison as the world template's svm_spot_slot.
vec3 beam_lit(int i, vec3 wp) {
	vec4 a = texelFetch(svm_spots, ivec2(3, i), 0);
	if (a.x < 0.5) {
		return vec3(0.0);
	}
	vec4 w = vec4(wp, 1.0);
	vec3 v = vec3(dot(texelFetch(svm_spots, ivec2(0, i), 0), w), dot(texelFetch(svm_spots, ivec2(1, i), 0), w),
		dot(texelFetch(svm_spots, ivec2(2, i), 0), w));
	float z = -v.z;
	vec4 c = texelFetch(svm_spots, ivec2(4, i), 0);
	if (z <= c.a || z >= c.a + a.y) {
		return vec3(0.0);
	}
	vec4 f = texelFetch(svm_spots, ivec2(5, i), 0);
	vec2 ndc = f.z > 0.5 ? v.xy * f.xy : v.xy * f.xy / z;
	if (abs(ndc.x) >= 1.0 || abs(ndc.y) >= 1.0) {
		return vec3(0.0);
	}
	vec2 suv = vec2(ndc.x, -ndc.y) * 0.5 + 0.5;
	vec4 tile = texelFetch(svm_spots, ivec2(6, i), 0);
	vec4 e = texelFetch(svm_spots, ivec2(7, i), 0);
	float zref = z - e.y - e.z * z;
	vec2 atlas = vec2(textureSize(svm_spot_depth, 0));
	vec2 lo = tile.xy * atlas + 0.5;
	vec2 hi = (tile.xy + tile.zw) * atlas - 1.5;
	vec2 p = clamp((tile.xy + suv * tile.zw) * atlas - 0.5, lo, max(lo, hi));
	vec2 base = floor(p);
	vec2 fr = p - base;
	vec4 d = textureGather(svm_spot_depth, (base + 1.0) / atlas, 0);
	vec4 lit = step(vec4(zref), d);
	// textureGather order: (0, 1), (1, 1), (1, 0), (0, 0).
	float shadow = mix(mix(lit.w, lit.z, fr.x), mix(lit.x, lit.y, fr.x), fr.y);
	return texture(svm_spot_cookies, vec3(suv, f.w)).rgb * shadow;
}

void fragment() {
	if (svm_vision == 36 || svm_vision == 38) {
		discard;
	}
	vec3 lit = beam_slot >= 0 ? beam_lit(beam_slot, v_world) : vec3(1.0);
	ALBEDO = max(beam_boost.rgb, vec3(0.0)) * clamp(1.0 - v_along, 0.0, 1.0) * lit;
	ALPHA = 1.0;
}
