// UE2 particles (spec/native/emitters.md rule 21): world-space quads and lines built every frame. UV is the
// particle's texture cell, UV2 the next cell when the emitter blends between cells (the vertex alpha is then the
// blend weight), COLOR the particle colour (bytes / 255, already faded as the draw style wants). Texture x colour, as
// D3D's MODULATE stage. The includer sets render_mode and one STYLE_* define (blend factors of SetParticleMaterial):
//   STYLE_REGULAR (ONE, ZERO, alpha test), STYLE_ALPHA (SRCALPHA, INVSRCALPHA), STYLE_MODULATE (DESTCOLOR, SRCCOLOR:
//   twice the product; the texture blended toward the vertex colour by the vertex alpha), STYLE_ADD (ONE, ONE),
//   STYLE_PREMUL (ONE, INVSRCALPHA), STYLE_DARKEN (ZERO, INVSRCCOLOR), STYLE_BRIGHTEN (ONE, INVSRCCOLOR; drawn
//   premultiplied with the brightest channel as coverage). PARTICLE_MESH: a mesh emitter drawn with the particle
//   style (instance colour). Thermal draws the translucent rule (0, 0, fade) by alpha x HeatOpacity for alpha styles
//   and nothing for the others; EMF draws nothing.

global uniform int svm_vision;

uniform sampler2D tex_sprite : filter_linear_mipmap, repeat_disable, hint_default_white;
uniform bool blend_cells = false;
uniform float alpha_ref = -1.0;
uniform float heat_opacity = 1.0;

void fragment() {
	vec4 t = texture(tex_sprite, UV);
	vec4 v = COLOR;
	if (blend_cells) {
		t = mix(t, texture(tex_sprite, UV2), v.a);
		v.a = 1.0;
	}
#if defined(STYLE_MODULATE)
	vec4 c = vec4(mix(v.rgb, t.rgb, v.a), 1.0);
#else
	vec4 c = t * v;
#endif
	if (svm_vision == 38) {
		discard;
	}
	if (svm_vision == 36) {
#if defined(STYLE_ALPHA) || defined(STYLE_PREMUL) || defined(STYLE_REGULAR)
		float fade = 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0);
		ALBEDO = vec3(0.0, 0.0, fade);
		ALPHA = c.a * heat_opacity;
#else
		discard;
#endif
	} else {
#if defined(STYLE_ADD)
		ALBEDO = c.rgb;
		ALPHA = 1.0;
#elif defined(STYLE_MODULATE)
		ALBEDO = c.rgb * 2.0;
		ALPHA = 1.0;
#elif defined(STYLE_DARKEN)
		ALBEDO = vec3(1.0) - c.rgb;
		ALPHA = 1.0;
#elif defined(STYLE_PREMUL)
		ALBEDO = c.rgb;
		ALPHA = c.a;
#elif defined(STYLE_BRIGHTEN)
		ALBEDO = c.rgb;
		ALPHA = max(c.r, max(c.g, c.b));
#elif defined(STYLE_REGULAR)
		if (alpha_ref >= 0.0 && c.a <= alpha_ref) {
			discard;
		}
		ALBEDO = c.rgb;
#else
		ALBEDO = c.rgb;
		ALPHA = c.a;
#endif
	}
}
