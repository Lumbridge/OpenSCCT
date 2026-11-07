shader_type spatial;
render_mode unshaded, cull_disabled;

// The Faithful compositor expects display values. Imported PBR materials otherwise undergo a second sRGB decode.
uniform sampler2D diffuse : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform sampler2D normal_map : filter_linear_mipmap_anisotropic, repeat_enable, hint_normal;
uniform bool has_normal = false;
uniform vec4 tint = vec4(1.0);
uniform float alpha_cut = 0.0;
global uniform int svm_vision;
global uniform sampler2D svm_emf_env;
#include "res://Shaders/svm_fog.gdshaderinc"

void fragment() {
    vec4 texel = texture(diffuse, UV) * tint;
    if (texel.a < alpha_cut) discard;
    vec3 n = NORMAL;
    if (has_normal) {
        vec3 detail = texture(normal_map, UV).rgb * 2.0 - 1.0;
        n = normalize(TANGENT * detail.x + BINORMAL * detail.y + NORMAL * detail.z);
    }
    vec3 world_n = normalize((INV_VIEW_MATRIX * vec4(n, 0.0)).xyz);
    float sunlight = max(dot(world_n, normalize(vec3(-0.6, 0.8, 0.35))), 0.0);
    vec3 colour = texel.rgb * (0.72 + 0.28 * sunlight);
    if (svm_vision == 36) colour = vec3(0.0, 0.0, 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0));
    if (svm_vision == 38) {
        vec3 r = reflect(normalize(VERTEX), normalize(NORMAL));
        colour = texture(svm_emf_env, vec2(0.25 * (r.x + r.y) + 0.5, 0.5 * r.y + 0.5)).rgb;
    }
    vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
    ALBEDO = colour * fog.a + fog.rgb;
}
