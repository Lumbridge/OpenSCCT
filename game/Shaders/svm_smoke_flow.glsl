#version 450

layout(local_size_x=4,local_size_y=4,local_size_z=4) in;
layout(set=0,binding=0) uniform sampler3D previous_state;
layout(rgba16f,set=0,binding=1) uniform writeonly image3D next_state;
layout(std430,set=0,binding=2) readonly buffer Cells { uint cell_data[]; };
layout(std430,set=0,binding=3) readonly buffer Impulses { float event_data[]; };
layout(push_constant,std430) uniform Parameters {
    ivec4 grid; // slot, resolution, capacity
    vec4 timing; // delta time, cloud age, stable seed, reset
    vec4 origin; // authoritative origin in UE units, creation time
    ivec4 events; // scalar offset, count
} pc;

bool valid_cell(ivec3 p) { return all(greaterThanEqual(p,ivec3(0))) && all(lessThan(p,ivec3(25))); }
int index_of(ivec3 p) { return p.x+25*(p.y+25*p.z); }
uint cell_at(ivec3 p) { return valid_cell(p) ? cell_data[pc.grid.x*15625+index_of(p)] : 0u; }
bool free_at(vec3 local) { return cell_at(ivec3(floor(local/25.0))) != 0u; }
vec3 atlas(vec3 local) {
    float n = float(pc.grid.y);
    vec3 uv = clamp(local/625.0,vec3(.5/n),vec3(1.0-.5/n));
    uv.z = (float(pc.grid.x)+uv.z)/float(pc.grid.z);
    return uv;
}
vec4 sample_state(vec3 local) { return textureLod(previous_state,atlas(local),0.0); }
float hash(vec3 p) { p=fract(p*.1031); p+=dot(p,p.yzx+33.33); return fract((p.x+p.y)*p.z); }
float noise(vec3 p) {
    vec3 c=floor(p),f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(mix(hash(c),hash(c+vec3(1,0,0)),f.x),mix(hash(c+vec3(0,1,0)),hash(c+vec3(1,1,0)),f.x),f.y),
               mix(mix(hash(c+vec3(0,0,1)),hash(c+vec3(1,0,1)),f.x),mix(hash(c+vec3(0,1,1)),hash(c+vec3(1,1,1)),f.x),f.y),f.z);
}
vec3 curl(vec3 p,float age) {
    // The sine field's period is 2*pi*scale: 38 uu gives cloud-local rolls about 239 uu across.
    vec3 q=p/38.0+vec3(age*.05,-age*.04,age*.03)+vec3(pc.timing.z);
    return vec3(-sin(q.x)*sin(q.y)-cos(q.z)*cos(q.x),
                -sin(q.y)*sin(q.z)-cos(q.x)*cos(q.y),
                -sin(q.z)*sin(q.x)-cos(q.y)*cos(q.z));
}

