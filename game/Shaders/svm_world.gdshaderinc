// Faithful material template (docs/RENDERING.md#materials). Every value written here is a display value, the
// number the original's framebuffer would hold; the display-space post pass turns the frame into linear light at
// the end. The includer sets render_mode and one define of each group:
//   light:  LIGHT_LIGHTMAP | LIGHT_VERTEX | LIGHT_ACTOR | (none: fullbright)
//   blend:  BLEND_MASKED | BLEND_TRANSLUCENT | BLEND_ADDITIVE | BLEND_MODULATE | BLEND_DARKEN | (none: opaque)
//   extras: SELF_ILLUM, SELF_ILLUM_MASK, ENV_COLOR (+ ENV_TEX, SPEC_LAYER), UV_ANIM, OPACITY_LAYER, COMBINE, FLIPBOOK
// Textures are sampled raw (no sRGB decode): they hold display values like the original's.
// Vision modes (global svm_vision = PlayerController.RendMap, docs/RENDERING.md#vision-modes) replace the output:
//   36 thermal: the Z pass, (0, heat, 1 - viewZ / 5000); translucent surfaces blend (0, 0, fade) by alpha x HeatOpacity
//   38 EMF: the environment map by camera-space reflection x (1 - EMF glow), x the material's EMF texture
//   other modes draw the normal scene.

global uniform float svm_time;
// Texture LOD bias of colour textures (0 at the original's DefaultTexMipBias; a calibration switch).
global uniform float svm_mip_bias;
global uniform int svm_vision;
global uniform sampler2D svm_emf_env;
// Light night vision adds to every lit surface's light term: the render interface's boost byte (30 in night vision),
// halved into the texture factor and added before the 2x modulation (docs/RENDERING.md#vision-modes).
global uniform float svm_vision_ambient;

// Vision data of the material (its heat and EMF modifiers) and of the actor drawing it.
// heat_mode: 0 flat HeatIntensity, 1 HEATTEX_Cool 4 x lum x HeatIntensity, 2 HEATTEX_Heat lum + HeatIntensity.
// emf_mode: 0 none, 1 the EMF texture, 2 the EMF texture inverted.
uniform sampler2D tex_heat : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_black;
uniform sampler2D tex_emf : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int heat_mode = 0;
uniform int emf_mode = 0;
// x HeatIntensity, y HeatOpacity, z EMF glow (EMFIntensity while bEMFIsOn), w EMFOpacity (0 hides the actor in EMF).
// Merged geometry carries the values of each instance per vertex instead (CUSTOM0, w + 2); actors use the instance uniform.
instance uniform vec4 svm_vision_actor = vec4(0.0, 1.0, 0.0, 1.0);
// Set on solid and masked materials that carry a VISION_SEE_THROUGH next pass (MaterialCompiler.GetSeeThrough): in
// thermal and EMF the solid pass leaves out a surface whose HeatOpacity (EMFOpacity) is partial, and the blended pass
// draws it over what lies behind, by that opacity, writing no depth.
uniform bool vision_see_through = false;
// OpenSCCT's own cosmetics (actors only): a colour tint (rgb, a = strength; keeps each texel's brightness) and a hue
// turn of the self-illumination (x radians, y > 0 on; keeps brightness), for goggle lenses and lamps; z > 0 turns the
// diffuse too (the goggle beams, whose colour is their texture).
instance uniform vec4 svm_tint = vec4(1.0, 1.0, 1.0, 0.0);
instance uniform vec4 svm_glow_hue = vec4(0.0);

// Turns a colour's hue around the grey axis; the sum of the channels (its brightness) stays the same.
vec3 svm_hue_turn(vec3 c, float angle) {
	const vec3 k = vec3(0.57735);
	float co = cos(angle);
	return c * co + cross(k, c) * sin(angle) + k * dot(k, c) * (1.0 - co);
}
varying vec4 v_vision;
// OpenSCCT's own: a polished sheen on cosmetic camo materials (SkeletalModel.Cosmetic): its strength (0 off, 1 on the
// golden camo), the colour of its highlights (gold, or near white for the pyre camo's black lacquer), and how much of a
// mirror it is (1 on the golden camo: a full reflection of a bright fake environment, whatever the local light; 0: a
// partial reflection that darkens in shadow like the rest of the scene).
uniform float svm_sheen = 0.0;
uniform vec3 svm_sheen_tint = vec3(1.0, 0.8, 0.36);
uniform float svm_sheen_mirror = 0.0;

uniform sampler2D tex_diffuse : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;

#ifdef LIGHT_LIGHTMAP
uniform sampler2D tex_lightmap : filter_linear, repeat_disable, hint_default_white;
uniform float lightmap_scale = 2.0;
#endif

