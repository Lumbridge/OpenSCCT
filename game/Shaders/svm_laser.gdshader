// A laser beam in the vision modes that show it (docs/RENDERING.md#vision-modes): thermal draws the beam at its actor's
// HeatIntensity (SLaserBeam default 1) with the depth fade. EMF draws the connecting beam as a non-glowing surface;
// the post inversion makes it dark grey. Weapon lasers use it as the vision pass of their additive look: the merc
// laser sight enables EMF too, while thermal_only leaves the spy pistol's EMF to its additive look. The mask keeps
// both vision modes to the texels the beam texture lights (its bright core, not the whole quad).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_opaque;

#include "res://Shaders/svm_laser.gdshaderinc"