// Walk every crossed logical cell, including edge/corner ties. Sampling only the backtrace endpoint permits
// velocity to carry detail through a thin wall; clipping at its first blocked boundary prevents that transport.
vec3 clip_backtrace(vec3 start,vec3 end) {
    vec3 delta=end-start;
    ivec3 cell=ivec3(floor(start/25.0)), target=ivec3(floor(end/25.0));
    ivec3 cell_step=ivec3(sign(delta));
    vec3 inv=1.0/max(abs(delta),vec3(.000001));
    vec3 next=(vec3(cell)+step(vec3(0),delta))*25.0;
    vec3 tmax=abs(next-start)*inv, tinc=25.0*inv;
    for(int axis=0;axis<3;axis++) if(abs(delta[axis])<.000001) { tmax[axis]=1e20; tinc[axis]=1e20; }
    for(int i=0;i<32;i++) {
        if(all(equal(cell,target))) return end;
        float boundary=min(tmax.x,min(tmax.y,tmax.z));
        if(boundary>1.0) return end;
        int tied=0;
        for(int axis=0;axis<3;axis++) if(abs(tmax[axis]-boundary)<.00001) tied|=1<<axis;
        // Supercover corner crossings: check every adjacent face/edge cell, not just the diagonal endpoint.
        for(int subset=1;subset<8;subset++) {
            if((subset&tied)!=subset) continue;
            ivec3 candidate=cell;
            for(int axis=0;axis<3;axis++) if((subset&(1<<axis))!=0) candidate[axis]+=cell_step[axis];
            if(cell_at(candidate)==0u) return start+delta*max(0.0,boundary-.001);
        }
        for(int axis=0;axis<3;axis++) if((tied&(1<<axis))!=0) { cell[axis]+=cell_step[axis]; tmax[axis]+=tinc[axis]; }
    }
    return start;
}
vec4 sample_visible(vec3 local) {
    float h=625.0/float(pc.grid.y);
    vec3 coordinate=local/h-.5, fraction=fract(coordinate);
    ivec3 base=ivec3(floor(coordinate));
    vec4 result=vec4(0);
    float weight_sum=0.0;
    for(int corner=0;corner<8;corner++) {
        ivec3 bit=ivec3(corner&1,(corner>>1)&1,(corner>>2)&1);
        ivec3 tap=base+bit;
        if(any(lessThan(tap,ivec3(0))) || any(greaterThanEqual(tap,ivec3(pc.grid.y)))) continue;
        vec3 center=(vec3(tap)+.5)*h;
        if(!free_at(center) || length(clip_backtrace(local,center)-center)>.01) continue;
        vec3 weights=mix(vec3(1)-fraction,fraction,vec3(bit));
        float weight=weights.x*weights.y*weights.z;
        result+=texelFetch(previous_state,tap+ivec3(0,0,pc.grid.x*pc.grid.y),0)*weight;
        weight_sum+=weight;
    }
    return weight_sum>.00001 ? result/weight_sum : vec4(0);
}
vec3 neighbor_velocity(vec3 local,vec3 offset) {
    vec3 neighbor=local+offset;
    return free_at(neighbor) && length(clip_backtrace(local,neighbor)-neighbor)<.01 ? sample_state(neighbor).xyz : vec3(0);
}
vec4 event_row(int base) { return vec4(event_data[base],event_data[base+1],event_data[base+2],event_data[base+3]); }