#ifdef SELF_ILLUM
uniform sampler2D tex_self_illum : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_black;
// A TexPanner on the self-illumination layer alone (a Shader whose SelfIllumination pans over a still Diffuse, as the
// Stickess skins): UV units a second.
uniform vec2 si_pan = vec2(0.0);
#ifdef SELF_ILLUM_MASK
uniform sampler2D tex_self_illum_mask : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
#endif
#endif

#ifdef ENV_COLOR
uniform sampler2D tex_spec_mask : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform vec3 env_color = vec3(0.0);
#ifdef ENV_TEX
// The env map's own texture, looked up by the camera-space reflection like the EMF scene (0.25 R.xy + 0.5).
uniform sampler2D tex_env : filter_linear, repeat_enable, hint_default_black;
#endif
#ifdef SPEC_LAYER
// The SpecularityMask's own modifiers (layer layout below).
uniform int sm_anim_type = 0;
uniform vec4 sm_anim_a = vec4(0.0);
uniform vec4 sm_anim_b = vec4(0.0);
uniform vec3 sm_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 sm_row_v = vec3(0.0, 1.0, 0.0);
#endif
#endif

#ifdef UV_ANIM
// type 1 pan: a.xy direction, a.z rate. type 2 oscillate: a = (U amp, U rate, U type), b = (V amp, V rate, V type).
// type 3 rotate: a.x radians per second, a.z / a.w pivot in texels; tex_size gives the texel scale.
uniform int uv_anim_type = 0;
uniform vec4 uv_anim_a = vec4(0.0);
uniform vec4 uv_anim_b = vec4(0.0);
uniform vec2 tex_size = vec2(256.0);
#endif

// Separate layers (UE2 transforms each Shader / Combiner input by its own modifier chain): each has an animation of the
// UV_ANIM layout and a static transform (rows dotted with (u, v, 1)). They start from the mesh's own UVs: base_row_u / v
// undo the static transform the exporter baked into the UVs for the Diffuse.
#if defined(OPACITY_LAYER) || defined(SPEC_LAYER)
uniform vec3 base_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 base_row_v = vec3(0.0, 1.0, 0.0);
#endif

#ifdef OPACITY_LAYER
uniform sampler2D tex_opacity : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int op_anim_type = 0;
uniform vec4 op_anim_a = vec4(0.0);
uniform vec4 op_anim_b = vec4(0.0);
uniform vec3 op_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 op_row_v = vec3(0.0, 1.0, 0.0);
// 1: the opacity is the texture's luminance (its alpha is flat), as the exporter's opacity bake picks.
uniform int op_lum = 0;
#endif

#ifdef COMBINE
// UE2 Combiner: tex_diffuse is Material1, tex_c2 Material2, tex_cmask the Mask (Material1 when cmask_on is 0).
// combine = (CombineOperation, AlphaOperation, Modulate 1/2/4, InvertMask).
uniform sampler2D tex_c2 : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform sampler2D tex_cmask : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int cmask_on = 0;
uniform ivec4 combine = ivec4(0, 0, 1, 0);
uniform int c1_anim_type = 0;
uniform vec4 c1_anim_a = vec4(0.0);
uniform vec4 c1_anim_b = vec4(0.0);
uniform vec3 c1_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 c1_row_v = vec3(0.0, 1.0, 0.0);
uniform int c2_anim_type = 0;
uniform vec4 c2_anim_a = vec4(0.0);
uniform vec4 c2_anim_b = vec4(0.0);
uniform vec3 c2_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 c2_row_v = vec3(0.0, 1.0, 0.0);
uniform int cm_anim_type = 0;
uniform vec4 cm_anim_a = vec4(0.0);
uniform vec4 cm_anim_b = vec4(0.0);
uniform vec3 cm_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 cm_row_v = vec3(0.0, 1.0, 0.0);
// 1: Material2 is a TexEnvMap, looked up by the camera-space reflection (0.25 R.xy + 0.5, as ENV_TEX) instead of the
// UVs; c2_flip plays its AnimNext chain from the atlas in tex_c2 (x columns, y rows, z frames, w frames per second).
uniform int c2_env = 0;
uniform vec4 c2_flip = vec4(1.0, 1.0, 1.0, 0.0);
#endif

#ifdef FLIPBOOK
// Texture.AnimNext chain as an atlas (tex_diffuse): x columns, y rows, z frames, w frames per second.
uniform vec4 flipbook = vec4(1.0, 1.0, 1.0, 30.0);
#endif

