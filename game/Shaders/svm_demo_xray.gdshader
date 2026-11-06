shader_type canvas_item;
render_mode unshaded;

// The mask camera sees the posed bodies in isolation. Dilate their silhouettes by two pixels,
// then subtract the original mask so the scene inside each silhouette remains visible.
const vec2 EDGE_OFFSETS[12] = vec2[12](
	vec2(-2, 0), vec2(2, 0), vec2(0, -2), vec2(0, 2),
	vec2(-1, -1), vec2(-1, 1), vec2(1, -1), vec2(1, 1),
	vec2(-1, 0), vec2(1, 0), vec2(0, -1), vec2(0, 1)
);

void fragment() {
	if (texture(TEXTURE, UV).a > 0.5) {
		discard;
	}

	vec4 edge = vec4(0.0);
	for (int i = 0; i < 12; i++) {
		vec4 neighbor = texture(TEXTURE, UV + EDGE_OFFSETS[i] * TEXTURE_PIXEL_SIZE);
		if (neighbor.a > edge.a) {
			edge = neighbor;
		}
	}
	COLOR = vec4(edge.rgb, edge.a * 0.96);
}
