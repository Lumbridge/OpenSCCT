// Laid over the merc's first-person arms and rifle (FirstPersonView): draws nothing and marks the gun's pixels in the
// stencil (MaterialCompiler.StencilFirstPerson). The original draws the view model over the world (the weapon
// overlay), so the gun covers the merc's laser beam where it passes; here the model is drawn in the world, and the
// beam skips the marked pixels instead. Drawn first in the transparent pass (lowest render priority), before the beam.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;
stencil_mode write, compare_always, 4;

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}
