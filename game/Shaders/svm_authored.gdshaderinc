// Shared authored-material sampling. Colour is linear; data textures remain linear values.
uniform vec3 am_baseColor = vec3(1.0);
uniform float am_opacity = 1.0;
uniform float am_cutoff = 0.5;
uniform float am_normalStrength = 1.0;
uniform bool am_normalDirectX = false;
uniform bool am_bumpFromHeight = false;
uniform float am_roughness = 0.85;
uniform float am_metallic = 0.0;
uniform float am_specular = 0.5;
uniform vec3 am_specularColor = vec3(1.0);
uniform vec3 am_emissionColor = vec3(1.0);
uniform float am_emissionEnergy = 0.0;
uniform float am_aoStrength = 1.0;
uniform float am_clearcoat = 0.0;
uniform float am_clearcoatGloss = 0.5;
uniform float am_anisotropy = 0.0;
uniform float am_refraction = 0.0;
uniform float am_detailStrength = 0.0;
uniform float am_parallaxDepth = 0.0;
uniform float am_displacement = 0.0;
uniform float am_heightMidpoint = 0.5;
uniform int am_parallaxSteps = 16;
uniform float am_tileU = 1.0;
uniform float am_tileV = 1.0;
uniform float am_offsetU = 0.0;
uniform float am_offsetV = 0.0;
uniform float am_rotation = 0.0;
uniform bool am_triplanar = false;
uniform vec4 am_atlas = vec4(1.0, 1.0, 1.0, 12.0);
uniform vec4 am_motion = vec4(0.0);
uniform bool am_loop = true;
uniform bool am_pingPong = false;
uniform sampler2D am_bakedDirection : filter_linear, repeat_disable;
uniform bool am_hasBakedDirection = false;
#ifdef AM_REFRACTION
uniform sampler2D am_scene : hint_screen_texture, filter_linear_mipmap, repeat_disable;
#endif

vec3 am_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
vec3 am_display(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c)); }

mat3 am_frame(vec3 position, vec3 normal, vec2 uv) {
    vec3 p = dFdx(position), q = dFdy(position);
    vec2 u = dFdx(uv), v = dFdy(uv);
    float det = u.x * v.y - u.y * v.x;
    vec3 tangent = p * v.y - q * u.y;
    if (dot(tangent, tangent) < 0.0000001) tangent = cross(normal, abs(normal.y) > 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0));
    tangent = normalize(tangent) * (det < 0.0 ? -1.0 : 1.0);
    return mat3(tangent, normalize(cross(normal, tangent)) * (det < 0.0 ? -1.0 : 1.0), normal);
}

// BINDINGS

vec2 am_mapping(vec2 uv, float clock) {
    uv = uv * vec2(am_tileU, am_tileV) + vec2(am_offsetU, am_offsetV) + am_motion.xy * clock;
    float angle = am_rotation + am_motion.z * clock;
    vec2 p = uv - vec2(0.5);
    uv = vec2(p.x * cos(angle) - p.y * sin(angle), p.x * sin(angle) + p.y * cos(angle)) + vec2(0.5);
    return uv;
}

vec2 am_uv(vec2 original_uv, float clock) {
    vec2 uv = am_mapping(original_uv, clock);
    float frames = max(1.0, am_atlas.z);
    float period = am_pingPong && frames > 1.0 ? frames * 2.0 - 2.0 : frames;
    float tick = max(0.0, floor(clock * am_atlas.w + am_motion.w));
    float frame = am_loop ? mod(tick, period) : min(tick, am_pingPong && frames > 1.0 ? period : period - 1.0);
    if (frame >= frames) frame = period - frame;
    return (fract(uv) + vec2(mod(frame, am_atlas.x), floor(frame / am_atlas.x))) / am_atlas.xy;
}