void main() {
    ivec3 xyz=ivec3(gl_GlobalInvocationID.xyz);
    if(any(greaterThanEqual(xyz,ivec3(pc.grid.y)))) return;
    ivec3 output_xyz=xyz+ivec3(0,0,pc.grid.x*pc.grid.y);
    vec3 local=(vec3(xyz)+.5)*(625.0/float(pc.grid.y));
    if(pc.timing.x==0.0 && pc.timing.w<.5 && pc.events.y==0 && pc.grid.w==0) {
        imageStore(next_state,output_xyz,texelFetch(previous_state,output_xyz,0));
        return;
    }
    ivec3 logical=ivec3(floor(local/25.0));
    uint cell=cell_at(logical);
    if(cell==0u || cell==65536u) { imageStore(next_state,output_xyz,vec4(0)); return; }
    float dt=pc.timing.x, age=pc.timing.y, activation=float(cell-1u)/20.0;
    float arrival=clamp((age-activation)/.10,0.0,1.0); // ResponsiveSmokeField.GrowthFade.
    if(arrival<=0.0) { imageStore(next_state,output_xyz,vec4(0)); return; }
    vec3 radial=local-vec3(312.5);
    // Broad transported lobes survive advection and line integration; a smaller band gives their rims detail.
    // This scalar is illumination detail only, independent of authoritative optical density and clearing.
    vec3 seed=vec3(pc.timing.z);
    float broad=noise(local/110.0+seed);
    float fine=noise(local/45.0+seed+vec3(17.13,31.7,7.9));
    float seed_density=.10+.90*(.75*broad+.25*fine);
    vec4 previous=texelFetch(previous_state,output_xyz,0);
    bool reset=pc.timing.w>.5;
    vec3 velocity;
    float density;
    if(reset) {
        velocity=normalize(radial+vec3(.001))*45.0*exp(-max(age,0.0)*2.0)+curl(local,age)*22.0;
        density=seed_density*arrival;
    } else {
        vec3 traced=clip_backtrace(local,local-previous.xyz*dt);
        vec4 advected=dt>0.0 ? sample_visible(traced) : previous;
        velocity=advected.xyz;
        density=advected.w;
        if(dt>0.0) {
            // Smooth the transported vector field rather than running an expensive pressure solve. The driving
            // curl is divergence-free; damping limits compression without removing the visible rolling motion.
            float h=625.0/float(pc.grid.y);
            vec3 mean=(neighbor_velocity(local,vec3(h,0,0))+neighbor_velocity(local,vec3(-h,0,0))+
                       neighbor_velocity(local,vec3(0,h,0))+neighbor_velocity(local,vec3(0,-h,0))+
                       neighbor_velocity(local,vec3(0,0,h))+neighbor_velocity(local,vec3(0,0,-h)))/6.0;
            velocity=mix(velocity,mean,clamp(dt*.35,0.0,1.0));
            velocity+=(curl(local,age)*30.0+vec3(0,0,8.0+12.0*density))*dt;
            velocity*=exp(-dt*.6);
            density*=exp(-dt*.025);
        }
        // New reachable cells receive the initial puff while existing detail moves with its velocity field.
        float injection=clamp((.65-(age-activation))*3.0,0.0,1.0);
        density=mix(density,seed_density*arrival,injection*min(1.0,dt*6.0));
    }
    vec3 world=pc.origin.xyz+local;
    for(int i=0;i<pc.events.y;i++) {
        int at=pc.events.x+i*16;
        vec4 a=event_row(at),b=event_row(at+4),timing=event_row(at+8);
        int mask=int(event_data[at+12]);
        if(mask!=0 && event_data[mask+index_of(logical)]<.5) continue;
        vec3 segment=b.xyz-a.xyz;
        vec3 closest=a.xyz+segment*clamp(dot(world-a.xyz,segment)/max(dot(segment,segment),.00001),0.0,1.0);
        vec3 away=world-closest;
        float distance=length(away), extent=a.w+b.w+(timing.w>.5 ? 45.0 : 28.0);
        // Most voxels lie outside a narrow shot's influence. Reject them before the wall traversal;
        // otherwise every rifle impulse walks the grid for the entire cloud, including unaffected cells.
        if(distance>=extent) continue;
        float influence=1.0-smoothstep(a.w*.35,extent,distance);
        float remaining=1.0-smoothstep(timing.y,timing.y+max(timing.z,.001),age+pc.origin.w-timing.x);
        if(remaining<=0.0) continue;
        vec3 impulse_local=clamp(closest-pc.origin.xyz,vec3(.001),vec3(624.999));
        if(timing.w<.5 && length(clip_backtrace(local,impulse_local)-impulse_local)>.01) continue;
        vec3 direction=distance>.001 ? away/distance : normalize(curl(local,age)+vec3(.001));
        velocity+=direction*influence*remaining*(timing.w>.5 ? 150.0 : 85.0);
        density*=1.0-influence*remaining*(timing.w>.5 ? .8 : .45);
    }
    velocity=clamp(velocity,vec3(-140),vec3(140));
    // Remove components entering obstacles, keeping tangential flow along walls, ceiling and closed doors.
    float wall_probe=max(625.0/float(pc.grid.y)*.55,3.0);
    for(int axis=0;axis<3;axis++) {
        vec3 probe=local; probe[axis]+=sign(velocity[axis])*wall_probe;
        if(!free_at(probe)) velocity[axis]=0.0;
    }
    float boundary=1.0-smoothstep(280.0,310.0,length(radial));
    // Arrival and the local envelope bound the stored detail. Multiplying them into persistent state every
    // dispatch would erase the rim exponentially, including on zero-time geometry/impulse updates.
    density=min(clamp(density,0.0,1.0),arrival*boundary);
    imageStore(next_state,output_xyz,vec4(velocity,density));
}
