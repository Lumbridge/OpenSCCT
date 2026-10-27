// A light's corona (docs/RENDERING.md#coronas): a camera-facing quad at the light, additive, drawn over everything
// (no depth test on the quad). Visibility: the CPU's trace against the world BSP, and the frame's depth at the
// light's centre, which must not lie in front of the light by more than max(16 uu, 8% of the distance), so meshes
// such as floors hide a corona while the lamp head around the light does not. The CPU sets the half size, the roll
// and the colour per instance.
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, skip_vertex_transform;

uniform sampler2D corona_tex : filter_linear_mipmap, repeat_disable, hint_default_black;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
varying vec3 v_center;

instance uniform vec4 svm_corona = vec4(0.0);        // x half size in metres, y roll in radians
instance uniform vec4 svm_corona_colour = vec4(0.0); // display colour x 0.5 x fade

void vertex() {
	vec3 center = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	v_center = center;
	vec2 corner = VERTEX.xy * 2.0;
	float c = cos(svm_corona.y);
	float s = sin(svm_corona.y);
	vec2 r = vec2(corner.x * c - corner.y * s, corner.x * s + corner.y * c) * svm_corona.x;
	VERTEX = center + vec3(r, 0.0);
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	vec4 clip = PROJECTION_MATRIX * vec4(v_center, 1.0);
	vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
	if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) {
		float d = texture(depth_tex, uv).r;
		vec4 v = INV_PROJECTION_MATRIX * vec4(uv * 2.0 - 1.0, d, 1.0);
		float scene = -v.z / v.w;
		float light = -v_center.z;
		if (scene < light - max(0.16, 0.08 * light)) {
			discard;
		}
	}
	ALBEDO = texture(corona_tex, UV).rgb * svm_corona_colour.rgb;
	ALPHA = 1.0;
}
