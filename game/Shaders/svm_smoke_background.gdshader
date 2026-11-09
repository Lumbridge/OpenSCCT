shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, skip_vertex_transform;
#include "res://Shaders/svm_responsive_smoke.gdshaderinc"
#include "res://Shaders/svm_sky_depth.gdshaderinc"
// On top of the sky zone's depths (the depth test keeps it where only the sky or nothing was drawn).
void vertex() { POSITION = vec4(VERTEX.xy, SVM_SKY_DEPTH_LIMIT, 1.0); }
void fragment() {
    vec4 smoke = responsive_smoke_background(CAMERA_VISIBLE_LAYERS, SCREEN_UV);
    ALBEDO = smoke.rgb / max(1.0-smoke.a,0.00001);
    ALPHA = 1.0-smoke.a;
}
