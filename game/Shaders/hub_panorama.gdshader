shader_type sky;
render_mode disable_fog;
uniform sampler2D panorama : filter_linear, repeat_enable;
void sky() {
    vec3 d = normalize(EYEDIR);
    vec2 uv = vec2(atan(d.x, d.z) / 6.2831853 + 0.5, acos(clamp(d.y, -1.0, 1.0)) / 3.14159265);
    vec3 light = texture(panorama, uv).rgb * 0.7;
    // Tonemap the photographic HDRI to the display-space range used by the rest of the Hub.
    COLOR = min(pow(light / (vec3(1.0) + light), vec3(1.0 / 2.2)), vec3(0.96));
}
