// Lighting changes radiance only. Extinction, geometry and apertures remain the authoritative density field.
// HG is normalized to isotropic scattering (4*pi is folded into the light units), with mild forward bias.
float smoke_phase_hg(float cosine) {
    const float g = .22;
    float denominator = max(1.0+g*g-2.0*g*clamp(cosine,-1.0,1.0),.0001);
    return (1.0-g*g)/(denominator*sqrt(denominator));
}

// A small isotropic share represents unresolved multiple scattering from the local incident light. Split,
// rather than add, its energy: shadows retain readable volume detail without boosting a directly lit rim.
float smoke_scattered_light(float phase, float transmission) {
    return .85*phase*transmission+.15;
}

float smoke_light_transmission(int cloud_base, int cloud_count, vec3 ue, vec3 toward_light,
                               float light_distance, float now, int quality) {
    int taps = quality == 0 ? 1 : quality == 1 ? 2 : 3;
    float stop = min(max(light_distance,0.0),255.0);
    float optical = 0.0;
    for (int tap = 0; tap < taps; tap++) {
        // Concentrate the same bounded sample count near the visible lobe skin. Uniform midpoints begin
        // 64 uu inside on Medium and miss the 18 uu creases. Segment weights still integrate uniform smoke exactly.
        float lo = float(tap)/float(taps), hi = float(tap+1)/float(taps);
        lo = lo*lo*lo*stop; hi = hi*hi*hi*stop;
        float step_length = hi-lo;
        vec3 p = ue+toward_light*((lo+hi)*.5);
        float density = 0.0;
        for (int c = 0; c < cloud_count; c++) {
            int field = int(smoke_frame_row(cloud_base+c*14).x);
            density += smoke_density(field,p,now,false);
            if (density >= 1.0) break;
        }
        optical += min(density,1.0)*step_length*SMOKE_EXTINCTION;
    }
    return exp(-optical);
}

// The eight probe positions are center +/-150 uu. Their locations are projected into reachable cells on CPU,
// retaining zone-local ambient and light selection when one cloud straddles a doorway or differently lit rooms.
void smoke_sample_light_probes(int cloud, vec3 ue, out vec3 ambient, out vec3 direct,
                              out vec3 direction, out float light_distance) {
    int base = int(smoke_frame_row(cloud+1).w);
    int field = int(smoke_frame_row(cloud).x);
    vec3 f = clamp((ue-smoke_field_row(field+2).xyz)/300.0+vec3(.5),vec3(0.0),vec3(1.0));
    ambient = vec3(0.0); direct = vec3(0.0); direction = vec3(0.0); light_distance = 0.0;
    for (int i = 0; i < 8; i++) {
        vec3 corner = vec3(float(i&1),float((i>>1)&1),float((i>>2)&1));
        vec3 axis_weight = mix(vec3(1.0)-f,f,corner);
        float weight = axis_weight.x*axis_weight.y*axis_weight.z;
        vec4 a = smoke_frame_row(base+i*3), d = smoke_frame_row(base+i*3+1);
        ambient += a.rgb*weight; direct += d.rgb*weight;
        direction += smoke_frame_row(base+i*3+2).xyz*weight;
        light_distance += d.w*weight;
    }
}

// Map spots and the merc torch reuse their existing shadow/cookie atlas. Density attenuation is a separate,
// soft volumetric shadow toward the source, so a carved blast can brighten the body behind its opening.
vec3 smoke_spot_light(vec3 ue, vec3 ray, float now, int quality, int cloud_base, int cloud_count) {
    vec3 wp = ue.xzy*.01, sum = vec3(0.0);
    for (int i = 0; i < svm_spot_count; i++) {
        vec4 a = texelFetch(svm_spots,ivec2(3,i),0);
        if (a.x < .5) continue;
        vec4 world = vec4(wp,1.0);
        vec4 r0 = texelFetch(svm_spots,ivec2(0,i),0), r1 = texelFetch(svm_spots,ivec2(1,i),0), r2 = texelFetch(svm_spots,ivec2(2,i),0);
        vec3 v = vec3(dot(r0,world),dot(r1,world),dot(r2,world));
        float z = -v.z;
        vec4 color = texelFetch(svm_spots,ivec2(4,i),0);
        if (z <= color.a || z >= color.a+a.y) continue;
        vec4 shape = texelFetch(svm_spots,ivec2(5,i),0);
        vec2 uv = (shape.z > .5 ? v.xy*shape.xy : v.xy*shape.xy/z)*vec2(.5,-.5)+vec2(.5);
        if (any(lessThan(uv,vec2(0.0))) || any(greaterThan(uv,vec2(1.0)))) continue;
        vec4 tile = texelFetch(svm_spots,ivec2(6,i),0), shadow = texelFetch(svm_spots,ivec2(7,i),0);
        float stored = textureLod(svm_spot_depth,tile.xy+uv*tile.zw,0.0).r;
        if (z-shadow.y-shadow.z*z > stored) continue;
        vec3 source_position = -(r0.xyz*r0.w+r1.xyz*r1.w+r2.xyz*r2.w);
        vec3 toward = source_position-wp;
        float distance = length(toward)*100.0;
        vec3 direction = shape.z > .5 ? r2.xzy : normalize(toward+vec3(.000001)).xzy;
        float attenuation = a.w < .5 ? 1.0 : clamp(1.0-(z-color.a)/a.y,0.0,1.0);
        vec3 incident = color.rgb*textureLod(svm_spot_cookies,vec3(uv,shape.w),0.0).rgb*attenuation;
        if (max(incident.r,max(incident.g,incident.b)) < .0001) continue;
        float transmission = smoke_light_transmission(cloud_base,cloud_count,ue,direction,distance,now,quality);
        sum += incident*smoke_scattered_light(smoke_phase_hg(dot(direction,ray)),transmission);
    }
    return sum;
}

