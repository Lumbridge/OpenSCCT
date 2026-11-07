// Authoritative 25 uu smoke cells and analytic carves. No view-dependent density simulation.
global uniform sampler2D svm_smoke_fields : filter_nearest, repeat_disable;
global uniform sampler2D svm_smoke_frame : filter_nearest, repeat_disable;
global uniform sampler2D svm_smoke_growth : filter_linear, repeat_disable;
global uniform sampler3D svm_smoke_flow : filter_linear, repeat_disable;
global uniform sampler3D svm_smoke_shape : filter_linear, repeat_enable;
global uniform bool svm_smoke_flow_ready;
global uniform sampler2D svm_smoke_prefix : filter_linear, repeat_disable;
global uniform bool svm_smoke_prefix_ready;
global uniform bool svm_smoke_scene_shadows;
global uniform bool svm_smoke_active;
#ifndef SVM_SMOKE_SPOT_GLOBALS
#define SVM_SMOKE_SPOT_GLOBALS
global uniform sampler2D svm_spots;
global uniform int svm_spot_count;
global uniform sampler2D svm_spot_depth;
global uniform sampler2DArray svm_spot_cookies;
global uniform sampler2D svm_spot_atten;
#endif

// A skipped opaque tail changes transmittance by at most exp(-8), about .000336. Rays that remain
// transmissive, including thermal and bullet apertures, keep integrating at their normal full resolution.
const float SMOKE_OPAQUE_TAU = 8.0;
// Shared with ResponsiveSmokeField.Extinction. All qualities, light paths and optical queries agree.
const float SMOKE_EXTINCTION = .060;

