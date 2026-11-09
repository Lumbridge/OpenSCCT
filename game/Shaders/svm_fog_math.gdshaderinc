// Shared fog equations for material fragments and the independent compositor fallback.
// The caller provides svm_fog_row(base, index), selecting either texture or SSBO storage.
// Independently implemented from the behavioral rules in spec/native/enhanced-zone-fog.md.
void svm_fog_add_ramp(inout float cover, inout vec3 color, vec4 ramp, vec3 ramp_color, float distance, float sign) {
	float part = ramp.w * sign * max(distance, ramp.x) * ramp.z;
	cover += part;
	color += part * ramp_color;
}

void svm_fog_add_zone(int fog_base, inout float cover, inout vec3 color, int zone, float distance, float sign) {
	int at = 6 + clamp(zone, 0, 63) * 2;
	svm_fog_add_ramp(cover, color, svm_fog_row(fog_base, at), svm_fog_row(fog_base, at + 1).rgb, distance, sign);
}

bool svm_fog_portal_crossing(int fog_base, int at, vec3 origin, vec3 ray, float limit, out float distance, out ivec2 zones) {
	vec4 plane = svm_fog_row(fog_base, at);
	vec4 info = svm_fog_row(fog_base, at + 1);
	float along = dot(plane.xyz, ray);
	zones = along > 0.0 ? ivec2(info.xy) : ivec2(info.yx);
	distance = 0.0;
	if (abs(along) < 1e-7) return false;
	distance = (plane.w - dot(plane.xyz, origin)) / along;
	if (distance <= 0.0 || distance >= limit) return false;
	vec3 hit = origin + ray * distance;
	for (int e = 0; e < int(info.w); e++) {
		vec4 edge = svm_fog_row(fog_base, int(info.z) + e);
		if (dot(edge.xyz, hit) < edge.w) return false;
	}
	return true;
}

// A candidate row is (portal, first partner row, partner count, 0). Partners are the earlier candidates in the same
// tile that join the same two zones: no other earlier candidate can match zones, so none can repeat this crossing.
bool svm_fog_distinct_crossing(int fog_base, int portal_base, vec4 candidate, vec3 origin, vec3 ray, float distance, out float at, out ivec2 zones) {
	if (!svm_fog_portal_crossing(fog_base, portal_base + int(candidate.x) * 2, origin, ray, distance, at, zones)) return false;
	for (int p = 0; p < int(candidate.z); p++) {
		float other_at;
		ivec2 other_zones;
		int other = int(svm_fog_row(fog_base, int(candidate.y) + p).x);
		if (svm_fog_portal_crossing(fog_base, portal_base + other * 2, origin, ray, distance, other_at, other_zones)
			&& all(equal(zones, other_zones)) && abs(other_at - at) < 0.001 * max(at, 1.0)) return false;
	}
	return true;
}

// Integral of density along a ray. Height <= floor has full density; above it the authored distance halves it.
float svm_fog_density_run(vec4 shape, float vertical, float a, float b) {
	if (shape.y <= 0.0) return b - a;
	vec2 height = vertical * vec2(a, b) - shape.x;
	vec2 density = exp2(-max(height, vec2(0.0)) / shape.y);
	vec2 primitive = min(height, vec2(0.0)) - shape.y * 1.4426950408889634 * density;
	float rise = height.y - height.x;
	return (b - a) * (abs(rise) > 0.001 ? (primitive.y - primitive.x) / rise : density.x);
}

float svm_fog_clamp_primitive(float value) {
	return value <= 0.0 ? 0.0 : value < 1.0 ? 0.5 * value * value : value - 0.5;
}

float svm_fog_faded_run(int fog_base, vec4 shape, vec4 info, vec3 ray, float a, float b) {
	vec2 nearest = vec2(1e30);
	for (int i = 0; i < int(info.z); i++) {
		vec4 plane = svm_fog_row(fog_base, int(info.x) + i);
		nearest = min(nearest, plane.w - dot(plane.xyz, ray) * vec2(a, b));
	}
	nearest /= shape.z;
	float low = min(nearest.x, nearest.y), high = max(nearest.x, nearest.y);
	float average = high - low < 0.00001 ? clamp(low, 0.0, 1.0)
		: (svm_fog_clamp_primitive(high) - svm_fog_clamp_primitive(low)) / (high - low);
	return average * svm_fog_density_run(shape, ray.z, a, b);
}

float svm_fog_volume_depth(int fog_base, int header, vec3 ray, float distance, float start) {
	vec4 shape = svm_fog_row(fog_base, header + 1), info = svm_fog_row(fog_base, header + 2);
	float enter = 0.0, leave = distance, clear_start = 0.0, clear_end = distance;
	for (int i = 0; i < int(info.y); i++) {
		vec4 plane = svm_fog_row(fog_base, int(info.x) + i);
		float speed = dot(plane.xyz, ray);
		float fade = i < int(info.z) ? max(shape.z, 0.0) : 0.0;
		if (abs(speed) < 1e-7) {
			if (plane.w < 0.0) return 0.0;
			if (plane.w < fade) { clear_start = distance; clear_end = 0.0; }
			continue;
		}
		float cross_at = plane.w / speed, clear_at = (plane.w - fade) / speed;
		if (speed > 0.0) { leave = min(leave, cross_at); clear_end = min(clear_end, clear_at); }
		else { enter = max(enter, cross_at); clear_start = max(clear_start, clear_at); }
	}
	enter = max(enter, start);
	if (leave <= enter) return 0.0;
	clear_start = clamp(max(clear_start, start), enter, leave);
	clear_end = clamp(clear_end, enter, leave);
	if (clear_end <= clear_start) clear_start = clear_end = (enter + leave) * 0.5;
	float integral = svm_fog_density_run(shape, ray.z, clear_start, clear_end);
	if (shape.z > 0.0 && info.z > 0.0)
		integral += svm_fog_faded_run(fog_base, shape, info, ray, enter, clear_start) + svm_fog_faded_run(fog_base, shape, info, ray, clear_end, leave);
	return svm_fog_row(fog_base, header).w * integral;
}

