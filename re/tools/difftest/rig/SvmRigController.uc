class SvmRigController extends SPlayerController;

// The combat rig's target player (SvmCombatRig): an SPlayerController with no Player behind it, so the damage
// pipeline (PawnTakeDamage on the victim's SPlayerController) and every state reaction run as for a remote player
// on a host. Nothing is overridden: the class only marks the rig's controller in memory.
