// One optical sheet per pane, with its actual triangulated outline in world metres.
global uniform sampler2D svm_window_light_sheets;
global uniform int svm_window_light_triangles;

vec3 svm_window_filter(vec3 from, vec4 light) {
    vec3 ray = light.w < 0.0 ? normalize(light.xyz) * 2000.0 : light.xyz - from;
    float end_margin = 0.0005 / max(length(ray), 0.0005);
    vec3 tint = vec3(1.0);
    int row = 0;
    while (row < svm_window_light_triangles) {
        vec4 sheet = texelFetch(svm_window_light_sheets, ivec2(3, row), 0);
        int next_sheet = row + max(1, int(sheet.a));
        bool hit = false;
        for (int i = row; i < next_sheet; i++) {
            vec3 a = texelFetch(svm_window_light_sheets, ivec2(0, i), 0).xyz;
            vec3 u = texelFetch(svm_window_light_sheets, ivec2(1, i), 0).xyz;
            vec3 v = texelFetch(svm_window_light_sheets, ivec2(2, i), 0).xyz;
            vec3 h = cross(ray, v);
            float det = dot(u, h);
            if (abs(det) < 1e-8) continue;
            vec3 s = from - a;
            float x = dot(s, h) / det;
            vec3 q = cross(s, u);
            float y = dot(ray, q) / det;
            float t = dot(v, q) / det;
            if (x >= -1e-6 && y >= -1e-6 && x + y <= 1.000001 && t > end_margin && t < 1.0 - end_margin) {
                hit = true;
                break;
            }
        }
        // A ray on a triangulation seam still crosses this pane only once.
        if (hit) tint *= sheet.rgb;
        row = next_sheet;
    }
    return tint;
}
