shader_type spatial;
render_mode unshaded, cull_disabled;

// Display-space colour matches the Faithful compositor and imported Hub scenery.
uniform sampler2D diffuse : filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec4 tint = vec4(1.0);
uniform float fade_end = 88.0;
uniform float wind_strength = 0.09;
global uniform int svm_vision;
global uniform sampler2D svm_emf_env;
#include "res://Shaders/svm_fog.gdshaderinc"

void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float fade = 1.0 - smoothstep(fade_end - 20.0, fade_end, distance(world, CAMERA_POSITION_WORLD));
    float rooted = max(VERTEX.y, 0.0);
    VERTEX.x += sin(TIME * 1.7 + world.x * 0.8 + world.z * 0.65) * rooted * rooted * wind_strength;
    VERTEX.y *= fade;
}

void fragment() {
    vec4 texel = texture(diffuse, UV) * tint * COLOR;
    if (texel.a < 0.38) discard;
    vec3 colour = texel.rgb * (0.88 + 0.12 * (1.0 - UV.y));
    if (svm_vision == 36) colour = vec3(0.0, 0.0, 1.0 - clamp(-VERTEX.z * 100.0 / 5000.0, 0.0, 1.0));
    if (svm_vision == 38) {
        vec3 r = reflect(normalize(VERTEX), normalize(NORMAL));
        colour = texture(svm_emf_env, vec2(0.25 * (r.x + r.y) + 0.5, 0.5 * r.y + 0.5)).rgb;
    }
    vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
    ALBEDO = colour * fog.a + fog.rgb;
}
