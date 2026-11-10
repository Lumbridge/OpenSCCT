// The sky zone drawn in the main view (docs/RENDERING.md#sky-zone). Its surfaces keep the depth the sky camera gave
// them, scaled down towards the far plane (reverse Z: 0 there), so every surface of the main world lies in front of
// the whole sky zone while the sky keeps its own depth order (the depth buffer is float: scaling keeps its precision).
// Shared by the world template, the responsive smoke background and the compute passes that read depth.
// Written as exp2 calls, not literals: Godot's shader compiler re-emits float literals with 14 fixed decimals
// (shader_compiler.cpp f2sp0 -> String::num), which turns 2^-50 into 0.0 and 2^-40 into 9.1e-13. exp2 of a whole
// number is exact and folded by the GLSL compiler.

// The sky's depth scale: its surfaces end at this depth on the near plane (2^-50, exact, so the scaling is lossless).
#define SVM_SKY_DEPTH_SCALE exp2(-50.0)

// Depths at or below this are the sky zone or nothing drawn, as depth 0 was before (2^-40). The headroom over
// SVM_SKY_DEPTH_SCALE covers sky surfaces up to 1/1024 of the near distance from the SkyZoneInfo, which are not
// near-clipped. A main-world surface gets here only within 0.02 mm of the far plane.
#define SVM_SKY_DEPTH_LIMIT exp2(-40.0)