#ifdef LIGHT_ACTOR
// Actor lighting (docs/RENDERING.md#actor-lighting): ambient plus the four strongest lights, set per instance in
// the svm_one_light layout below. svm_ambient.w holds the actor's Echelon code (v_echelon).
instance uniform vec4 svm_ambient = vec4(0.0);
instance uniform vec4 svm_l0_pos = vec4(0.0);
instance uniform vec4 svm_l0_col = vec4(0.0);
instance uniform vec4 svm_l0_spot = vec4(0.0, 0.0, 0.0, -2.0);
instance uniform vec4 svm_l1_pos = vec4(0.0);
instance uniform vec4 svm_l1_col = vec4(0.0);
instance uniform vec4 svm_l1_spot = vec4(0.0, 0.0, 0.0, -2.0);
instance uniform vec4 svm_l2_pos = vec4(0.0);
instance uniform vec4 svm_l2_col = vec4(0.0);
instance uniform vec4 svm_l2_spot = vec4(0.0, 0.0, 0.0, -2.0);
instance uniform vec4 svm_l3_pos = vec4(0.0);
instance uniform vec4 svm_l3_col = vec4(0.0);
instance uniform vec4 svm_l3_spot = vec4(0.0, 0.0, 0.0, -2.0);

varying vec3 v_light;

#endif

// One light of either light set: pos.xyz in metres and pos.w the radius in metres (pos.w < 0: a sun, pos.xyz the
// direction to it); col.rgb gain x colour, col.a cos(theta / 2) of a spot; spot.xyz the spot direction, spot.w
// cos(phi / 2), else the point light's effect: -2 plain, -3 LE_QuadraticNonIncidence, -4 LE_PointAmbient (both on
// actors only, falloff 0). Falloff 1 - 3x^2 + 2x^3 over x = d / R, Lambert, no specular.
vec3 svm_one_light(vec4 pos, vec4 col, vec4 spot, vec3 wp, vec3 wn, int falloff) {
	if (dot(col.rgb, col.rgb) <= 0.0) {
		return vec3(0.0);
	}
	vec3 l;
	float att = 1.0;
	float ambient = 0.0;
	if (pos.w < 0.0) {
		l = normalize(pos.xyz);
	} else {
		vec3 d = pos.xyz - wp;
		float dist = length(d);
		l = d / max(dist, 1e-5);
		float x = dist / max(pos.w, 1e-5);
		if (x >= 1.0) {
			att = 0.0;
		} else if (falloff == 0 && spot.w < -2.5 && spot.w > -3.5) {
			// D3D attenuation 1 / (Att2 d^2) with Att2 = 8 / R^2, without the 2 of the point fit that col carries
			// (col = 2 C); the light term saturates later.
			att = 0.5 / max(8.0 * x * x, 1e-3);
		} else if (falloff == 0 && spot.w < -3.5) {
			// The light's ambient (its colour, attenuated) is added whatever the normal.
			att = 1.0 - 3.0 * x * x + 2.0 * x * x * x;
			ambient = att;
		} else if (falloff == 1) {
			att = 1.0 - x * x;
		} else if (falloff == 2) {
			att = 1.0 - x;
		} else if (falloff == 3) {
			att = (1.0 - x) * (1.0 - x);
		} else {
			att = 1.0 - 3.0 * x * x + 2.0 * x * x * x;
		}
		if (spot.w > -1.5) {
			float c = dot(-l, spot.xyz);
			att *= clamp((c - spot.w) / max(col.a - spot.w, 1e-5), 0.0, 1.0);
		}
	}
	return col.rgb * (att * max(0.0, dot(wn, l)) + ambient);
}

#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX) || defined(LIGHT_ACTOR)
// The spot shadow lights (LE_ESpotShadow: the merc torch, searchlights, window and sun projectors,
// docs/RENDERING.md#spot-shadow-lights), per pixel on the light stage: colour x cookie x attenuation x shadow, N.L by
// the normal influence. One row of svm_spots per slot:
//   0-2 world to the light camera's view (3 x 4; the camera sits at the frustum's apex)
//   3   x on (1 a static light, 2 bDynamicLight), y range (m), z normal influence, w attenuation (0 none, 1 linear,
//       2 late)
//   4   rgb gain x colour, a distance from the apex to the light (m): the lit span starts there
//   5   x, y the view to clip scale (perspective: divided by depth), z 1 for parallel rays, w cookie layer
//   6   the slot's tile of the depth atlas (uv origin, uv size)
//   7   x own zone (or -1), y bias (m), z bias per metre, w edge softening disc radius (texels; 0 one lookup)
global uniform sampler2D svm_spots;
global uniform int svm_spot_count;
global uniform sampler2D svm_spot_depth;
global uniform sampler2DArray svm_spot_cookies;
global uniform sampler2D svm_spot_atten;
// IgnoredEchelonLights: one row per group of the map's lists (row 0 none), one texel per slot, 1 where the group
// ignores the slot's light (SpotShadowLight.SetIgnoreGroups).
global uniform sampler2D svm_spot_ignore;
varying vec3 v_wpos;
varying vec3 v_wnormal;
// The surface's Echelon code (RenderMap.EchelonCode): bit 0 skips static spot shadow lights, bit 1 dynamic ones, the
// rest the IgnoredEchelonLights group. Actors: svm_ambient.w; other surfaces: CUSTOM1.x (0 without the stream).
varying flat float v_echelon;

