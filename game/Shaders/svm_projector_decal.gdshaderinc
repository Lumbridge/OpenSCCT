// CPU-clipped opaque receiver triangles preserve perspective UVs without a scene depth read. COLOR.r is geometric facing.
global uniform int svm_vision;
#include "res://Shaders/svm_fog.gdshaderinc"
uniform sampler2D proj_tex : filter_linear_mipmap, repeat_enable;
uniform bool proj_luminance = false;
uniform vec3 proj_origin;
uniform vec3 proj_x;
uniform vec3 proj_y;
uniform vec3 proj_z;
uniform float proj_inverse_apex = 0.0;
uniform bool proj_backfaces = false;
varying vec3 v_projection;
varying vec3 v_receiver;

void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 d = world - proj_origin;
    v_projection = vec3(dot(d, proj_y), dot(d, proj_z), 1.0 + dot(d, proj_x) * proj_inverse_apex);
    v_receiver = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
    // Pull along the eye ray by 0.05 mm to avoid depth fighting without changing screen XY or receiver fog depth.
    vec3 view = v_receiver * (1.0 - 0.00005 / max(length(v_receiver), 0.001));
    VERTEX = v_receiver;
    POSITION = PROJECTION_MATRIX * vec4(view, 1.0);
}

void fragment() {
    if (svm_vision == 36 || svm_vision == 38 || (!proj_backfaces && FRONT_FACING != (COLOR.r > 0.5))) discard;
    vec2 uv = v_projection.xy / v_projection.z + vec2(0.5);
    vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, v_receiver, FRAGCOORD.xy, VIEWPORT_SIZE);
    ALBEDO = mix(vec3(254.0 / 255.0), (proj_luminance ? texture(proj_tex, uv).rrr : texture(proj_tex, uv).rgb) * 2.0, fog.a);
    ALPHA = 1.0;
}
