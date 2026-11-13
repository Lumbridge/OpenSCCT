// Unlit gameplay FX retain their StandardMaterial input colour/texture conventions.
#include "res://Shaders/svm_fog.gdshaderinc"
#ifdef EFFECT_HIDDEN_IN_VISION
global uniform int svm_vision;
#endif
uniform vec4 effect_color : source_color = vec4(1.0);
#ifdef EFFECT_DISPLAY_TEXTURE
// The frame holds display values (svm_world.gdshaderinc): this texture is added as stored, not decoded.
uniform sampler2D effect_texture : filter_linear_mipmap, repeat_enable;
#else
uniform sampler2D effect_texture : source_color, filter_linear_mipmap, repeat_enable;
#endif
uniform bool effect_has_texture = false;
uniform vec3 effect_uv_scale = vec3(1.0);
uniform vec3 effect_uv_offset = vec3(0.0);

void vertex() {
    UV = UV * effect_uv_scale.xy + effect_uv_offset.xy;
#ifdef EFFECT_BILLBOARD
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(MAIN_CAM_INV_VIEW_MATRIX[0], MAIN_CAM_INV_VIEW_MATRIX[1],
                                        MAIN_CAM_INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
#endif
#ifdef EFFECT_FIXED_SIZE
    float size_scale = PROJECTION_MATRIX[3][3] != 0.0 ? abs(1.0 / PROJECTION_MATRIX[1][1]) : -MODELVIEW_MATRIX[3].z;
    MODELVIEW_MATRIX[0] *= size_scale;
    MODELVIEW_MATRIX[1] *= size_scale;
    MODELVIEW_MATRIX[2] *= size_scale;
#endif
}

void fragment() {
#ifdef EFFECT_HIDDEN_IN_VISION
    // Thermal and EMF read the frame as heat and field strength: a surface mark has neither.
    if (svm_vision == 36 || svm_vision == 38) {
        discard;
    }
#endif
    vec4 c = effect_color;
    if (effect_has_texture) c *= texture(effect_texture, UV);
#ifdef EFFECT_VERTEX_COLOR
    c *= COLOR;
#endif
    vec4 fog = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
#ifdef EFFECT_ADDITIVE
    c.rgb *= fog.a;
#elif defined(EFFECT_MULTIPLY)
    c.rgb = mix(vec3(1.0), c.rgb, fog.a);
#else
    c.rgb = c.rgb * fog.a + fog.rgb;
#endif
    ALBEDO = c.rgb;
#ifdef EFFECT_ALPHA
    ALPHA = c.a;
#endif
}
