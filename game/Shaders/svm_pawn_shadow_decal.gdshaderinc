global uniform int svm_vision;
#include "res://Shaders/svm_fog.gdshaderinc"
uniform sampler2D shadow_mask : filter_linear, repeat_disable;
uniform vec3 shadow_origin;
uniform vec3 shadow_forward;
uniform vec3 shadow_u;
uniform vec3 shadow_v;
uniform float shadow_darkness;
uniform float shadow_range = 3.0;
uniform float shadow_cull = 6.0;
varying vec3 v_shadow_world;
varying vec3 v_shadow_view;

void vertex() {
    v_shadow_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_shadow_view = (VIEW_MATRIX * vec4(v_shadow_world, 1.0)).xyz;
    VERTEX = v_shadow_view;
    vec3 biased = v_shadow_view * (1.0 - 0.00005 / max(length(v_shadow_view), 0.001));
    POSITION = PROJECTION_MATRIX * vec4(biased, 1.0);
}

void fragment() {
    vec3 n = cross(dFdx(v_shadow_world), dFdy(v_shadow_world));
    if (dot(n, INV_VIEW_MATRIX[3].xyz - v_shadow_world) < 0.0) n = -n;
    vec3 delta = v_shadow_world - shadow_origin;
    float depth = dot(delta, shadow_forward);
    vec2 uv = vec2(dot(delta, shadow_u), dot(delta, shadow_v)) + vec2(0.5);
    if (svm_vision == 36 || svm_vision == 38 || depth < 0.0 || depth > shadow_range ||
        distance(INV_VIEW_MATRIX[3].xyz, shadow_origin) > shadow_cull || dot(n, -shadow_forward) <= 0.0 ||
        uv.x <= 0.0 || uv.y <= 0.0 || uv.x >= 1.0 || uv.y >= 1.0) discard;
    vec2 step_uv = vec2(1.5 / 128.0);
    float mask = 0.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
        mask += float((2 - abs(x)) * (2 - abs(y))) * texture(shadow_mask, uv + vec2(float(x), float(y)) * step_uv).a;
    vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, v_shadow_view, FRAGCOORD.xy, VIEWPORT_SIZE);
    float strength = shadow_darkness * (1.0 - depth / shadow_range) * mask / 16.0 * fog.a;
    ALBEDO = vec3(1.0 - strength);
    ALPHA = 1.0;
}
