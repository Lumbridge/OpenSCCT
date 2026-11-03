// MaterialSequence texture inputs, including each frame's own modifiers and AnimNext atlas.
#ifdef SEQUENCE_D
uniform float seq_d_mix = 1.0;
uniform sampler2D seq_d0_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_d0_anim_type = 0;
uniform vec4 seq_d0_anim_a = vec4(0.0);
uniform vec4 seq_d0_anim_b = vec4(0.0);
uniform vec4 seq_d0_anim_c = vec4(0.0);
uniform vec3 seq_d0_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_d0_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_d0_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_d0_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_d0_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_d0_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_d0_row_u, seq_d0_row_v), seq_d0_anim_type,
        seq_d0_anim_a, seq_d0_anim_b, seq_d0_anim_c, vec2(textureSize(seq_d0_tex, 0)) / seq_d0_flip.xy),
        seq_d0_post_u, seq_d0_post_v);
    if (seq_d0_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_d0_flip.w), seq_d0_flip.z);
        vec2 cell = vec2(mod(frame, seq_d0_flip.x), floor(frame / seq_d0_flip.x));
        return textureGrad(seq_d0_tex, (fract(uv) + cell) / seq_d0_flip.xy, dFdx(uv) / seq_d0_flip.xy, dFdy(uv) / seq_d0_flip.xy);
    }
    return texture(seq_d0_tex, uv, svm_mip_bias);
}
uniform sampler2D seq_d1_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_d1_anim_type = 0;
uniform vec4 seq_d1_anim_a = vec4(0.0);
uniform vec4 seq_d1_anim_b = vec4(0.0);
uniform vec4 seq_d1_anim_c = vec4(0.0);
uniform vec3 seq_d1_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_d1_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_d1_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_d1_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_d1_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_d1_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_d1_row_u, seq_d1_row_v), seq_d1_anim_type,
        seq_d1_anim_a, seq_d1_anim_b, seq_d1_anim_c, vec2(textureSize(seq_d1_tex, 0)) / seq_d1_flip.xy),
        seq_d1_post_u, seq_d1_post_v);
    if (seq_d1_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_d1_flip.w), seq_d1_flip.z);
        vec2 cell = vec2(mod(frame, seq_d1_flip.x), floor(frame / seq_d1_flip.x));
        return textureGrad(seq_d1_tex, (fract(uv) + cell) / seq_d1_flip.xy, dFdx(uv) / seq_d1_flip.xy, dFdy(uv) / seq_d1_flip.xy);
    }
    return texture(seq_d1_tex, uv, svm_mip_bias);
}
vec4 seq_d_sample(vec2 uv) { return mix(seq_d0_sample(uv), seq_d1_sample(uv), seq_d_mix); }
#endif
#ifdef SEQUENCE_SI
uniform float seq_si_mix = 1.0;
uniform sampler2D seq_si0_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_si0_anim_type = 0;
uniform vec4 seq_si0_anim_a = vec4(0.0);
uniform vec4 seq_si0_anim_b = vec4(0.0);
uniform vec4 seq_si0_anim_c = vec4(0.0);
uniform vec3 seq_si0_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_si0_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_si0_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_si0_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_si0_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_si0_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_si0_row_u, seq_si0_row_v), seq_si0_anim_type,
        seq_si0_anim_a, seq_si0_anim_b, seq_si0_anim_c, vec2(textureSize(seq_si0_tex, 0)) / seq_si0_flip.xy),
        seq_si0_post_u, seq_si0_post_v);
    if (seq_si0_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_si0_flip.w), seq_si0_flip.z);
        vec2 cell = vec2(mod(frame, seq_si0_flip.x), floor(frame / seq_si0_flip.x));
        return textureGrad(seq_si0_tex, (fract(uv) + cell) / seq_si0_flip.xy, dFdx(uv) / seq_si0_flip.xy, dFdy(uv) / seq_si0_flip.xy);
    }
    return texture(seq_si0_tex, uv, svm_mip_bias);
}
uniform sampler2D seq_si1_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_si1_anim_type = 0;
uniform vec4 seq_si1_anim_a = vec4(0.0);
uniform vec4 seq_si1_anim_b = vec4(0.0);
uniform vec4 seq_si1_anim_c = vec4(0.0);
uniform vec3 seq_si1_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_si1_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_si1_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_si1_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_si1_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_si1_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_si1_row_u, seq_si1_row_v), seq_si1_anim_type,
        seq_si1_anim_a, seq_si1_anim_b, seq_si1_anim_c, vec2(textureSize(seq_si1_tex, 0)) / seq_si1_flip.xy),
        seq_si1_post_u, seq_si1_post_v);
    if (seq_si1_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_si1_flip.w), seq_si1_flip.z);
        vec2 cell = vec2(mod(frame, seq_si1_flip.x), floor(frame / seq_si1_flip.x));
        return textureGrad(seq_si1_tex, (fract(uv) + cell) / seq_si1_flip.xy, dFdx(uv) / seq_si1_flip.xy, dFdy(uv) / seq_si1_flip.xy);
    }
    return texture(seq_si1_tex, uv, svm_mip_bias);
}
vec4 seq_si_sample(vec2 uv) { return mix(seq_si0_sample(uv), seq_si1_sample(uv), seq_si_mix); }
#endif
#ifdef SEQUENCE_SIM
uniform float seq_sim_mix = 1.0;
uniform sampler2D seq_sim0_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_sim0_anim_type = 0;
uniform vec4 seq_sim0_anim_a = vec4(0.0);
uniform vec4 seq_sim0_anim_b = vec4(0.0);
uniform vec4 seq_sim0_anim_c = vec4(0.0);
uniform vec3 seq_sim0_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_sim0_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_sim0_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_sim0_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_sim0_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_sim0_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_sim0_row_u, seq_sim0_row_v), seq_sim0_anim_type,
        seq_sim0_anim_a, seq_sim0_anim_b, seq_sim0_anim_c, vec2(textureSize(seq_sim0_tex, 0)) / seq_sim0_flip.xy),
        seq_sim0_post_u, seq_sim0_post_v);
    if (seq_sim0_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_sim0_flip.w), seq_sim0_flip.z);
        vec2 cell = vec2(mod(frame, seq_sim0_flip.x), floor(frame / seq_sim0_flip.x));
        return textureGrad(seq_sim0_tex, (fract(uv) + cell) / seq_sim0_flip.xy, dFdx(uv) / seq_sim0_flip.xy, dFdy(uv) / seq_sim0_flip.xy);
    }
    return texture(seq_sim0_tex, uv, svm_mip_bias);
}
uniform sampler2D seq_sim1_tex : filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform int seq_sim1_anim_type = 0;
uniform vec4 seq_sim1_anim_a = vec4(0.0);
uniform vec4 seq_sim1_anim_b = vec4(0.0);
uniform vec4 seq_sim1_anim_c = vec4(0.0);
uniform vec3 seq_sim1_row_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_sim1_row_v = vec3(0.0, 1.0, 0.0);
uniform vec3 seq_sim1_post_u = vec3(1.0, 0.0, 0.0);
uniform vec3 seq_sim1_post_v = vec3(0.0, 1.0, 0.0);
uniform vec4 seq_sim1_flip = vec4(1.0, 1.0, 1.0, 0.0);
vec4 seq_sim1_sample(vec2 uv) {
    uv = svm_affine(svm_anim(svm_affine(uv, seq_sim1_row_u, seq_sim1_row_v), seq_sim1_anim_type,
        seq_sim1_anim_a, seq_sim1_anim_b, seq_sim1_anim_c, vec2(textureSize(seq_sim1_tex, 0)) / seq_sim1_flip.xy),
        seq_sim1_post_u, seq_sim1_post_v);
    if (seq_sim1_flip.z > 1.0) {
        float frame = mod(floor(svm_time * seq_sim1_flip.w), seq_sim1_flip.z);
        vec2 cell = vec2(mod(frame, seq_sim1_flip.x), floor(frame / seq_sim1_flip.x));
        return textureGrad(seq_sim1_tex, (fract(uv) + cell) / seq_sim1_flip.xy, dFdx(uv) / seq_sim1_flip.xy, dFdy(uv) / seq_sim1_flip.xy);
    }
    return texture(seq_sim1_tex, uv, svm_mip_bias);
}
vec4 seq_sim_sample(vec2 uv) { return mix(seq_sim0_sample(uv), seq_sim1_sample(uv), seq_sim_mix); }
#endif
