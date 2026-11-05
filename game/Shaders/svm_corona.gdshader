// A light's corona (docs/RENDERING.md#coronas): a camera-facing quad at the light, screen blended over the scene
// (no depth test on the quad). Visibility: the CPU's trace against the world BSP, and the frame's depth at the
// light's centre, which must not lie in front of the light by more than max(16 uu, 8% of the distance), so meshes
// such as floors hide a corona while the lamp head around the light does not. The CPU sets the half size, the roll
// and the colour per instance.
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, skip_vertex_transform;
#include "res://Shaders/svm_fog.gdshaderinc"

uniform sampler2D corona_tex : filter_linear_mipmap, repeat_disable, hint_default_black;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D scene_tex : hint_screen_texture, filter_nearest, repeat_disable;
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
	vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, v_center, uv * VIEWPORT_SIZE, VIEWPORT_SIZE);
	// Native DrawCoronas selects FB_Translucent (ONE, INVSRCCOLOR) at 0x10a1418b.
	// Over the opaque scene this adds src * (1 - dst), preserving detail on bright lamp housings.
	vec3 scene = clamp(texture(scene_tex, SCREEN_UV).rgb, vec3(0.0), vec3(1.0));
	ALBEDO = texture(corona_tex, UV).rgb * svm_corona_colour.rgb * fog.a * (vec3(1.0) - scene);
	ALPHA = 1.0;
}