vec4 am_image(sampler2D tex, vec2 uv) {
    if (am_atlas.x > 1.0 || am_atlas.y > 1.0) {
        vec2 dimensions = vec2(textureSize(tex, 0)), frame_size = dimensions / am_atlas.xy;
        vec2 powers = log2(frame_size);
        float max_level = all(lessThan(abs(powers - round(powers)), vec2(0.0001))) ? min(powers.x, powers.y) : 0.0;
        vec2 dx = dFdx(uv) * dimensions, dy = dFdy(uv) * dimensions;
        float level = clamp(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1.0)), 0.0, max_level);
        vec2 cell = floor(uv * am_atlas.xy), texel = 0.5 * exp2(ceil(level)) / dimensions;
        return textureLod(tex, clamp(uv, cell / am_atlas.xy + texel, (cell + 1.0) / am_atlas.xy - texel), level);
    }
    return texture(tex, uv);
}

vec4 am_sample(sampler2D tex, vec2 uv, vec3 position, vec3 normal, float clock) {
    if (!am_triplanar) return am_image(tex, uv);
    vec3 weights = pow(abs(normal), vec3(4.0));
    weights /= max(dot(weights, vec3(1.0)), 0.0001);
    return am_image(tex, am_uv(position.zy, clock)) * weights.x + am_image(tex, am_uv(position.xz, clock)) * weights.y + am_image(tex, am_uv(position.xy, clock)) * weights.z;
}

vec3 am_triplanar_normal(sampler2D tex, vec3 position, vec3 normal, float clock, float strength, bool srgb) {
    vec3 weights = pow(abs(normal), vec3(4.0)); weights /= max(dot(weights, vec3(1.0)), 0.0001);
    vec3 nx = am_image(tex, am_uv(position.zy, clock)).xyz;
    vec3 ny = am_image(tex, am_uv(position.xz, clock)).xyz;
    vec3 nz = am_image(tex, am_uv(position.xy, clock)).xyz;
    if (srgb) { nx = am_linear(nx); ny = am_linear(ny); nz = am_linear(nz); }
    nx = nx * 2.0 - 1.0; ny = ny * 2.0 - 1.0; nz = nz * 2.0 - 1.0;
    vec2 scale = vec2(strength, strength * (am_normalDirectX ? -1.0 : 1.0)); nx.xy *= scale; ny.xy *= scale; nz.xy *= scale;
    return normalize(vec3(nx.z * sign(normal.x), nx.y, nx.x) * weights.x + vec3(ny.x, ny.z * sign(normal.y), ny.y) * weights.y + vec3(nz.x, nz.y, nz.z * sign(normal.z)) * weights.z);
}

float am_scalar(vec4 sample_value, int channel, bool invert_value) {
    float value = channel == 4 ? dot(sample_value.rgb, vec3(0.2126, 0.7152, 0.0722)) : sample_value[channel];
    return invert_value ? 1.0 - value : value;
}

float am_value(vec4 sample_value, int channel, bool invert_value, bool srgb) {
    if (srgb) sample_value.rgb = am_linear(sample_value.rgb);
    return am_scalar(sample_value, channel, invert_value);
}

struct AuthoredSample { vec4 color; vec3 normal; vec3 emission; float roughness; float metallic; float specular; float ao; };

