shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, skip_vertex_transform;
#include "res://Shaders/svm_responsive_smoke.gdshaderinc"
void vertex() { POSITION = vec4(VERTEX.xy, 0.0, 1.0); }
void fragment() {
    vec4 smoke = responsive_smoke_background(CAMERA_VISIBLE_LAYERS, SCREEN_UV);
    ALBEDO = smoke.rgb / max(1.0-smoke.a,0.00001);
    ALPHA = 1.0-smoke.a;
}