// Shared with ResponsiveSmokeField.GrowthFade; quality cannot change the initial fill time.
const float SMOKE_GROWTH_FADE = .10;
vec4 smoke_field_row(int row) { return texelFetch(svm_smoke_fields, ivec2(row % 1024, row / 1024), 0); }
vec4 smoke_frame_row(int row) { return texelFetch(svm_smoke_frame, ivec2(row % 1024, row / 1024), 0); }
float smoke_scalar(int base_row, int index) {
    vec4 v = smoke_field_row(base_row + index / 4);
    return v[index % 4];
}
int smoke_index(ivec3 cell) { return cell.x + 25 * (cell.y + 25 * cell.z); }
bool smoke_cell_valid(ivec3 cell) { return all(greaterThanEqual(cell, ivec3(0))) && all(lessThan(cell, ivec3(25))); }
float smoke_growth(int cells, ivec3 cell, float age) {
    if (!smoke_cell_valid(cell)) return 0.0;
    float activation = smoke_scalar(cells, smoke_index(cell));
    return activation <= 0.0 ? 0.0 : clamp((age - (activation - 1.0) / 20.0) / SMOKE_GROWTH_FADE, 0.0, 1.0);
}
float smoke_segment_distance(vec3 p, vec3 a, vec3 b) {
    vec3 ab = b - a;
    return length(p - a - ab * clamp(dot(p - a, ab) / max(dot(ab, ab), 0.00001), 0.0, 1.0));
}
float smoke_segment_segment_distance(vec3 p, vec3 q, vec3 a, vec3 b) {
    vec3 d = q-p, e = b-a, r = p-a;
    float dd = dot(d,d), ee = dot(e,e), de = dot(d,e), dr = dot(d,r), er = dot(e,r);
    if (dd < .00001) return smoke_segment_distance(p,a,b);
    if (ee < .00001) return smoke_segment_distance(a,p,q);
    float determinant = dd*ee-de*de;
    float s = determinant > .00001 ? clamp((de*er-dr*ee)/determinant,0.0,1.0) : 0.0;
    float t = (de*s+er)/ee;
    if (t < 0.0) { t = 0.0; s = clamp(-dr/dd,0.0,1.0); }
    else if (t > 1.0) { t = 1.0; s = clamp((de-dr)/dd,0.0,1.0); }
    return length(r+d*s-e*t);
}
float smoke_carve_stride(vec3 ray, vec3 near_point, vec3 far_point, vec4 a, vec4 b) {
    float feature = max(1.0,min(a.w*.25,max(b.w*.5,1.0)));
    vec3 segment = b.xyz-a.xyz;
    float size = length(segment);
    if (size < .001) return feature;
    // A near-parallel tunnel has a long longitudinal feather. Refine its transverse thickness, while retaining
    // isotropic sampling at either spherical endpoint. Full-pixel coverage is independent of this ray stride.
    float transverse = max(length(cross(ray,segment/size)),.0001);
    float stride = feature/transverse;
    float radius = a.w+b.w;
    if (smoke_segment_distance(a.xyz,near_point,far_point) <= radius ||
        smoke_segment_distance(b.xyz,near_point,far_point) <= radius) stride = feature;
    return stride;
}
bool smoke_boundary_path(int cells, int lookup, vec3 origin, ivec3 cell, ivec3 offset, vec3 p, vec3 target) {
    // Complete supercover, shared with the simulation: no diagonal borrowing through a wall corner.
    ivec3 extent = abs(offset);
    for (int z = 0; z <= extent.z; z++) for (int y = 0; y <= extent.y; y++) for (int x = 0; x <= extent.x; x++) {
        ivec3 at = cell + ivec3(x,y,z)*offset;
        int index = smoke_index(at);
        if (smoke_scalar(cells,index) != 0.0) continue;
        if (lookup == 0) return false;
        int record = int(smoke_scalar(lookup,index));
        if (record == 0) return false;
        vec3 center = origin+(vec3(at)+vec3(.5))*25.0;
        int count = int(smoke_field_row(record).x);
        for (int plane_index = 0; plane_index < count; plane_index++) {
            vec4 plane = smoke_field_row(record+1+plane_index);
            float a = dot(plane.xyz,p-center)-plane.w, b = dot(plane.xyz,target-center)-plane.w;
            if (abs(a) <= .05 || a*b <= 0.0) return false;
        }
    }
    return true;
}
float smoke_boundary_density(int cells, int lookup, vec3 origin, ivec3 cell, vec3 p, float age, out int mask_cell) {
    mask_cell = -1;
    if (lookup == 0 || smoke_scalar(lookup,smoke_index(cell)) == 0.0) return 0.0;
    float best = 1e30, density = 0.0;
    // A face neighbor normally bounds the nearest donor at a floor or flat wall. Most later candidates
    // then need only arithmetic, rather than repeated atlas and plane lookups. Ties retain CPU index order.
    for (int face = 0; face < 6; face++) {
        ivec3 offset = ivec3(0); offset[face/2] = (face%2 == 0 ? -1 : 1);
        ivec3 donor = cell+offset;
        if (!smoke_cell_valid(donor)) continue;
        int index = smoke_index(donor);
        vec3 target = origin+(vec3(donor)+vec3(.5))*25.0, delta = p-target;
        float distance_squared = dot(delta,delta);
        if (distance_squared > best || (distance_squared == best && index >= mask_cell)) continue;
        float activation = smoke_scalar(cells,index);
        if (activation <= 0.0 || !smoke_boundary_path(cells,lookup,origin,cell,offset,p,target)) continue;
        best = distance_squared; mask_cell = index;
        density = clamp((age-(activation-1.0)/20.0)/SMOKE_GROWTH_FADE,0.0,1.0);
    }
    for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        ivec3 offset = ivec3(x,y,z), donor = cell+offset;
        if (!smoke_cell_valid(donor)) continue;
        int index = smoke_index(donor);
        vec3 target = origin+(vec3(donor)+vec3(.5))*25.0, delta = p-target;
        float distance_squared = dot(delta,delta);
        if (distance_squared > best || (distance_squared == best && index >= mask_cell)) continue;
        float activation = smoke_scalar(cells,index);
        if (activation <= 0.0 || !smoke_boundary_path(cells,lookup,origin,cell,offset,p,target)) continue;
        best = distance_squared; mask_cell = index;
        density = clamp((age-(activation-1.0)/20.0)/SMOKE_GROWTH_FADE,0.0,1.0);
    }
    return density;
}
float smoke_shape_noise(vec3 p) {
    // The baked cellular field has eight texels per feature cell. Plain trilinear interpolation matches the
    // shared CPU table; applying Hermite smoothing to individual texels would imprint the sampling grid.
    return textureLod(svm_smoke_shape,(p*8.0+vec3(.5))/64.0,0.0).r;
}
// Match ResponsiveSmokeField.MaterialCoordinate. Ordered, invertible shears roll a persistent material
// pattern upward. Only the material moves: collision, radius, core and carve positions stay in world space.
vec3 smoke_material_coordinate(vec3 p, float age) {
    p.x += 24.0*sin(p.z/85.0-1.2*age);
    p.z += 22.0*sin(p.x/90.0+.9*age+1.7);
    p.y += 18.0*sin(p.z/100.0-.7*age+3.1);
    p.z -= 18.0*age;
    return p;
}
float smoke_material_macro(vec3 material) {
    return smoothstep(.28,.72,smoke_shape_noise(material/135.0+vec3(11.17,7.31,19.83)));
}
float smoke_shape_density(vec3 relative, float age, vec3 extents) {
    float radius_squared = dot(relative,relative);
    if (radius_squared <= 22500.0) return 1.0;
    if (radius_squared >= 202500.0) return 0.0;
    vec3 material = smoke_material_coordinate(relative,age);
    float macro = smoke_material_macro(material);
    float fine = smoke_shape_noise(material/55.0+vec3(23.41,3.71,13.29));
    float taper = 1.0-.16*smoothstep(-.1,1.0,relative.z/extents.z);
    vec3 p = abs(relative/(extents*vec3(taper,taper,1.0)));
    float body = pow(dot(p,p*p),1.0/3.0), edge = .90+.08*macro+.02*fine;
    float shell = mix(1.0,.70+.30*macro,smoothstep(.55,.88,body));
    float density = shell*(1.0-smoothstep(edge-.07,edge,body));
    return max(1.0-smoothstep(150.0,180.0,sqrt(radius_squared)),density);
}
float smoke_density(int field, vec3 p, float now, bool include_bullets) {
    vec4 origin = smoke_field_row(field), info = smoke_field_row(field + 1);
    vec3 local = (p - origin.xyz) / 25.0;
    ivec3 nearest = ivec3(floor(local));
    if (!smoke_cell_valid(nearest)) return 0.0;
    int cells = int(info.y), index = smoke_index(nearest);
    float age = now - info.x;
    float fade = 1.0 - smoothstep(8.0, 10.0, age);
    if (fade <= 0.0 || age < 0.0) return 0.0;
    fade *= smoke_shape_density(p-smoke_field_row(field+2).xyz,age,smoke_field_row(field+3).yzw);
    if (fade <= 0.0) return 0.0;
    int lookup = int(smoke_field_row(field+3).x);
    float d;
    if (smoke_scalar(cells,index) == 0.0) {
        d = smoke_boundary_density(cells,lookup,origin.xyz,nearest,p,age,index);
    } else {
        float z = local.z-.5, z0 = floor(z), layer = smoke_field_row(field+2).w;
        vec2 size = vec2(textureSize(svm_smoke_growth,0));
        vec2 uv0 = vec2(z0*27.0+local.x+1.0,layer*27.0+local.y+1.0)/size;
        vec2 uv1 = uv0+vec2(27.0/size.x,0.0);
        vec2 a0 = z0 < 0.0 ? vec2(0.0) : textureLod(svm_smoke_growth,uv0,0.0).rg;
        vec2 a1 = z0 >= 24.0 ? vec2(0.0) : textureLod(svm_smoke_growth,uv1,0.0).rg;
        vec2 growth = mix(a0,a1,fract(z));
        d = growth.x;
        if (growth.y < .99999) {
            // Only samples beside geometry need explicit corner visibility and normalized valid weights.
            ivec3 base = ivec3(floor(local-vec3(.5)));
            vec3 fraction = fract(local-vec3(.5));
            float weight = 0.0; d = 0.0;
            for (int iz = 0; iz <= 1; iz++) for (int iy = 0; iy <= 1; iy++) for (int ix = 0; ix <= 1; ix++) {
                ivec3 corner = ivec3(ix,iy,iz), donor = base+corner;
                if (!smoke_cell_valid(donor)) continue;
                float activation = smoke_scalar(cells,smoke_index(donor));
                if (activation == 0.0) continue;
                ivec3 offset = donor-nearest, axes = abs(offset);
                if (axes.x+axes.y+axes.z > 1 && !smoke_boundary_path(cells,lookup,origin.xyz,nearest,offset,p,origin.xyz+(vec3(donor)+vec3(.5))*25.0)) continue;
                vec3 w = mix(vec3(1.0)-fraction,fraction,vec3(corner));
                float part = w.x*w.y*w.z;
                weight += part;
                if (activation > 0.0) d += part*clamp((age-(activation-1.0)/20.0)/SMOKE_GROWTH_FADE,0.0,1.0);
            }
            d /= max(weight,.00001);
        }
    }
    if (d <= 0.0) return 0.0;
    float carved = 1.0;
#ifdef SVM_SMOKE_COMPUTE
    // Bullet apertures are resolved only by full-resolution rays. Sampling them into the coarse cache would
    // both blur neighboring clear pixels and waste density work across every pixel of the view volume.
    int carve_count = int(origin.w);
#else
    int carve_count = include_bullets ? int(info.w) : int(origin.w);
#endif
    for (int i = 0; i < carve_count; i++) {
        int at = int(info.z) + i * 4;
        vec4 a = smoke_field_row(at), b = smoke_field_row(at + 1), timing = smoke_field_row(at + 2);
        float strength = 1.0 - smoothstep(timing.y, timing.y + max(timing.z, 0.001), now - timing.x);
        if (strength <= 0.0 || now < timing.x) continue;
        int mask = int(smoke_field_row(at + 3).x);
        if (mask != 0 && (index < 0 || smoke_scalar(mask, index) <= 0.0)) continue;
        float shape = 1.0 - smoothstep(a.w, a.w + max(b.w, 0.001), smoke_segment_distance(p,a.xyz,b.xyz));
        carved *= 1.0 - shape * strength;
        if (carved <= .00001) return 0.0;
    }
    return d*carved*fade;
}
bool smoke_box(vec3 eye, vec3 ray, vec3 lo, vec3 hi, float limit, out vec2 interval) {
    // Keep parallel rays finite; do not let 0 * infinity turn boundary rays into NaNs.
    vec3 safe_ray = mix(vec3(0.0000001), ray, greaterThan(abs(ray), vec3(0.0000001)));
    vec3 a = (lo-eye)/safe_ray, b = (hi-eye)/safe_ray;
    vec3 n = min(a,b), f = max(a,b);
    interval = vec2(max(0.0,max(n.x,max(n.y,n.z))), min(limit,min(f.x,min(f.y,f.z))));
    return interval.y > interval.x;
}