// The rows every pixel of a view reads: eye, grid, layout and the camera zone's ramp pair.
void svm_fog_view_rows(int fog_base, out vec4 eye, out vec4 grid, out vec4 layout_info, out vec4 ramp, out vec4 ramp_color) {
	eye = svm_fog_row(fog_base, 0);
	grid = svm_fog_row(fog_base, 4);
	layout_info = svm_fog_row(fog_base, 5);
	int at = 6 + clamp(int(eye.w), 0, 63) * 2;
	ramp = svm_fog_row(fog_base, at);
	ramp_color = svm_fog_row(fog_base, at + 1);
}

// As svm_fog_evaluate, with the view's rows supplied by the caller (material shaders take the main view's from uniforms).
vec4 svm_fog_evaluate_view(int fog_base, vec4 eye, vec4 grid, vec4 layout_info, vec4 ramp, vec3 ramp_color, vec2 screen_uv, vec3 ray, float distance) {
	// Binning uses the output resolution even when the 3D buffer is rendered at a lower scale.
	ivec2 pixel = clamp(ivec2(screen_uv * grid.xy), ivec2(0), max(ivec2(grid.xy) - 1, ivec2(0)));
	ivec2 tile = pixel / 32; // PortalFogFrame.TileSize
	vec4 cell = svm_fog_row(fog_base, int(grid.w) + tile.y * int(grid.z) + tile.x);
	if (cell.y == 0.0 && layout_info.y == 0.0 && layout_info.w == 0.0) return vec4(0.0, 0.0, 0.0, 1.0);
	vec3 origin = eye.xyz;
	int camera = int(eye.w), last = camera;
	int portal_base = int(layout_info.x);
	float last_at = 0.0, cover = 0.0;
	vec3 weighted_color = vec3(0.0);
	svm_fog_add_ramp(cover, weighted_color, ramp, ramp_color, 0.0, -1.0);
	for (int k = 0; k < int(cell.y); k++) {
		float at;
		ivec2 zones;
		vec4 candidate = svm_fog_row(fog_base, int(cell.x) + k);
		if (!svm_fog_distinct_crossing(fog_base, portal_base, candidate, origin, ray, distance, at, zones)) continue;
		svm_fog_add_zone(fog_base, cover, weighted_color, zones.x, at, 1.0);
		svm_fog_add_zone(fog_base, cover, weighted_color, zones.y, at, -1.0);
		if (at > last_at) { last_at = at; last = zones.y; }
	}
	// The camera is zone 0-63 (PortalFogFrame), so last == camera reads the same row svm_fog_add_zone would.
	if (last == camera) svm_fog_add_ramp(cover, weighted_color, ramp, ramp_color, distance, 1.0);
	else svm_fog_add_zone(fog_base, cover, weighted_color, last, distance, 1.0);
	float total_depth = 0.0;
	vec3 volume_color = vec3(0.0);
	for (int piece = 0; piece < int(layout_info.w); piece++) {
		int header = int(layout_info.z) + piece * 3;
		int zone = int(svm_fog_row(fog_base, header + 1).w);
		float depth = zone < 0 || zone == camera ? svm_fog_volume_depth(fog_base, header, ray, distance, 0.0) : 0.0;
		if (zone >= 0) {
			for (int k = 0; k < int(cell.y); k++) {
				float at;
				ivec2 zones;
				vec4 candidate = svm_fog_row(fog_base, int(cell.x) + k);
				// Only a portal with this zone on exactly one side changes the depth; skip the others' crossing tests.
				vec4 sides = svm_fog_row(fog_base, portal_base + int(candidate.x) * 2 + 1);
				if (float(int(sides.x) == zone) == float(int(sides.y) == zone)) continue;
				if (!svm_fog_distinct_crossing(fog_base, portal_base, candidate, origin, ray, distance, at, zones)) continue;
				float change = float(zones.y == zone) - float(zones.x == zone);
				if (change != 0.0) depth += change * svm_fog_volume_depth(fog_base, header, ray, distance, at);
			}
		}
		depth = max(depth, 0.0);
		total_depth += depth;
		volume_color += depth * svm_fog_row(fog_base, header).rgb;
	}
	float zone_opacity = clamp(cover, 0.0, 1.0), volume_opacity = 1.0 - exp(-total_depth);
	float through = (1.0 - zone_opacity) * (1.0 - volume_opacity), weight = zone_opacity + volume_opacity;
	vec3 combined = cover > 0.0 ? weighted_color * (zone_opacity / cover) : vec3(0.0);
	if (total_depth > 0.0) combined += volume_opacity / total_depth * volume_color;
	return vec4(weight > 0.0 ? (1.0 - through) / weight * combined : vec3(0.0), through);
}

vec4 svm_fog_evaluate(int fog_base, vec2 screen_uv, vec3 ray, float distance) {
	vec4 eye, grid, layout_info, ramp, ramp_color;
	svm_fog_view_rows(fog_base, eye, grid, layout_info, ramp, ramp_color);
	return svm_fog_evaluate_view(fog_base, eye, grid, layout_info, ramp, ramp_color.rgb, screen_uv, ray, distance);
}