// One filtered shadow lookup (the depth map compared at the four nearest texels, blended bilinearly, as hardware
// shadow filtering does), kept inside the slot's tile. 1 lit, 0 shadowed.
float svm_spot_lookup(vec4 tile, vec2 suv, float zref) {
	vec2 atlas = vec2(textureSize(svm_spot_depth, 0));
	vec2 lo = tile.xy * atlas + 0.5;
	vec2 hi = (tile.xy + tile.zw) * atlas - 1.5;
	vec2 p = clamp((tile.xy + suv * tile.zw) * atlas - 0.5, lo, max(lo, hi));
	vec2 base = floor(p);
	vec2 f = p - base;
	vec4 d = textureGather(svm_spot_depth, (base + 1.0) / atlas, 0);
	vec4 lit = step(vec4(zref), d);
	// textureGather order: (0, 1), (1, 1), (1, 0), (0, 0).
	return mix(mix(lit.w, lit.z, f.x), mix(lit.x, lit.y, f.x), f.y);
}

// The shadow term: one filtered lookup, or eight on a disc softening the edge (the sandbox's shadow_edge_smoothing).
float svm_spot_shadow(vec4 tile, vec2 suv, float zref, float soft) {
	if (soft <= 0.0) {
		return svm_spot_lookup(tile, suv, zref);
	}
	vec2 step_uv = soft / vec2(textureSize(svm_spot_depth, 0)) / tile.zw;
	float sum = 0.0;
	for (int k = 0; k < 8; k++) {
		float a = float(k) * 0.78539816 + 0.39269908;
		sum += svm_spot_lookup(tile, suv + vec2(cos(a), sin(a)) * step_uv, zref);
	}
	return sum * 0.125;
}

// ignore: the surface's Echelon code (v_echelon; game 0x10a37310).
vec3 svm_spot_slot(int i, vec3 wp, vec3 wn, float zone, int ignore) {
	vec4 a = texelFetch(svm_spots, ivec2(3, i), 0);
	if (a.x < 0.5 || (ignore & (a.x > 1.5 ? 2 : 1)) != 0) {
		return vec3(0.0);
	}
	int group = ignore >> 2;
	if (group > 0 && group < textureSize(svm_spot_ignore, 0).y && texelFetch(svm_spot_ignore, ivec2(i, group), 0).r > 0.5) {
		return vec3(0.0);
	}
	vec4 e = texelFetch(svm_spots, ivec2(7, i), 0);
	if (e.x >= 0.0 && zone >= 0.0 && abs(e.x - zone) > 0.5) {
		return vec3(0.0);
	}
	vec4 r0 = texelFetch(svm_spots, ivec2(0, i), 0);
	vec4 r1 = texelFetch(svm_spots, ivec2(1, i), 0);
	vec4 r2 = texelFetch(svm_spots, ivec2(2, i), 0);
	vec4 w = vec4(wp, 1.0);
	vec3 v = vec3(dot(r0, w), dot(r1, w), dot(r2, w));
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
	float shadow = svm_spot_shadow(texelFetch(svm_spots, ivec2(6, i), 0), suv, z - e.y - e.z * z, e.w);
	if (shadow <= 0.0) {
		return vec3(0.0);
	}
	vec3 cookie = texture(svm_spot_cookies, vec3(suv, f.w)).rgb;
	float atten = a.w < 0.5 ? 1.0 : texture(svm_spot_atten, vec2((z - c.a) / a.y, a.w < 1.5 ? 0.25 : 0.75)).r;
	// N.L towards the apex (parallel rays: along the axis), in the light's view space.
	vec3 nv = vec3(dot(r0.xyz, wn), dot(r1.xyz, wn), dot(r2.xyz, wn));
	vec3 l = f.z > 0.5 ? vec3(0.0, 0.0, 1.0) : normalize(-v);
	float ndl = max(0.0, dot(nv, l));
	return c.rgb * cookie * atten * mix(1.0, ndl, a.z) * shadow;
}

// Every spot shadow light at a surface point; zone is the surface's zone (-1 unknown: own-zone lights still reach it),
// ignore its Echelon flags (svm_spot_slot).
vec3 svm_spot_light(vec3 wp, vec3 wn, float zone, int ignore) {
	vec3 sum = vec3(0.0);
	for (int i = 0; i < svm_spot_count; i++) {
		sum += svm_spot_slot(i, wp, wn, zone, ignore);
	}
	return sum;
}
#endif