float smoke_hash(vec3 p) {
    p = fract(p*.1031);
    p += dot(p,p.yzx+33.33);
    return fract((p.x+p.y)*p.z);
}
float smoke_noise(vec3 p) {
    vec3 cell = floor(p), f = fract(p);
    f = f*f*(3.0-2.0*f);
    return mix(mix(mix(smoke_hash(cell),smoke_hash(cell+vec3(1,0,0)),f.x),
                   mix(smoke_hash(cell+vec3(0,1,0)),smoke_hash(cell+vec3(1,1,0)),f.x),f.y),
               mix(mix(smoke_hash(cell+vec3(0,0,1)),smoke_hash(cell+vec3(1,0,1)),f.x),
                   mix(smoke_hash(cell+vec3(0,1,1)),smoke_hash(cell+vec3(1,1,1)),f.x),f.y),f.z);
}
#include "res://Shaders/svm_smoke_lighting.gdshaderinc"

vec3 smoke_light(int cloud, vec3 ue, vec3 ray, float density, float now, int quality, int cloud_base, int cloud_count) {
    return smoke_lighting(cloud,ue,ray,now,quality,cloud_base,cloud_count);
}
// Full-resolution integration is intentional. In particular Low never downsamples a narrow bullet opening.
vec4 responsive_smoke_exact(uint layers, vec3 view_position_metres, bool transmission_only) {
    int slot = int((layers >> 8u) & 127u);
    if (slot == 0 || slot == 127 || (layers & 262144u) != 0u) return vec4(0.0,0.0,0.0,1.0);
    int view = slot * 14;
    vec4 frame = smoke_frame_row(view), eye = smoke_frame_row(view+1);
    if (frame.y < 0.5 || int(eye.w) == 38) return vec4(0.0,0.0,0.0,1.0);
    vec3 delta = smoke_frame_row(view+2).xyz*view_position_metres.x
               + smoke_frame_row(view+3).xyz*view_position_metres.y
               - smoke_frame_row(view+4).xyz*view_position_metres.z;
    float limit = length(delta)*100.0;
    if (limit < 0.001) return vec4(0.0,0.0,0.0,1.0);
    vec3 ray = normalize(delta);
    int quality = int(frame.w), mode = int(eye.w);
    bool shared_bullets = transmission_only && smoke_frame_row(view+6).w > .5;
    vec4 bullets = smoke_frame_row(view+7);
    float near_t = limit, far_t = 0.0;
    float stride = quality == 0 ? 12.5 : quality == 1 ? 8.333333 : 6.25;
    for (int c = 0; c < int(frame.y); c++) {
        int cloud = int(frame.x) + c*14, field = int(smoke_frame_row(cloud).x);
        vec4 origin = smoke_field_row(field), info = smoke_field_row(field+1);
        vec2 span;
        if (!smoke_box(eye.xyz,ray,origin.xyz,origin.xyz+vec3(625.0),limit,span)) continue;
        near_t = min(near_t,span.x); far_t = max(far_t,span.y);
        for (int k = 0; k < (shared_bullets ? int(origin.w) : int(info.w)); k++) {
            int at = int(info.z)+k*4;
            vec4 a = smoke_field_row(at), b = smoke_field_row(at+1), timing = smoke_field_row(at+2);
            if (frame.z > timing.x+timing.y+timing.z) continue;
            float radius = a.w+b.w;
            if (smoke_segment_segment_distance(eye.xyz+ray*span.x,eye.xyz+ray*span.y,a.xyz,b.xyz) <= radius)
                stride = min(stride,smoke_carve_stride(ray,eye.xyz+ray*span.x,eye.xyz+ray*span.y,a,b));
        }
    }
    if (far_t <= near_t) return vec4(0.0,0.0,0.0,1.0);
    uint selected_mask = 0u;
    if (shared_bullets) for (int k = 0; k < int(bullets.w); k++) {
        int at = int(bullets.z)+k*4;
        vec4 a = smoke_frame_row(at), b = smoke_frame_row(at+1);
        if (smoke_segment_segment_distance(eye.xyz+ray*near_t,eye.xyz+ray*far_t,a.xyz,b.xyz) <= a.w+b.w) {
            stride = min(stride,smoke_carve_stride(ray,eye.xyz+ray*near_t,eye.xyz+ray*far_t,a,b));
            if (k < 32) selected_mask |= 1u << uint(k);
        }
    }
    int steps = int(ceil((far_t-near_t)/stride));
    float step_length = (far_t-near_t)/float(max(steps,1));
    float tau = 0.0;
    vec3 radiance = vec3(0.0);
    for (int s = 0; s < steps; s++) {
        float distance_along = near_t+(float(s)+0.5)*step_length;
        vec3 p = eye.xyz+ray*distance_along;
        float shared_carve = 1.0;
        if (shared_bullets) for (int k = 0; k < int(bullets.w); k++) {
            if (k < 32 && (selected_mask & (1u << uint(k))) == 0u) continue;
            int at = int(bullets.z)+k*4;
            vec4 a = smoke_frame_row(at), b = smoke_frame_row(at+1), timing = smoke_frame_row(at+2);
            vec3 segment = b.xyz-a.xyz;
            vec3 difference = p-a.xyz-segment*clamp(dot(p-a.xyz,segment)*timing.y,0.0,1.0);
            float square_distance = dot(difference,difference), outer = a.w+b.w;
            if (square_distance >= outer*outer) continue;
            float feather = clamp((sqrt(square_distance)-a.w)*timing.z,0.0,1.0);
            float shape = 1.0-feather*feather*(3.0-2.0*feather);
            shared_carve *= 1.0-shape*timing.x;
            if (shared_carve <= .00001) break;
        }
        if (shared_carve <= .00001) continue;
        float density = 0.0;
        vec3 source = vec3(0.0);
        // Raw omitted radiance is at most .9*exp(-6) per channel, before history/vision. Extinction
        // continues to the bounded opaque-tail threshold; thermal keeps its unmodified source.
        bool light_sample = !transmission_only && mode != 36 && tau < 6.0;
        bool spots_ready = false;
        vec3 common_spots = vec3(0.0);
        for (int c = 0; c < int(frame.y); c++) {
            int cloud = int(frame.x) + c*14, field = int(smoke_frame_row(cloud).x);
            float part = smoke_density(field,p,frame.z,!shared_bullets)*shared_carve;
            if (part <= .0001) continue;
            density += part;
            if (light_sample) {
                if (!spots_ready) {
                    common_spots = smoke_spot_light(p,ray,frame.z,quality,int(frame.x),int(frame.y));
                    spots_ready = true;
                }
                source += part*smoke_lighting_with_spots(cloud,p,ray,frame.z,quality,int(frame.x),int(frame.y),common_spots);
            }
            if (!light_sample && density >= 1.0) break;
        }
        if (density <= .0001) continue;
        source = mode == 36 ? vec3(0.0,0.0,1.0-clamp(distance_along/5000.0,0.0,1.0)) : source/density;
        // Gas/AI and visual extinction all use the same bounded union, independent of cloud enumeration order.
        float optical = min(density,1.0)*step_length*SMOKE_EXTINCTION*(mode == 36 ? .2 : 1.0);
        if (light_sample || mode == 36) radiance += exp(-tau)*(1.0-exp(-optical))*source;
        tau += optical;
        if (tau >= SMOKE_OPAQUE_TAU) break;
    }
    return vec4(radiance,exp(-tau));
}