AuthoredSample am_evaluate(vec2 original_uv, vec3 position, vec3 normal, vec3 view_tangent, float clock) {
    vec2 uv = am_uv(original_uv, clock);
    vec2 atlas_cell = floor(uv * am_atlas.xy);
    if (am_has_height && am_parallaxDepth > 0.0 && !am_triplanar) {
        vec2 delta_uv = view_tangent.xy / max(abs(view_tangent.z), 0.15) * am_parallaxDepth / float(am_parallaxSteps) / am_atlas.xy;
        float layer = 0.0;
        for (int i = 0; i < 64; i++) {
            vec2 height_uv = (fract(uv * am_atlas.xy) + atlas_cell) / am_atlas.xy;
            vec2 margin = 0.5 / vec2(textureSize(am_tex_height, 0));
            height_uv = clamp(height_uv, atlas_cell / am_atlas.xy + margin, (atlas_cell + 1.0) / am_atlas.xy - margin);
            if (i >= am_parallaxSteps || layer >= 1.0 - am_value(textureLod(am_tex_height, height_uv, 0.0), am_channel_height, am_invert_height, am_srgb_height)) break;
            uv -= delta_uv; layer += 1.0 / float(am_parallaxSteps);
        }
        uv = (fract(uv * am_atlas.xy) + atlas_cell) / am_atlas.xy;
    }
    AuthoredSample result;
    vec4 base = am_has_baseColor ? am_sample(am_tex_baseColor, uv, position, normal, clock) : vec4(1.0);
    if (am_srgb_baseColor) base.rgb = am_linear(base.rgb);
    result.color = base * vec4(am_baseColor, am_opacity);
    if (am_has_opacity) result.color.a *= am_value(am_sample(am_tex_opacity, uv, position, normal, clock), am_channel_opacity, am_invert_opacity, am_srgb_opacity);
    if (am_has_detailColor) {
        vec3 detail = am_sample(am_tex_detailColor, uv, position, normal, clock).rgb;
        if (am_srgb_detailColor) detail = am_linear(detail);
        result.color.rgb *= mix(vec3(1.0), detail * 2.0, am_detailStrength);
    }
    result.normal = am_triplanar ? normal : vec3(0.0, 0.0, 1.0);
    if (am_has_normal) {
        vec3 normal_pixel = am_sample(am_tex_normal, uv, position, normal, clock).xyz;
        result.normal = (am_srgb_normal ? am_linear(normal_pixel) : normal_pixel) * 2.0 - 1.0;
        result.normal.xy *= vec2(am_normalStrength, am_normalStrength * (am_normalDirectX ? -1.0 : 1.0));
        if (am_triplanar) result.normal = am_triplanar_normal(am_tex_normal, position, normal, clock, am_normalStrength, am_srgb_normal);
    } else if (am_has_height && am_bumpFromHeight) {
        vec2 texel = 1.0 / vec2(textureSize(am_tex_height, 0));
        float h = am_value(texture(am_tex_height, uv), am_channel_height, am_invert_height, am_srgb_height);
        float x = am_value(texture(am_tex_height, uv + vec2(texel.x, 0.0)), am_channel_height, am_invert_height, am_srgb_height);
        float y = am_value(texture(am_tex_height, uv + vec2(0.0, texel.y)), am_channel_height, am_invert_height, am_srgb_height);
        result.normal = vec3((h - x) * am_normalStrength, (h - y) * am_normalStrength, 1.0);
        if (am_triplanar) {
            float height_value = am_value(am_sample(am_tex_height, uv, position, normal, clock), am_channel_height, am_invert_height, am_srgb_height);
            vec3 dx = dFdx(position), dy = dFdy(position), r1 = cross(dy, normal), r2 = cross(normal, dx);
            float det = dot(dx, r1);
            result.normal = normalize(normal - (r1 * dFdx(height_value) + r2 * dFdy(height_value)) / max(abs(det), 0.000001) * sign(det) * am_normalStrength);
        }
    }
    if (am_has_detailNormal) {
        vec3 detail = am_sample(am_tex_detailNormal, uv, position, normal, clock).xyz * 2.0 - 1.0;
        detail.y *= am_normalDirectX ? -1.0 : 1.0;
        result.normal = vec3(result.normal.xy + detail.xy * am_detailStrength, result.normal.z * detail.z);
        if (am_triplanar) result.normal = normalize((am_has_normal ? am_triplanar_normal(am_tex_normal, position, normal, clock, am_normalStrength, am_srgb_normal) : normal) + (am_triplanar_normal(am_tex_detailNormal, position, normal, clock, 1.0, am_srgb_detailNormal) - normal) * am_detailStrength);
    }
    result.normal = normalize(result.normal);
    result.roughness = clamp(am_roughness * (am_has_roughness ? am_value(am_sample(am_tex_roughness, uv, position, normal, clock), am_channel_roughness, am_invert_roughness, am_srgb_roughness) : 1.0), 0.04, 1.0);
    result.metallic = am_metallic * (am_has_metallic ? am_value(am_sample(am_tex_metallic, uv, position, normal, clock), am_channel_metallic, am_invert_metallic, am_srgb_metallic) : 1.0);
    result.specular = am_specular * (am_has_specular ? am_value(am_sample(am_tex_specular, uv, position, normal, clock), am_channel_specular, am_invert_specular, am_srgb_specular) : 1.0);
    result.ao = mix(1.0, am_has_ao ? am_value(am_sample(am_tex_ao, uv, position, normal, clock), am_channel_ao, am_invert_ao, am_srgb_ao) : 1.0, am_aoStrength);
    vec3 emissive = am_has_emission ? am_sample(am_tex_emission, uv, position, normal, clock).rgb : vec3(1.0);
    if (am_srgb_emission) emissive = am_linear(emissive);
    result.emission = emissive * am_emissionColor * am_emissionEnergy;
    return result;
}