// Spot illumination depends on the view's union field, not this cloud. Callers integrating overlapping
// clouds may reuse it, but it must enter before each cloud's nonlinear compression and detail albedo.
vec3 smoke_lighting_with_spots(int cloud, vec3 ue, vec3 ray, float now, int quality, int cloud_base, int cloud_count,
                              vec3 common_spots) {
    vec3 ambient = max(smoke_frame_row(cloud+1).rgb,vec3(.035)), direct = vec3(0.0);
    if (smoke_frame_row(cloud+1).w > .5) {
        vec3 direction; float distance;
        smoke_sample_light_probes(cloud,ue,ambient,direct,direction,distance);
        ambient = max(ambient,vec3(.035));
        float coherence = clamp(length(direction),0.0,1.0);
        if (coherence > .001 && max(direct.r,max(direct.g,direct.b)) > .0001) {
            direction /= coherence;
            float transmission = smoke_light_transmission(cloud_base,cloud_count,ue,direction,distance,now,quality);
            direct *= mix(1.0,smoke_scattered_light(smoke_phase_hg(dot(direction,ray)),transmission),coherence);
        }
    } else {
        // Fixtures without a map retain packed lights. Directional positions are directions, not locations.
        vec3 wp = ue.xzy*.01;
        int count = int(smoke_frame_row(cloud).y);
        for (int i = 0; i < count; i++) {
            vec4 p = smoke_frame_row(cloud+2+i*3), color = smoke_frame_row(cloud+3+i*3), cone = smoke_frame_row(cloud+4+i*3);
            vec3 delta = p.w < 0.0 ? p.xyz : p.xyz-wp;
            float range = length(delta), attenuation = 1.0;
            vec3 direction = normalize(delta+vec3(.000001));
            if (p.w >= 0.0) {
                if (cone.w > -3.5 && cone.w < -2.5) {
                    float ratio = range/max(p.w,.001);
                    attenuation = ratio >= 1.0 ? 0.0 : 1.0/max(8.0*ratio*ratio,.001);
                } else attenuation = range > (256.0-color.w)/max(1.0,p.w) ? 0.0 : 1.0/max(color.w+p.w*range,.01);
                if (cone.w >= 0.0) attenuation *= smoothstep(cone.w,min(1.0,cone.w+.1),dot(-direction,cone.xyz));
            }
            if (attenuation <= .0001) continue;
            float transmission = smoke_light_transmission(cloud_base,cloud_count,ue,direction.xzy,p.w < 0.0 ? 625.0 : range*100.0,now,quality);
            direct += color.rgb*attenuation*smoke_scattered_light(smoke_phase_hg(dot(direction.xzy,ray)),transmission);
        }
    }
    direct += common_spots*.55;
    float detail;
    vec4 cloud_info = smoke_frame_row(cloud);
    int field = int(cloud_info.x);
    float age = now-smoke_field_row(field+1).x;
    vec3 material = smoke_material_coordinate(ue-smoke_field_row(field+2).xyz,age);
    float macro = smoke_material_macro(material);
    if (svm_smoke_flow_ready && cloud_info.z > .5 && cloud_info.w > .5) {
        vec3 local = (ue-smoke_field_row(field).xyz)/625.0;
        float border = .5/cloud_info.w;
        local = clamp(local,vec3(border),vec3(1.0-border));
        vec3 uv = vec3(local.xy,(cloud_info.z-1.0+local.z)/8.0);
        detail = clamp(textureLod(svm_smoke_flow,uv,0.0).w,0.0,1.0);
    } else detail = smoke_shape_noise(material/55.0+vec3(23.41,3.71,13.29));
    // Connected folds at several scales break up broad rounded crowns. All procedural detail follows the
    // silhouette's material motion; transported detail adds rolling eddies without changing extinction.
    float folds = smoke_shape_noise(material/42.0+vec3(7.13,17.71,5.29));
    if (quality > 1) folds = folds*.8+.2*smoke_shape_noise(material/18.0+vec3(13.61,5.73,21.19));
    float billow=.40*macro+.30*smoothstep(.32,.72,detail)+.30*smoothstep(.25,.75,folds);
    // Compress incident display-space light before the lobe albedo. A hard final clamp made every bright
    // crown the same white, erasing transported creases under the map's strong actor-light probes.
    vec3 incident = ambient+direct;
    incident /= 1.0+.65*max(incident.r,max(incident.g,incident.b));
    return clamp(incident*(.60+.40*billow),vec3(.025),vec3(.9));
}

// Keep the standalone lighting entry point for diagnostics and single-cloud callers.
vec3 smoke_lighting(int cloud, vec3 ue, vec3 ray, float now, int quality, int cloud_base, int cloud_count) {
    vec3 spots = smoke_spot_light(ue,ray,now,quality,cloud_base,cloud_count);
    return smoke_lighting_with_spots(cloud,ue,ray,now,quality,cloud_base,cloud_count,spots);
}