#ifndef SVM_SMOKE_COMPUTE
// Full mode shadows scene lighting with the same clipped density used for visibility. Work is bounded to
// the receiving surface's nearest cloud light set, four sources and eight taps per intersected cloud.
float smoke_scene_ray_transmission(int base, int count, vec3 point, vec3 direction, float distance, float now) {
    float optical = 0.0;
    for (int c = 0; c < count; c++) {
        int field = int(smoke_frame_row(base+c*14).x);
        vec3 origin = smoke_field_row(field).xyz;
        vec2 span;
        if (!smoke_box(point,direction,origin,origin+vec3(625.0),distance,span)) continue;
        float step_length = (span.y-span.x)/8.0;
        for (int tap = 0; tap < 8; tap++) {
            vec3 sample_point = point+direction*(span.x+(float(tap)+.5)*step_length);
            optical += smoke_density(field,sample_point,now,false)*step_length*SMOKE_EXTINCTION;
            if (optical >= SMOKE_OPAQUE_TAU) return exp(-SMOKE_OPAQUE_TAU);
        }
    }
    return exp(-optical);
}

float responsive_smoke_scene_light(uint layers, vec3 world_position, vec3 world_normal) {
    if (!svm_smoke_active || !svm_smoke_scene_shadows || (layers&262144u) != 0u) return 1.0;
    int slot = int((layers>>8u)&127u), view = slot*14;
    if (slot == 0 || slot == 127) return 1.0;
    vec4 frame = smoke_frame_row(view), eye = smoke_frame_row(view+1);
    if (frame.y < .5 || int(eye.w) == 36 || int(eye.w) == 38) return 1.0;
    vec3 point = world_position.xzy*100.0;
    int nearest = int(frame.x);
    float best = 1e30;
    for (int c = 0; c < int(frame.y); c++) {
        int cloud = int(frame.x)+c*14, field = int(smoke_frame_row(cloud).x);
        vec3 delta = point-smoke_field_row(field+2).xyz;
        float distance_squared = dot(delta,delta);
        if (distance_squared < best) { best = distance_squared; nearest = cloud; }
    }
    float ambient = max(dot(smoke_frame_row(nearest+1).rgb,vec3(.2126,.7152,.0722)),.01);
    float incident = ambient, attenuated = ambient;
    for (int i = 0; i < int(smoke_frame_row(nearest).y); i++) {
        vec4 light = smoke_frame_row(nearest+2+i*3), color = smoke_frame_row(nearest+3+i*3), cone = smoke_frame_row(nearest+4+i*3);
        vec3 delta = light.w < 0.0 ? light.xyz : light.xyz-world_position;
        float distance = length(delta), attenuation = 1.0;
        vec3 direction = delta/max(distance,.00001);
        if (light.w >= 0.0) {
            if (cone.w > -3.5 && cone.w < -2.5) {
                float ratio = distance/max(light.w,.001);
                attenuation = ratio >= 1.0 ? 0.0 : 1.0/max(8.0*ratio*ratio,.001);
            } else attenuation = distance > (256.0-color.w)/max(1.0,light.w) ? 0.0 : 1.0/max(color.w+light.w*distance,.01);
            if (cone.w >= 0.0) attenuation *= smoothstep(cone.w,min(1.0,cone.w+.1),dot(-direction,cone.xyz));
        }
        float energy = dot(color.rgb,vec3(.2126,.7152,.0722))*attenuation*max(dot(world_normal,direction),0.0);
        if (energy <= .00001) continue;
        float transmission = smoke_scene_ray_transmission(int(frame.x),int(frame.y),point+world_normal.xzy*.1,
            direction.xzy,light.w < 0.0 ? 100000.0 : distance*100.0,frame.z);
        incident += energy; attenuated += energy*transmission;
    }
    return clamp(attenuated/incident,0.0,1.0);
}

