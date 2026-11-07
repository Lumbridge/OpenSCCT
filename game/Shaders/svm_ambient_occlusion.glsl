#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(set = 0, binding = 1) uniform sampler2D depth_texture;
layout(push_constant, std430) uniform Params {
    vec4 frame; // size, radius in metres, strength
    vec4 projection; // focal scales and asymmetric frustum offsets
    vec4 range; // near, far, near exclusion, distance fade
} p;

vec3 position_at(ivec2 pixel) {
    pixel = clamp(pixel, ivec2(0), ivec2(p.frame.xy) - 1);
    float d = texelFetch(depth_texture, pixel, 0).r;
    float z = p.range.x * p.range.y / (d * (p.range.y - p.range.x) + p.range.x);
    vec2 uv = (vec2(pixel) + .5) / p.frame.xy;
    vec2 ndc = vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    return vec3((ndc + p.projection.zw) / p.projection.xy * z, -z);
}

void main() {
    ivec2 xy = ivec2(gl_GlobalInvocationID.xy), size = ivec2(p.frame.xy);
    if (any(greaterThanEqual(xy, size))) return;
    if (texelFetch(depth_texture, xy, 0).r <= 0.0) return; // sky
    vec3 center = position_at(xy);
    float z = -center.z;
    if (z <= p.range.z || z >= p.range.w) return; // held weapon and distant scene
    // Choose the derivative that stays on the same surface to avoid silhouette halos.
    vec3 left = center - position_at(xy - ivec2(1, 0));
    vec3 right = position_at(xy + ivec2(1, 0)) - center;
    vec3 up = center - position_at(xy - ivec2(0, 1));
    vec3 down = position_at(xy + ivec2(0, 1)) - center;
    vec3 cross_normal = cross(abs(left.z) < abs(right.z) ? left : right, abs(up.z) < abs(down.z) ? up : down);
    if (dot(cross_normal, cross_normal) < 1e-12) return;
    vec3 normal = normalize(cross_normal);
    if (dot(normal, -center) < 0.0) normal = -normal;
    float radius = p.frame.z;
    float pixels = min(64.0, radius * p.projection.y * p.frame.y / (2.0 * z));
    float total = 0.0;
    for (int dir = 0; dir < 8; dir++) {
        float angle = float(dir) * .7853981634;
        vec2 direction = vec2(cos(angle), sin(angle));
        float horizon = 0.0;
        for (int step = 1; step <= 3; step++) {
            ivec2 sample_xy = xy + ivec2(round(direction * max(1.0, pixels * float(step) / 3.0)));
            if (any(lessThan(sample_xy, ivec2(0))) || any(greaterThanEqual(sample_xy, size))) continue;
            if (texelFetch(depth_texture, sample_xy, 0).r <= 0.0) continue;
            vec3 delta = position_at(sample_xy) - center;
            float distance = length(delta);
            if (distance < .001 || distance >= radius) continue;
            float cosine = max(0.0, dot(normal, delta / distance) - .08);
            horizon = max(horizon, cosine * (1.0 - distance * distance / (radius * radius)));
        }
        total += horizon;
    }
    float fade = smoothstep(p.range.z, p.range.z * 2.0, z) * (1.0 - smoothstep(p.range.w * .5, p.range.w, z));
    float ao = 1.0 - min(.45, total * p.frame.w / 8.0) * fade;
    vec4 color = imageLoad(color_image, xy);
    imageStore(color_image, xy, vec4(color.rgb * ao, color.a));
}
