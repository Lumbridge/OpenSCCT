// A laser beam in the vision modes that show it (docs/RENDERING.md#vision-modes): thermal draws the beam at its actor's
// HeatIntensity (SLaserBeam default 1) with the depth fade, EMF draws it at full EMF glow (the inverting post pass
// makes it white); the other modes never show it (bHideInNormalVision). The weapon lasers (spy pistol, merc laser
// sight) use it as the thermal pass of their additive look: thermal_only leaves EMF to that look, and mask keeps the
// heat to the texels the beam texture lights (its bright core, not the whole quad).
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_draw_opaque;

#include "res://Shaders/svm_laser.gdshaderinc"
