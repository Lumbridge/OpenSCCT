// Rain volume sheets (ERainVolume, docs/RENDERING.md#rain-volumes): the CPU builds the sheets around the camera each
// frame with their texture coordinates (one repeat per m_RainMatScale uu) and grey vertex colour (the sheet's fade); the
// rain material's TexFriendlyPanner moves them here. The includer sets render_mode (blend_add for the additive rain
// materials, blend_mix for the alpha-blended ones). Textures hold display values, sampled raw like the world's.

global uniform float svm_time;
global uniform int svm_vision;

uniform sampler2D rain_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_black;
// TexFriendlyPanner: xy scaleU / scaleV, zw PanU x PanRate / PanV x PanRate per second.
uniform vec4 rain_pan = vec4(1.0, 1.0, 0.0, 0.0);

void fragment() {
	// Thermal and EMF leave out additive and translucent world surfaces; the volume's HeatOpacity and EMFOpacity are 0.
	if (svm_vision == 36 || svm_vision == 38) {
		discard;
	}
	vec2 uv = UV * rain_pan.xy + rain_pan.zw * svm_time;
	vec4 t = texture(rain_tex, uv);
	ALBEDO = t.rgb * COLOR.rgb;
#ifdef RAIN_ALPHA
	ALPHA = t.a;
#else
	ALPHA = 1.0;
#endif
}