vec4 smoke_prefix_slice(vec2 uv, int slice, vec4 grid) {
    vec2 size = vec2(textureSize(svm_smoke_prefix,0));
    vec2 local = clamp(uv*grid.yz,vec2(.5),grid.yz-vec2(.5));
    vec2 tile = vec2(float(slice%8)*grid.y,grid.x+float(slice/8)*grid.z);
    return textureLod(svm_smoke_prefix,(tile+local)/size,0.0);
}

vec4 responsive_smoke(uint layers, vec3 position) {
    if (!svm_smoke_active) return vec4(0.0,0.0,0.0,1.0);
    int slot = int((layers>>8u)&127u), view = slot*14;
    if (slot == 0 || slot == 127 || (layers&262144u) != 0u) return vec4(0.0,0.0,0.0,1.0);
    vec4 frame = smoke_frame_row(view), eye = smoke_frame_row(view+1);
    if (frame.y < .5 || int(eye.w) == 38) return vec4(0.0,0.0,0.0,1.0);
    vec4 grid = smoke_frame_row(view+5), depths = smoke_frame_row(view+6);
    vec4 right = smoke_frame_row(view+2), up = smoke_frame_row(view+3), forward = smoke_frame_row(view+4);
    if (!svm_smoke_prefix_ready || depths.z < .5) return responsive_smoke_exact(layers,position,false);
    vec3 delta = right.xyz*position.x+up.xyz*position.y-forward.xyz*position.z;
    float limit = length(delta)*100.0;
    vec3 ray = normalize(delta);
    // Refine the finite capsule inside the smoke depth interval. A muzzle at the eye or a diagonal shot must not
    // trigger full-screen work merely because its axis-aligned box crosses the camera. The cache omits bullets.
    vec4 bullets = smoke_frame_row(view+7);
    float ray_scale = length(position)/max(-position.z,.00001);
    vec2 span = depths.xy*ray_scale;
    vec2 uv = (vec2(1.0)+vec2(position.x*right.w,-position.y*up.w)/max(-position.z,.00001))*.5;
    bool refine = false;
    for (int k = 0; k < int(bullets.w); k++) {
        int at = int(bullets.z)+k*4;
        vec4 rect = smoke_frame_row(at+3);
        if (uv.x < rect.x || uv.y < rect.y || uv.x > rect.z || uv.y > rect.w) continue;
        vec4 a = smoke_frame_row(at), b = smoke_frame_row(at+1);
        float margin = span.y/max(bullets.y*up.w,1.0);
        float radius = a.w+b.w+margin;
        float stop = min(span.y,limit);
        if (stop > span.x && smoke_segment_segment_distance(eye.xyz+ray*span.x,eye.xyz+ray*stop,a.xyz,b.xyz) <= radius)
            { refine = true; break; }
    }
    float z = max(-position.z*100.0,0.0);
    if (z <= depths.x) return vec4(0.0,0.0,0.0,1.0);
    float slice = clamp((z-depths.x)/(depths.y-depths.x),0.0,1.0)*(grid.w-1.0);
    int low = int(floor(slice)), high = min(low+1,int(grid.w)-1);
    vec4 low_value = smoke_prefix_slice(uv,low,grid), high_value = smoke_prefix_slice(uv,high,grid);
    float fraction = fract(slice);
    vec4 result = mix(low_value,high_value,fraction);
    // A surface inside the cloud may sit behind only a short, optically thick path. Linear T interpolation
    // over a coarse depth interval can expose that surface. Monotone cumulative T bounds depth interpolation
    // error by the endpoint span; refine larger spans instead of inventing opacity across clear geometry or
    // blast intervals with log-space interpolation. This bound excludes spatial filtering/integration error.
    if (low != high && fraction > 0.0 && fraction < 1.0 && abs(low_value.a-high_value.a) > .015) refine = true;
    if (refine) {
        // Apertures and unresolved surface depths use full-pixel extinction with the opaque-tail cutoff. Illumination
        // keeps cached soft scattering; multiplying by the new opacity prevents a sub-grid milky halo.
        float transmission = responsive_smoke_exact(layers,position,true).a;
        result.rgb *= (1.0-transmission)/max(1.0-result.a,.00001);
        result.a = transmission;
    }
    return result;
}

vec4 responsive_smoke_background(uint layers, vec2 uv) {
    if (!svm_smoke_active) return vec4(0.0,0.0,0.0,1.0);
    int view = int((layers>>8u)&127u)*14;
    vec4 right = smoke_frame_row(view+2), up = smoke_frame_row(view+3), forward = smoke_frame_row(view+4);
    if (right.w == 0.0 || up.w == 0.0) return vec4(0.0,0.0,0.0,1.0);
    vec2 ndc = uv*2.0-1.0;
    return responsive_smoke(layers,vec3(ndc.x/right.w,-ndc.y/up.w,-1.0)*forward.w*.01);
}
#endif
