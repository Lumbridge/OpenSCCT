// Only moving receiver materials include this. Static world materials keep their existing programs.
global uniform bool svm_g16_active;
global uniform sampler2D svm_g16_texture : filter_linear_mipmap, repeat_enable;
global uniform vec4 svm_g16_origin;
global uniform vec4 svm_g16_x;
global uniform vec4 svm_g16_y;
global uniform vec4 svm_g16_z;
// xy inverse texture width/height in world units, z inverse apex, w project backfaces.
global uniform vec4 svm_g16_shape;
varying vec3 v_g16_world;