#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX)
// Dynamic world lights (triggered and pulsing lights that are not in the baked light), one row each of a float
// texture: position, colour, spot, own zone (or -1). They add to the baked light term before the 2x saturation.
// The vertex colour's alpha holds the surface's zone index / 255.
global uniform sampler2D svm_dyn_lights;
global uniform int svm_dyn_light_count;
// Calibration of the dynamic world lights: x gain, y radius scale, z falloff (0 smoothstep, 1 1-x^2, 2 1-x,
// 3 (1-x)^2), w 1 to honour bAffectOwnZoneOnly on world surfaces. (1, 1, 0, 1) is the decoded actor model.
global uniform vec4 svm_dyn_tune;

vec3 svm_dynamic(vec3 wp, vec3 wn, float zone) {
	vec3 sum = vec3(0.0);
	for (int i = 0; i < svm_dyn_light_count; i++) {
		float own = texelFetch(svm_dyn_lights, ivec2(3, i), 0).x;
		if (svm_dyn_tune.w > 0.5 && own >= 0.0 && abs(own - zone) > 0.5) {
			continue;
		}
		vec4 p = texelFetch(svm_dyn_lights, ivec2(0, i), 0);
		vec4 c = texelFetch(svm_dyn_lights, ivec2(1, i), 0);
		vec4 s = texelFetch(svm_dyn_lights, ivec2(2, i), 0);
		p.w *= svm_dyn_tune.y;
		sum += svm_one_light(p, c, s, wp, wn, int(svm_dyn_tune.z));
	}
	return sum * svm_dyn_tune.x;
}
#endif

void vertex() {
	v_vision = CUSTOM0.w > 1.5 ? vec4(CUSTOM0.xyz, CUSTOM0.w - 2.0) : svm_vision_actor;
#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX) || defined(LIGHT_ACTOR)
	v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_wnormal = MODEL_NORMAL_MATRIX * NORMAL;
#ifdef LIGHT_ACTOR
	v_echelon = svm_ambient.w;
#else
	v_echelon = CUSTOM1.x;
#endif
#endif
#ifdef LIGHT_ACTOR
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 wn = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	vec3 sum = svm_ambient.rgb;
	sum += svm_one_light(svm_l0_pos, svm_l0_col, svm_l0_spot, wp, wn, 0);
	sum += svm_one_light(svm_l1_pos, svm_l1_col, svm_l1_spot, wp, wn, 0);
	sum += svm_one_light(svm_l2_pos, svm_l2_col, svm_l2_spot, wp, wn, 0);
	sum += svm_one_light(svm_l3_pos, svm_l3_col, svm_l3_spot, wp, wn, 0);
	v_light = min(sum, vec3(2.0));
#endif
}

// One animated texture modifier (the UV_ANIM layout) at the scene time.
vec2 svm_anim(vec2 uv, int type, vec4 a, vec4 b, vec2 size) {
	float t = svm_time;
	if (type == 1) {
		uv += a.xy * a.z * t;
	} else if (type == 2) {
		// OT_Pan wobbles the coordinate; the stretch types scale about the middle.
		float su = a.x * sin(6.2831853 * a.y * t);
		float sv = b.x * sin(6.2831853 * b.y * t);
		uv.x = a.z >= 1.0 ? (uv.x - 0.5) * (1.0 + su) + 0.5 : uv.x + su;
		uv.y = b.z >= 1.0 ? (uv.y - 0.5) * (1.0 + sv) + 0.5 : uv.y + sv;
	} else if (type == 3) {
		vec2 pivot = a.zw / size;
		float r = a.x * t;
		vec2 d = uv - pivot;
		uv = pivot + vec2(d.x * cos(r) - d.y * sin(r), d.x * sin(r) + d.y * cos(r));
	}
	return uv;
}

// A static texture transform: rows dotted with (u, v, 1).
vec2 svm_affine(vec2 uv, vec3 row_u, vec3 row_v) {
	vec3 h = vec3(uv, 1.0);
	return vec2(dot(row_u, h), dot(row_v, h));
}

vec2 svm_uv(vec2 uv) {
#ifdef UV_ANIM
	uv = svm_anim(uv, uv_anim_type, uv_anim_a, uv_anim_b, tex_size);
#endif
	return uv;
}

