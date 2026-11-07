shader_type sky;
render_mode disable_fog;

// Original daylight sky: soft cloud bands, warm sun and distant mountain silhouettes.
float cloud_density(vec2 p) {
    // Continuous domain-warped waves avoid cell seams and large sine-hash precision errors on GPUs.
    p += vec2(sin(p.y * 0.73 + sin(p.x * 0.41)), cos(p.x * 0.61)) * 1.7;
    return 0.5 + sin(p.x * 0.83 + p.y * 0.27) * 0.22
        + sin(p.y * 1.17 - p.x * 0.38) * 0.16
        + sin(p.x * 2.13 + sin(p.y * 1.71)) * 0.08
        + cos(p.y * 3.41 + p.x * 1.28) * 0.04;
}

void sky() {
    vec3 d = normalize(EYEDIR);
    float elevation = max(d.y, 0.0);
    vec3 colour = mix(vec3(0.66, 0.76, 0.81), vec3(0.12, 0.31, 0.55), pow(elevation, 0.48));
    vec2 cloud_uv = d.xz / max(d.y + 0.18, 0.08) * 3.0;
    float cloud = cloud_density(cloud_uv);
    float veil = smoothstep(0.48, 0.82, cloud) * smoothstep(0.02, 0.22, d.y);
    colour = mix(colour, vec3(0.91, 0.92, 0.88), veil * 0.82);
    float sun = max(dot(d, normalize(vec3(-0.6, 0.48, 0.32))), 0.0);
    colour += vec3(0.22, 0.17, 0.09) * pow(sun, 40.0);
    colour = mix(colour, vec3(1.0, 0.94, 0.76), smoothstep(0.9994, 0.9998, sun));
    float azimuth = atan(d.x, d.z);
    float ridge = 0.012 + pow(abs(sin(azimuth * 3.0 + 0.8)), 3.0) * 0.045 + abs(sin(azimuth * 11.0)) * 0.014;
    colour = mix(colour, vec3(0.38, 0.48, 0.50), 1.0 - smoothstep(ridge - 0.003, ridge, d.y));
    colour = mix(colour, vec3(0.28, 0.36, 0.31), 1.0 - smoothstep(-0.02, 0.0, d.y));
    COLOR = colour;
}
