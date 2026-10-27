// Faithful material template (docs/RENDERING.md#materials). Every value written here is a display value, the
// number the original's framebuffer would hold; the display-space post pass turns the frame into linear light at
// the end. The includer sets render_mode and one define of each group:
//   light:  LIGHT_LIGHTMAP | LIGHT_VERTEX | LIGHT_ACTOR | (none: fullbright)
//   blend:  BLEND_MASKED | BLEND_TRANSLUCENT | BLEND_ADDITIVE | BLEND_MODULATE | BLEND_DARKEN | (none: opaque)
//   extras: SELF_ILLUM, SELF_ILLUM_MASK, ENV_COLOR, UV_ANIM
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
// golden camo) and the colour of its highlights (gold, or near white for the pyre camo's black lacquer).
uniform float svm_sheen = 0.0;
uniform vec3 svm_sheen_tint = vec3(1.0, 0.8, 0.36);

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
#endif

#ifdef UV_ANIM
// type 1 pan: a.xy direction, a.z rate. type 2 oscillate: a = (U amp, U rate, U type), b = (V amp, V rate, V type).
// type 3 rotate: a.x radians per second, a.z / a.w pivot in texels; tex_size gives the texel scale.
uniform int uv_anim_type = 0;
uniform vec4 uv_anim_a = vec4(0.0);
uniform vec4 uv_anim_b = vec4(0.0);
uniform vec2 tex_size = vec2(256.0);
#endif