void fragment() {
#ifdef VISION_SEE_THROUGH
	// The blended pass draws only in thermal and EMF.
	if (svm_vision != 36 && svm_vision != 38) {
		discard;
	}
#endif
	vec2 uv = svm_uv(UV);
#if defined(OPACITY_LAYER) || defined(SPEC_LAYER)
	vec2 raw_uv = svm_affine(UV, base_row_u, base_row_v);
#endif
#if defined(COMBINE)
	// Each input through its own modifiers (after the Combiner's own), then CombineOperation / AlphaOperation.
	vec2 uv1 = svm_anim(svm_affine(uv, c1_row_u, c1_row_v), c1_anim_type, c1_anim_a, c1_anim_b, vec2(textureSize(tex_diffuse, 0)));
	vec2 uv2 = svm_anim(svm_affine(uv, c2_row_u, c2_row_v), c2_anim_type, c2_anim_a, c2_anim_b, vec2(textureSize(tex_c2, 0)));
	vec4 m1 = texture(tex_diffuse, uv1, svm_mip_bias);
	vec4 m2;
	if (c2_env != 0) {
		vec2 euv = reflect(normalize(VERTEX), normalize(NORMAL)).xy * 0.25 + 0.5;
		float f2 = mod(floor(svm_time * c2_flip.w), c2_flip.z);
		vec2 cell2 = vec2(mod(f2, c2_flip.x), floor(f2 / c2_flip.x));
		m2 = textureGrad(tex_c2, (fract(euv) + cell2) / c2_flip.xy, dFdx(euv) / c2_flip.xy, dFdy(euv) / c2_flip.xy);
	} else {
		m2 = texture(tex_c2, uv2, svm_mip_bias);
	}
	vec4 mk = m1;
	if (cmask_on != 0) {
		vec2 uvm = svm_anim(svm_affine(uv, cm_row_u, cm_row_v), cm_anim_type, cm_anim_a, cm_anim_b, vec2(textureSize(tex_cmask, 0)));
		mk = texture(tex_cmask, uvm, svm_mip_bias);
	}
	float ma = combine.w != 0 ? 1.0 - mk.a : mk.a;
	vec3 crgb = m1.rgb;
	if (combine.x == 1) {
		crgb = m2.rgb;
	} else if (combine.x == 2) {
		crgb = m1.rgb * m2.rgb;
	} else if (combine.x == 3) {
		crgb = m1.rgb + m2.rgb;
	} else if (combine.x == 4) {
		crgb = m1.rgb - m2.rgb;
	} else if (combine.x == 5) {
		crgb = mix(m2.rgb, m1.rgb, ma);
	} else if (combine.x == 6) {
		crgb = m1.rgb + m2.rgb * ma;
	} else if (combine.x == 7) {
		crgb = mk.rgb;
	}
	float ca = combine.y == 1 ? m1.a * m2.a : combine.y == 2 ? m1.a + m2.a : combine.y == 3 ? m1.a : combine.y == 4 ? m2.a : ma;
	vec4 t = clamp(vec4(crgb * float(combine.z), ca), 0.0, 1.0);
#elif defined(FLIPBOOK)
	// The frame of the scene time, one atlas cell; gradients from the tiled UVs keep the mip level across cell edges.
	float frame = mod(floor(svm_time * flipbook.w), flipbook.z);
	vec2 cell = vec2(mod(frame, flipbook.x), floor(frame / flipbook.x));
	vec2 fuv = (fract(uv) + cell) / flipbook.xy;
	vec4 t = textureGrad(tex_diffuse, fuv, dFdx(uv) / flipbook.xy, dFdy(uv) / flipbook.xy);
#else
	vec4 t = texture(tex_diffuse, uv, svm_mip_bias);
#endif
#ifdef OPACITY_LAYER
	vec2 opuv = svm_anim(svm_affine(raw_uv, op_row_u, op_row_v), op_anim_type, op_anim_a, op_anim_b, vec2(textureSize(tex_opacity, 0)));
	vec4 op = texture(tex_opacity, opuv, svm_mip_bias);
	t.a = op_lum != 0 ? dot(op.rgb, vec3(0.299, 0.587, 0.114)) : op.a;
#endif
	if (svm_tint.a > 0.0) {
		const vec3 luma = vec3(0.3, 0.59, 0.11);
		vec3 recolour = svm_tint.rgb * (dot(t.rgb, luma) / max(dot(svm_tint.rgb, luma), 0.05));
		t.rgb = mix(t.rgb, clamp(recolour, 0.0, 1.0), svm_tint.a);
	}
	if (svm_glow_hue.z > 0.0) {
		t.rgb = clamp(svm_hue_turn(t.rgb, svm_glow_hue.x), 0.0, 1.0);
	}
#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX) || defined(LIGHT_ACTOR)
	vec3 wn = normalize(v_wnormal) * (FRONT_FACING ? 1.0 : -1.0);
#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX)
	float zone = floor(COLOR.a * 255.0 + 0.5);
#else
	float zone = -1.0;
#endif
	int echelon_ignore = int(v_echelon + 0.5);
	// Zone 255 (MapBuilder.AuthoredOverlayZone): an additive mesh with painted colours, lit by its colours alone.
	bool overlay = zone > 254.5;
	vec3 spot = svm_spot_count > 0 && !overlay ? svm_spot_light(v_wpos, wn, zone, echelon_ignore) : vec3(0.0);
#endif
#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX)
	vec3 dyn = (svm_dyn_light_count > 0 && !overlay ? svm_dynamic(v_wpos, wn, zone) : vec3(0.0)) + spot;
#endif
#if defined(LIGHT_LIGHTMAP)
	vec3 lit = t.rgb * min(texture(tex_lightmap, UV2).rgb * lightmap_scale + vec3(svm_vision_ambient) + dyn, vec3(2.0));
#elif defined(LIGHT_VERTEX)
	// COLOR holds half the light term (min(2, 2 x VC + glow) / 2), so the 2x stage is restored here. The overlay takes
	// no vision ambient either: night vision's lift would fill its faded rim back to a square.
	vec3 lit = t.rgb * min(COLOR.rgb * 2.0 + dyn + vec3(overlay ? 0.0 : svm_vision_ambient), vec3(2.0));
#elif defined(LIGHT_ACTOR)
	vec3 lit = t.rgb * min(v_light + spot + vec3(svm_vision_ambient), vec3(2.0));
#else
	vec3 lit = t.rgb;
#endif
	vec3 c = min(lit, vec3(1.0));
#ifdef SELF_ILLUM
	vec2 siuv = uv + si_pan * svm_time;
	vec4 si = texture(tex_self_illum, siuv);
#ifdef SELF_ILLUM_MASK
	float m = texture(tex_self_illum_mask, siuv).a;
#else
	float m = si.a;
#endif
	vec3 glow = svm_glow_hue.y > 0.0 ? clamp(svm_hue_turn(si.rgb, svm_glow_hue.x), 0.0, 1.0) : si.rgb;
	c = mix(c, glow, m);
#endif
#ifdef ENV_COLOR
#ifdef SPEC_LAYER
	vec2 smuv = svm_anim(svm_affine(raw_uv, sm_row_u, sm_row_v), sm_anim_type, sm_anim_a, sm_anim_b, vec2(textureSize(tex_spec_mask, 0)));
#else
	vec2 smuv = uv;
#endif
#ifdef ENV_TEX
	vec3 er = reflect(normalize(VERTEX), normalize(NORMAL));
	vec3 env_added = texture(tex_env, er.xy * 0.25 + 0.5).rgb;
#else
	vec3 env_added = env_color;
#endif
	c = min(c + env_added * texture(tex_spec_mask, smuv).a, vec3(1.0));
#endif
	if (svm_sheen > 0.0) {
		// Polished metal or lacquer: mostly a fake studio reflection of the texture (dark floor, bright sky, a hot strip
		// at the horizon) instead of its diffuse lighting, two highlights, a Fresnel rim and a slow glint across the
		// surface. All in view space. A partial mirror keeps some of the local light level, so it darkens in shadow; a
		// full mirror (the golden camo) ignores it and adds soft bright and dark streaks to the environment, so the
		// reflection slides over the metal as the gun moves.
		vec3 n = normalize(NORMAL);
		vec3 v = normalize(VIEW);
		vec3 r = reflect(-v, n);
		float ndv = clamp(dot(n, v), 0.0, 1.0);
		float level = clamp(dot(c, vec3(1.0)) / max(dot(t.rgb, vec3(1.0)), 0.03), 0.0, 1.0);
		float shade = mix(mix(0.3, 1.0, level), 1.0, svm_sheen_mirror);
		float env = mix(0.3, 1.25, smoothstep(-0.45, 0.9, r.y)) + 0.85 * exp(-pow((r.y - 0.12) * 7.0, 2.0));
		float streaks = 0.35 * sin(r.x * 7.0 + r.y * 2.5) + 0.2 * sin(r.x * 17.0 - r.z * 5.0)
			+ 0.7 * pow(clamp(dot(r, normalize(vec3(-0.7, 0.45, 0.4))), 0.0, 1.0), 6.0);
		env = max(env + streaks * svm_sheen_mirror, 0.15);
		vec3 refl = t.rgb * env * shade;
		float spec = pow(clamp(dot(r, normalize(vec3(0.35, 0.8, 0.5))), 0.0, 1.0), 48.0);
		float spec2 = pow(clamp(dot(r, normalize(vec3(-0.6, 0.3, 0.7))), 0.0, 1.0), 16.0);
		float rim = pow(1.0 - ndv, 4.0);
		float band = fract((uv.x + uv.y) * 0.5 - svm_time * 0.18);
		float glint = smoothstep(0.0, 0.035, band) * (1.0 - smoothstep(0.035, 0.09, band));
		vec3 metal = mix(c, refl, mix(0.75, 0.95, svm_sheen_mirror) * svm_sheen);
		vec3 shine = svm_sheen_tint * (spec * 1.8 + spec2 * 0.5 + rim * 0.6 + glint * 0.5) * shade * svm_sheen;
		c = min(metal + shine, vec3(1.0));
	}
	float a = t.a;
#if defined(BLEND_MODULATE)
	c = t.rgb * 2.0;
	a = 1.0;
#elif defined(BLEND_DARKEN)
	c = vec3(1.0) - t.rgb;
	a = 1.0;
#elif defined(BLEND_ADDITIVE)
	a = 1.0;
#endif
	if (svm_vision == 36) {
		// Thermal Z pass: heat in G, the depth fade in B. Additive, modulating and darkening surfaces are left out.
#if defined(BLEND_ADDITIVE) || defined(BLEND_MODULATE) || defined(BLEND_DARKEN)
		discard;
#else
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		// HeatOpacity 0 leaves the actor out of the thermal frame (as EMFOpacity does in EMF).
		if (v_vision.y <= 0.0) {
			discard;
		}
		bool partial_heat = v_vision.y < 1.0;
#if defined(VISION_SEE_THROUGH)
		if (!partial_heat) {
			discard;
		}
#else
		if (vision_see_through && partial_heat) {
			discard;
		}
#endif
#if defined(BLEND_TRANSLUCENT)
		c = vec3(0.0, 0.0, fade);
		a = t.a * v_vision.y;
#else
		float heat = v_vision.x;
		if (heat_mode != 0) {
			vec4 h = texture(tex_heat, uv);
			float l = dot(h.rgb, vec3(0.3, 0.59, 0.11));
			heat = heat_mode == 1 ? 4.0 * l * v_vision.x : l + v_vision.x;
		}
		c = vec3(0.0, clamp(heat, 0.0, 1.0), fade);
#if defined(VISION_SEE_THROUGH)
		// A partly see-through solid surface: its heat and fade over what lies behind, by HeatOpacity.
		a = v_vision.y;
#endif
#endif
#endif
	} else if (svm_vision == 38) {
		// EMF: the environment map by camera-space reflection, darkened by the actor's EMF glow (the post pass inverts).
#if defined(BLEND_ADDITIVE) || defined(BLEND_MODULATE) || defined(BLEND_DARKEN)
		discard;
#else
		float opacity = v_vision.w;
		// EMFOpacity 0 hides the actor; partial opacity is blended by the see-through pass where the material has one,
		// else drawn as a 4 x 4 screen-door pattern (actors built outside the map: pawn weapons, gadgets).
		const float bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
		ivec2 q = ivec2(mod(FRAGCOORD.xy, 4.0));
		float threshold = (bayer[q.y * 4 + q.x] + 0.5) / 16.0;
		bool partial_emf = opacity < 0.99;
#if defined(VISION_SEE_THROUGH)
		// A partly see-through solid surface: blended over what lies behind, by EMFOpacity.
		if (opacity <= 0.0 || !partial_emf) {
			discard;
		}
		a = opacity;
#else
		if (opacity <= 0.0 || (vision_see_through && partial_emf) || (partial_emf && threshold >= opacity)) {
			discard;
		}
#endif
		vec3 r = reflect(normalize(VERTEX), normalize(NORMAL));
		vec3 env = texture(svm_emf_env, r.xy * 0.25 + 0.5).rgb;
		c = env * (1.0 - clamp(v_vision.z, 0.0, 1.0));
		if (emf_mode != 0) {
			vec3 e = texture(tex_emf, uv).rgb;
			c *= emf_mode == 2 ? vec3(1.0) - e : e;
		}
#endif
	}
	ALBEDO = c;
#if defined(SEE_THROUGH_CUT)
	// A masked material's blended pass keeps the alpha cut of its solid pass.
	if (t.a < 0.5) {
		discard;
	}
#endif
#if defined(BLEND_TRANSLUCENT) || defined(BLEND_MASKED) || defined(BLEND_ADDITIVE) || defined(BLEND_MODULATE) || defined(BLEND_DARKEN) || defined(VISION_SEE_THROUGH)
	ALPHA = a;
#endif
#if defined(BLEND_MASKED)
	ALPHA_SCISSOR_THRESHOLD = 0.5;
#if defined(ALPHA_COVERAGE)
	// MSAA on (rl:gfx.aa): alpha to coverage over a band about a texel wide around the cut, as Godot's own materials.
	ALPHA_ANTIALIASING_EDGE = 0.3;
	ALPHA_TEXTURE_COORDINATE = uv * vec2(textureSize(tex_diffuse, 0));
#endif
#endif
}
