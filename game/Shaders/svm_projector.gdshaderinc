// A map texture projector (docs/RENDERING.md#projectors), drawn as a volume: the back faces of the projector's frustum
// box, with no depth test, rebuild the world point of every pixel behind them from the depth buffer and project the
// texture onto it. The including shader defines the frame buffer blend and its render mode:
//   PROJ_ALPHA     PB_AlphaBlend: SRCALPHA, INVSRCALPHA
//   PROJ_ADD       PB_Add: ONE, ONE
//   PROJ_MODULATE  PB_None and PB_Modulate: DESTCOLOR, SRCCOLOR (frame x texture x 2)
//   PROJ_LASER     spy laser dot: texture and alpha as its former additive material, in every vision mode
// Map projectors use display values, like the frame. Thermal and EMF draw no map projections.
//
// PROJ_SIDES (map projectors): the box is drawn by two surfaces, and for each camera exactly one of them makes pixels.
// A camera outside the box (by more than PROJ_MARGIN, so its near plane cannot cut a face) gets the faces turned
// towards it with the depth test on (PROJ_FRONT): a pixel whose surface lies in front of the box, which the range tests
// below would discard, is rejected before the shader runs, so a box hidden behind a wall costs nothing. A camera inside
// gets the faces turned away with no depth test, as before. Either way a pixel of the box is drawn once.

global uniform float svm_time;
global uniform int svm_vision;
#include "res://Shaders/svm_fog.gdshaderinc"

#ifdef PROJ_LASER
uniform sampler2D proj_tex : source_color, filter_linear_mipmap, repeat_disable, hint_default_black;
#else
uniform sampler2D proj_tex : filter_linear_mipmap, repeat_enable, hint_default_black;
#endif
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

#ifdef PROJ_SIDES
// The box's six faces as planes with outward normals: xyz normal, w = -dot(normal, point).
uniform vec4 proj_planes[6];
uniform vec3 proj_centre = vec3(0.0);
const float PROJ_MARGIN = 1.0;
#endif

varying vec3 v_outward;

void vertex() {
	v_outward = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
#ifdef PROJ_SIDES
	float outside = -1.0e9;
	for (int i = 0; i < 6; i++) {
		outside = max(outside, dot(proj_planes[i].xyz, CAMERA_POSITION_WORLD) + proj_planes[i].w);
	}
#ifdef PROJ_FRONT
	if (outside <= PROJ_MARGIN) {
		VERTEX = vec3(0.0);
	} else {
		// A little larger, so a surface lying in one of the box's faces is behind it for the depth test.
		VERTEX = proj_centre + (VERTEX - proj_centre) * 1.02;
	}
#else
	if (outside > PROJ_MARGIN) {
		VERTEX = vec3(0.0);
	}
#endif
#endif
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
#ifdef PROJ_FRONT
	// Only the faces turned towards the camera draw, so each pixel of the box is drawn once.
	if (dot(v_outward, VERTEX) >= 0.0) {
		discard;
	}
#else
	// Only the faces turned away from the camera draw, so each pixel of the box is drawn once, inside or outside it.
	if (dot(v_outward, VERTEX) < 0.0) {
		discard;
	}
#endif
#ifndef PROJ_LASER
	if (svm_vision == 36 || svm_vision == 38) {
		discard;
	}
#endif
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
	// A projector shades the reconstructed receiver, not its enclosing volume mesh.
	vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, view.xyz, FRAGCOORD.xy, VIEWPORT_SIZE);
#if defined(PROJ_LASER)
	ALBEDO = c.rgb * fog.a;
	ALPHA = c.a;
#elif defined(PROJ_MODULATE)
	// Native modulate fog is 127/255 before the doubled framebuffer multiplication.
	ALBEDO = mix(vec3(254.0 / 255.0), c.rgb * 2.0, fog.a);
	ALPHA = 1.0;
#elif defined(PROJ_ADD)
	ALBEDO = c.rgb * fog.a;
	ALPHA = 1.0;
#else
	ALBEDO = c.rgb * fog.a + fog.rgb;
	ALPHA = c.a;
#endif
}