#ifdef LIGHT_ACTOR
// Actor lighting (docs/RENDERING.md#actor-lighting): ambient plus the four strongest lights, set per instance in
// the svm_one_light layout below.
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
// cos(phi / 2) or -2. Falloff 1 - 3x^2 + 2x^3 over x = d / R, Lambert, no specular.
vec3 svm_one_light(vec4 pos, vec4 col, vec4 spot, vec3 wp, vec3 wn, int falloff) {
	if (dot(col.rgb, col.rgb) <= 0.0) {
		return vec3(0.0);
	}
	vec3 l;
	float att = 1.0;
	if (pos.w < 0.0) {
		l = normalize(pos.xyz);
	} else {
		vec3 d = pos.xyz - wp;
		float dist = length(d);
		l = d / max(dist, 1e-5);
		float x = dist / max(pos.w, 1e-5);
		if (x >= 1.0) {
			att = 0.0;
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
	return col.rgb * att * max(0.0, dot(wn, l));
}

#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX) || defined(LIGHT_ACTOR)
// The spot shadow light slot (LE_ESpotShadow: the merc torch, docs/RENDERING.md#spot-shadow-lights), per pixel on the
// light stage: colour x cookie x attenuation x shadow, N.L by the normal influence.
global uniform vec4 svm_spot;           // x on, y range (m), z normal influence, w attenuation (0 none, 1 linear, 2 late)
global uniform vec4 svm_spot_colour;    // rgb gain x colour, a near plane (m)
global uniform mat4 svm_spot_view;      // world to the light's view
global uniform vec4 svm_spot_frustum;   // x 1 / tan(half width), y 1 / tan(half height), z bias (m), w bias per metre
global uniform vec4 svm_spot_pos;       // xyz light position (m)
global uniform sampler2D svm_spot_depth;
global uniform sampler2D svm_spot_cookie;
global uniform sampler2D svm_spot_atten;
varying vec3 v_wpos;
varying vec3 v_wnormal;

vec3 svm_spot_light(vec3 wp, vec3 wn) {
	if (svm_spot.x < 0.5) {
		return vec3(0.0);
	}
	vec3 v = (svm_spot_view * vec4(wp, 1.0)).xyz;
	float z = -v.z;
	if (z <= svm_spot_colour.a || z >= svm_spot.y) {
		return vec3(0.0);
	}
	vec2 ndc = vec2(v.x * svm_spot_frustum.x, v.y * svm_spot_frustum.y) / z;
	if (abs(ndc.x) >= 1.0 || abs(ndc.y) >= 1.0) {
		return vec3(0.0);
	}
	vec2 suv = vec2(ndc.x, -ndc.y) * 0.5 + 0.5;
	if (z > texture(svm_spot_depth, suv).r + svm_spot_frustum.z + svm_spot_frustum.w * z) {
		return vec3(0.0);
	}
	vec3 cookie = texture(svm_spot_cookie, suv).rgb;
	float atten = svm_spot.w < 0.5 ? 1.0 : texture(svm_spot_atten, vec2(z / svm_spot.y, svm_spot.w < 1.5 ? 0.25 : 0.75)).r;
	float ndl = max(0.0, dot(wn, normalize(svm_spot_pos.xyz - wp)));
	return svm_spot_colour.rgb * cookie * atten * mix(1.0, ndl, svm_spot.z);
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

vec2 svm_uv(vec2 uv) {
#ifdef UV_ANIM
	float t = svm_time;
	if (uv_anim_type == 1) {
		uv += uv_anim_a.xy * uv_anim_a.z * t;
	} else if (uv_anim_type == 2) {
		// OT_Pan wobbles the coordinate; the stretch types scale about the middle.
		float su = uv_anim_a.x * sin(6.2831853 * uv_anim_a.y * t);
		float sv = uv_anim_b.x * sin(6.2831853 * uv_anim_b.y * t);
		uv.x = uv_anim_a.z >= 1.0 ? (uv.x - 0.5) * (1.0 + su) + 0.5 : uv.x + su;
		uv.y = uv_anim_b.z >= 1.0 ? (uv.y - 0.5) * (1.0 + sv) + 0.5 : uv.y + sv;
	} else if (uv_anim_type == 3) {
		vec2 pivot = uv_anim_a.zw / tex_size;
		float a = uv_anim_a.x * t;
		vec2 d = uv - pivot;
		uv = pivot + vec2(d.x * cos(a) - d.y * sin(a), d.x * sin(a) + d.y * cos(a));
	}
#endif
	return uv;
}

void fragment() {
	vec2 uv = svm_uv(UV);
	vec4 t = texture(tex_diffuse, uv, svm_mip_bias);
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
	vec3 spot = svm_spot_light(v_wpos, wn);
#endif
#if defined(LIGHT_LIGHTMAP) || defined(LIGHT_VERTEX)
	vec3 dyn = (svm_dyn_light_count > 0 ? svm_dynamic(v_wpos, wn, floor(COLOR.a * 255.0 + 0.5)) : vec3(0.0)) + spot;
#endif
#if defined(LIGHT_LIGHTMAP)
	vec3 lit = t.rgb * min(texture(tex_lightmap, UV2).rgb * lightmap_scale + vec3(svm_vision_ambient) + dyn, vec3(2.0));
#elif defined(LIGHT_VERTEX)
	// COLOR holds half the light term (min(2, 2 x VC + glow) / 2), so the 2x stage is restored here.
	vec3 lit = t.rgb * min(COLOR.rgb * 2.0 + dyn + vec3(svm_vision_ambient), vec3(2.0));
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
	c = min(c + env_color * texture(tex_spec_mask, uv).a, vec3(1.0));
#endif
	if (svm_sheen > 0.0) {
		// Polished metal or lacquer: mostly a fake studio reflection of the texture (dark floor, bright sky, a hot strip
		// at the horizon) instead of its diffuse lighting, two highlights, a Fresnel rim and a slow glint across the
		// surface. All in view space. The reflection keeps some of the local light level, so the surface still darkens
		// in shadow instead of glowing.
		vec3 n = normalize(NORMAL);
		vec3 v = normalize(VIEW);
		vec3 r = reflect(-v, n);
		float ndv = clamp(dot(n, v), 0.0, 1.0);
		float level = clamp(dot(c, vec3(1.0)) / max(dot(t.rgb, vec3(1.0)), 0.03), 0.0, 1.0);
		float shade = mix(0.3, 1.0, level);
		float env = mix(0.3, 1.25, smoothstep(-0.45, 0.9, r.y)) + 0.85 * exp(-pow((r.y - 0.12) * 7.0, 2.0));
		vec3 refl = t.rgb * env * shade;
		float spec = pow(clamp(dot(r, normalize(vec3(0.35, 0.8, 0.5))), 0.0, 1.0), 48.0);
		float spec2 = pow(clamp(dot(r, normalize(vec3(-0.6, 0.3, 0.7))), 0.0, 1.0), 16.0);
		float rim = pow(1.0 - ndv, 4.0);
		float band = fract((uv.x + uv.y) * 0.5 - svm_time * 0.18);
		float glint = smoothstep(0.0, 0.035, band) * (1.0 - smoothstep(0.035, 0.09, band));
		vec3 metal = mix(c, refl, 0.75 * svm_sheen);
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
#endif
#endif
	} else if (svm_vision == 38) {
		// EMF: the environment map by camera-space reflection, darkened by the actor's EMF glow (the post pass inverts).
#if defined(BLEND_ADDITIVE) || defined(BLEND_MODULATE) || defined(BLEND_DARKEN)
		discard;
#else
		float opacity = v_vision.w;
		// EMFOpacity 0 hides the actor; partial opacity is drawn as a 4 x 4 screen-door pattern.
		const float bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
		ivec2 q = ivec2(mod(FRAGCOORD.xy, 4.0));
		float threshold = (bayer[q.y * 4 + q.x] + 0.5) / 16.0;
		if (opacity <= 0.0 || (opacity < 0.99 && threshold >= opacity)) {
			discard;
		}
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
#if defined(BLEND_TRANSLUCENT) || defined(BLEND_MASKED) || defined(BLEND_ADDITIVE) || defined(BLEND_MODULATE) || defined(BLEND_DARKEN)
	ALPHA = a;
#endif
#if defined(BLEND_MASKED)
	ALPHA_SCISSOR_THRESHOLD = 0.5;
#endif
}
