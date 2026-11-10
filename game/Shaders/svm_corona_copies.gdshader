// A light's corona reading CoronaSampleEffect's copies of the scene (svm_corona.gdshaderinc).
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, skip_vertex_transform;
#define SVM_CORONA_COPIES
#include "res://Shaders/svm_corona.gdshaderinc"