// GGX direct-light response, with Schlick Fresnel and Smith masking. Inputs and output are linear.
vec3 am_environment(AuthoredSample s, vec3 n, vec3 v) {
    if (!am_has_environment) return vec3(0.0);
    vec3 reflected = reflect(-v, n);
    vec2 uv = vec2(atan(reflected.z, reflected.x) / 6.2831853 + 0.5, acos(clamp(reflected.y, -1.0, 1.0)) / 3.14159265);
    vec3 color = textureLod(am_tex_environment, uv, s.roughness * 7.0).rgb;
    if (am_srgb_environment) color = am_linear(color);
    vec3 f0 = mix(am_specularColor * 0.08 * s.specular, s.color.rgb, s.metallic);
    return color * (f0 + (1.0 - f0) * pow(1.0 - max(dot(n, v), 0.0), 5.0)) * s.ao;
}

vec3 am_brdf(AuthoredSample s, vec3 n, vec3 v, vec3 l, vec3 radiance, vec3 tangent) {
    vec3 h = normalize(v + l);
    float nl = max(dot(n, l), 0.0), nv = max(dot(n, v), 0.001), nh = max(dot(n, h), 0.0), vh = max(dot(v, h), 0.0);
    float anisotropic = mix(1.0, 0.5 + abs(dot(tangent, h)), am_anisotropy);
    float a = max(0.002, s.roughness * s.roughness * anisotropic), a2 = a * a;
    float denom = nh * nh * (a2 - 1.0) + 1.0;
    float d = a2 / max(3.14159265 * denom * denom, 0.00001);
    float k = (s.roughness + 1.0) * (s.roughness + 1.0) / 8.0;
    float g = nv / (nv * (1.0 - k) + k) * nl / (nl * (1.0 - k) + k);
    vec3 f0 = mix(am_specularColor * 0.08 * s.specular, s.color.rgb, s.metallic);
    vec3 f = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
    vec3 spec = d * g * f / max(4.0 * nv * nl, 0.001);
    float coat = am_clearcoat * pow(nh, mix(8.0, 256.0, am_clearcoatGloss)) * 0.25;
    return ((1.0 - f) * (1.0 - s.metallic) * s.color.rgb / 3.14159265 + spec + vec3(coat)) * radiance * nl;
}

// The SCCT light term already contains diffuse; this adds only the authored highlight.
vec3 am_highlight(AuthoredSample s, vec3 n, vec3 v, vec3 l, vec3 radiance, vec3 tangent) {
    vec3 f0 = mix(am_specularColor * 0.08 * s.specular, s.color.rgb, s.metallic);
    vec3 h = normalize(v + l);
    vec3 f = f0 + (1.0 - f0) * pow(1.0 - max(dot(v, h), 0.0), 5.0);
    vec3 diffuse = (1.0 - f) * (1.0 - s.metallic) * s.color.rgb / 3.14159265 * radiance * max(dot(n, l), 0.0);
    return max(am_brdf(s, n, v, l, radiance, tangent) - diffuse, vec3(0.0));
}
