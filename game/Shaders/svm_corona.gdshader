// A light's corona reading Godot's screen and depth textures (svm_corona.gdshaderinc). Coronas draw with it until
// CoronaSampleEffect's copies match the frame's size; svm_corona_copies.gdshader draws the same picture after that.
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, skip_vertex_transform;
#include "res://Shaders/svm_corona.gdshaderinc"
