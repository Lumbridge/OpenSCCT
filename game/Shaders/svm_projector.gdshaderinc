// A map texture projector (docs/RENDERING.md#projectors), drawn as a volume: the back faces of the projector's frustum
// box, with no depth test, rebuild the world point of every pixel behind them from the depth buffer and project the
// texture onto it. The including shader defines the frame buffer blend and its render mode:
//   PROJ_ALPHA     PB_AlphaBlend: SRCALPHA, INVSRCALPHA
//   PROJ_ADD       PB_Add: ONE, ONE
//   PROJ_MODULATE  PB_None and PB_Modulate: DESTCOLOR, SRCCOLOR (frame x texture x 2)
// Values are display values, like the frame. Thermal and EMF draw no projections.

global uniform float svm_time;
global uniform int svm_vision;

uniform sampler2D proj_tex : filter_linear_mipmap, repeat_enable, hint_default_black;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
// Projector frame in Godot space: origin (w = MaxTraceDistance, m), axes X (forward), Y (texture u), Z (texture v).
uniform vec4 proj_origin = vec4(0.0);
uniform vec3 proj_x = vec3(1.0, 0.0, 0.0);
uniform vec3 proj_y = vec3(0.0, 0.0, 1.0);
uniform vec3 proj_z = vec3(0.0, 1.0, 0.0);
// x, y: half the texture's width and height on the near plane (m); z: apex distance behind the origin (0 = ortho);
// w: 1 when the projector also draws on faces turned away from it.
uniform vec4 proj_shape = vec4(1.0, 1.0, 0.0, 0.0);
// Flipbook: cols, rows, frames, fps (frames < 2: none).
uniform vec4 proj_flip = vec4(1.0, 1.0, 1.0, 0.0);
// A static TexScaler / TexRotator over the texture, as svm_world's svm_affine rows (AquaE's rain tiles 10 times).
uniform vec3 uv_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 uv_row_v = vec3(0.0, 1.0, 0.0);
// UV animation as in svm_world: type 1 pan (a.xy direction, a.z rate), 2 oscillate.
uniform int uv_anim_type = 0;
uniform vec4 uv_anim_a = vec4(0.0);
uniform vec4 uv_anim_b = vec4(0.0);

varying vec3 v_outward;

void vertex() {
	v_outward = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	// The surface normal from the rebuilt points (taken before any discard), turned towards the camera.
	vec3 n = cross(dFdx(world), dFdy(world));
	if (dot(n, CAMERA_POSITION_WORLD - world) < 0.0) {
		n = -n;
	}
	// Only the faces turned away from the camera draw, so each pixel of the box is drawn once, inside or outside it.
	if (dot(v_outward, VERTEX) < 0.0 || svm_vision == 36 || svm_vision == 38) {
		discard;
	}
	vec3 d = world - proj_origin.xyz;
	float dx = dot(d, proj_x);
	if (dx < 0.0 || dx > proj_origin.w) {
		discard;
	}
	float s = proj_shape.z > 0.0 ? (dx + proj_shape.z) / proj_shape.z : 1.0;
	vec2 uv = vec2(dot(d, proj_y) / (2.0 * proj_shape.x * s), dot(d, proj_z) / (2.0 * proj_shape.y * s)) + 0.5;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		discard;
	}
	if (proj_shape.w < 0.5) {
		// Surfaces turned away from the projector are left alone (bProjectOnBackfaces off).
		if (dot(n, -proj_x) <= 0.0) {
			discard;
		}
	}
	uv = vec2(dot(uv_row_u, vec3(uv, 1.0)), dot(uv_row_v, vec3(uv, 1.0)));
	float t = svm_time;
	if (uv_anim_type == 1) {
		uv += uv_anim_a.xy * uv_anim_a.z * t;
	} else if (uv_anim_type == 2) {
		float su = uv_anim_a.x * sin(6.2831853 * uv_anim_a.y * t);
		float sv = uv_anim_b.x * sin(6.2831853 * uv_anim_b.y * t);
		uv.x = uv_anim_a.z >= 1.0 ? (uv.x - 0.5) * (1.0 + su) + 0.5 : uv.x + su;
		uv.y = uv_anim_b.z >= 1.0 ? (uv.y - 0.5) * (1.0 + sv) + 0.5 : uv.y + sv;
	}
	if (proj_flip.z >= 2.0) {
		float frame = mod(floor(t * proj_flip.w), proj_flip.z);
		vec2 cell = vec2(mod(frame, proj_flip.x), floor(frame / proj_flip.x));
		uv = (cell + clamp(fract(uv), 0.001, 0.999)) / proj_flip.xy;
	}
	vec4 c = texture(proj_tex, uv);
#if defined(PROJ_MODULATE)
	ALBEDO = c.rgb * 2.0;
	ALPHA = 1.0;
#elif defined(PROJ_ADD)
	ALBEDO = c.rgb;
	ALPHA = 1.0;
#else
	ALBEDO = c.rgb;
	ALPHA = c.a;
#endif
}
