vec3 svm_g16_factor(float transmittance, vec3 eye) {
    // Derivatives precede divergent range/vision tests and use the actual deformed/skinned surface.
    vec3 normal = cross(dFdx(v_g16_world), dFdy(v_g16_world));
    if (dot(normal, eye - v_g16_world) < 0.0) normal = -normal;
    vec3 delta = v_g16_world - svm_g16_origin.xyz;
    float depth = dot(delta, svm_g16_x.xyz);
    float scale = 1.0 + depth * svm_g16_shape.z;
    vec2 uv = vec2(dot(delta, svm_g16_y.xyz), dot(delta, svm_g16_z.xyz)) * svm_g16_shape.xy / scale + vec2(0.5);
    if (!svm_g16_active || depth < 0.0 || depth > svm_g16_origin.w ||
        uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 ||
        (svm_g16_shape.w < 0.5 && dot(normal, -svm_g16_x.xyz) <= 0.0)) return vec3(1.0);
    vec4 sample_value = texture(svm_g16_texture, uv);
    vec3 color = svm_g16_x.w > 0.5 ? sample_value.rrr : sample_value.rgb;
    return mix(vec3(254.0 / 255.0), color * 2.0, transmittance);
}
