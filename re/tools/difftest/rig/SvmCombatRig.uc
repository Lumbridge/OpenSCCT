class SvmCombatRig extends Info
	placeable;

// The combat rig of the difftest recorder (re/tools/difftest/combat.py). It is compiled into a sandbox copy of a
// stock map (MyLevel) and placed once; in Play Level it spawns a target player (a second controller and pawn made
// the way the game makes a player: its own SPlayerController, a team, a name, RestartPlayer) and runs commands the
// recorder writes into its fields from outside: write the arguments, then Cmd, then Seq + 1; the rig runs the
// command in its next Tick and copies Seq to Ack, with Status 1 for done and a negative value for refused.
// It never writes Physics.

var int Seq, Ack, Cmd, Status;
var int I0, I1, I2, I3;
var float F0, F1, F2, F3, F4, F5;
var vector V0, V1, V2;              // outputs (bone positions, aim origin)
var int Out0, Out1;

var SPlayerController Shooter;      // the local player
var SvmRigController Dummy;         // the target's controller
var SBasePawn Target;
var Projectile Grenade;             // the last grenade the rig spawned
var float GrenadeFuseAt;
var float GrenadeSpawnedAt;
var float GrenadeGoneAt;            // when it was seen destroyed (went off)
var bool bGrenadeRigFuse;           // the rig ran its fuse end (its own timer had not)
var vector GrenadeLastLocation;
var int TickCount;
var float FrameTime;                // Level.TimeSeconds of the last Tick
var int Spawns;
var vector StartSpotLocation;      // the team start RestartPlayer chose for the target
// A new target is moved to its spot 0.5 s and 1 s after RestartPlayer (its start stance changes its collision height
// over the first frames; placed in the spawn's own Tick it ended up in the floor).
var bool bPlacePending;
var float PlaceAt;
var vector PlaceSpot;
var int PlaceYaw;
var int PlaceCount;
// A placed target is let fall onto its spot from PlaceLift uu up: put down on the floor of a walking pawn, the
// walking physics found no floor under it after the move and it sank through (36 uu a frame). Once it lands, its
// landing state (which its owner's PlayerTick would end) is ended with GotoWaitingState.
var float PlaceLift;
var bool bSettle;
var float SettleUntil;

// Bullet impacts: every SFireTrace (the rifle's tracer, ImpactLoc = the trace's HitLocation) seen in a Tick.
var vector Impacts[32];
var vector ImpactStarts[32];
var float ImpactTimes[32];
var int NumImpacts;                 // impacts seen so far; the newest is at (NumImpacts - 1) % 32
var SFireTrace Recent[16];
var int NumRecent;

function Tick(float DeltaTime)
{
	TickCount++;
	FrameTime = Level.TimeSeconds;
	if (Shooter == None)
		Shooter = SPlayerController(Level.GetLocalPlayerController());
	ScanTracers();
	if (bSettle && Target != None && Dummy != None)
	{
		if (Target.Physics == PHYS_Walking && Target.Base != None && (Dummy.IsInState('PlayerLanding')
			|| Dummy.IsInState('PlayerLandingSilent') || Dummy.IsInState('PlayerFalling')))
		{
			Dummy.GotoWaitingState();
			bSettle = false;
		}
		else if (Level.TimeSeconds > SettleUntil)
			bSettle = false;
	}
	if (bPlacePending && Target != None && Level.TimeSeconds >= PlaceAt)
	{
		PlaceTargetAt(PlaceSpot, PlaceYaw);
		PlaceCount++;
		PlaceAt = Level.TimeSeconds + 0.5;
		bPlacePending = PlaceCount < 2;
	}
	if (Seq != Ack)
	{
		Status = 0;
		RunCommand();
		if (Status == 0)
			Status = 1;
		Ack = Seq;
	}
	// A grenade the rig spawned goes off at its fuse even when its own timer does not run.
	if (Grenade != None && !Grenade.bDeleteMe)
	{
		GrenadeLastLocation = Grenade.Location;
		if (GrenadeFuseAt > 0 && Level.TimeSeconds >= GrenadeFuseAt)
		{
			GrenadeFuseAt = 0;
			bGrenadeRigFuse = true;
			Grenade.Timer();
		}
	}
	if (GrenadeSpawnedAt > 0 && GrenadeGoneAt == 0 && (Grenade == None || Grenade.bDeleteMe))
		GrenadeGoneAt = Level.TimeSeconds;
}

function ScanTracers()
{
	local SFireTrace T;
	local int i;
	local bool bSeen;

	foreach DynamicActors(class'SFireTrace', T)
	{
		bSeen = false;
		for (i = 0; i < 16; i++)
		{
			if (Recent[i] == T)
				bSeen = true;
		}
		if (!bSeen)
		{
			Recent[NumRecent % 16] = T;
			NumRecent++;
			Impacts[NumImpacts % 32] = T.ImpactLoc;
			ImpactStarts[NumImpacts % 32] = T.Location;
			ImpactTimes[NumImpacts % 32] = Level.TimeSeconds;
			NumImpacts++;
		}
	}
}

function name BoneName(int Index)
{
	switch (Index)
	{
		case 1: return 'B Head';
		case 2: return 'B Spine2';
		case 3: return 'B Spine1';
		case 4: return 'B Pelvis';
		case 5: return 'B L UpperArm';
		case 6: return 'B L ForeArm';
		case 7: return 'B L Hand';
		case 8: return 'B L Thigh';
		case 9: return 'B L Calf';
		case 10: return 'B L Foot';
		case 11: return 'B R UpperArm';
		case 12: return 'B R ForeArm';
		case 13: return 'B R Hand';
		case 14: return 'B R Thigh';
		case 15: return 'B R Calf';
		case 16: return 'B R Foot';
		case 17: return 'B Spine';
		case 18: return 'B Neck';
	}
	return 'None';
}

function vector BonePoint(int Index)
{
	return BonePointOf(Target, Index);
}

function vector BonePointOf(Pawn P, int Index)
{
	local Coords C;

	if (P == None || Index <= 0)
		return vect(0,0,0);
	C = P.GetBoneCoords(BoneName(Index), true);
	return C.Origin;
}

function RunCommand()
{
	switch (Cmd)
	{
		case 1: SpawnTarget(); break;
		case 2: PlaceTarget(); break;
		case 3: if (Target != None) Target.Health = I0; else Status = -1; break;
		case 4: Aim(); break;
		case 5: PressFire(); break;
		case 6: if (Shooter != None) Shooter.bFire = 0; break;
		case 7: PressAltFire(); break;
		case 8: if (Shooter != None) Shooter.bAltFire = 0; break;
		case 9: V0 = BonePoint(I0); break;
		case 10: SpawnGrenade(); break;
		case 11: SetROF(); break;
		case 12: RemoveTarget(); break;
		case 13: ClearHistory(); break;
		case 14: Teleport(); break;
		case 15: if (Target != None) Target.ShouldCrouch(I0 != 0); else Status = -1; break;
		case 16: Floor(); break;
		case 17: DummyRole(); break;
		case 18: DummyFire(); break;
		case 19: if (Shooter != None && Shooter.Pawn != None) Shooter.Pawn.Health = I0; else Status = -1; break;
		case 20: if (Shooter != None && Shooter.Pawn != None) V0 = BonePointOf(Shooter.Pawn, I0); else Status = -1; break;
		case 21: DummyCrouch(); break;
		case 22: Refill(); break;
		case 23: if (Dummy != None) Dummy.GotoWaitingState(); else Status = -1; break;
		default: Status = -2;
	}
}

// I0 team (0 spy, 1 merc); F0..F2 floor spot; I1 yaw. Made as the game makes a player (Login, PostLogin and
// RestartPlayer in SGameInfo), less what needs a connection: name first (the Reloaded patch walks GRI.PRIArray and
// crashes on an empty PlayerName), team, pawn class from the team's PawnList, then RestartPlayer at a team start, and
// the new pawn moved to the spot.
function SpawnTarget()
{
	local SvmRigController C;
	local vector Spot;

	if (Target != None || Dummy != None)
		RemoveTarget();
	Spot.X = F0; Spot.Y = F1; Spot.Z = F2;
	C = Spawn(class'SvmRigController', , , Spot);
	if (C == None)
	{
		Status = -3;
		return;
	}
	Spawns++;
	Dummy = C;
	Level.Game.ChangeName(C, "RigTarget" $ Spawns, false);
	C.GameReplicationInfo = Level.Game.GameReplicationInfo;
	if (!Level.Game.ChangeTeam(C, I0, false))
	{
		Status = -4;
		return;
	}
	if (I0 == 0)
		C.SPawnType = SPAWNOFFENSE;
	else
		C.SPawnType = SPAWNDEFENSE;
	C.SetPawnClass(STeamInfo(C.PlayerReplicationInfo.Team).PawnList[0]);
	// PostLogin gives every player an SHud; the native hit feedback (ClientDamageHudEffect) treats myHUD as one and
	// crashed on the plain HUD a controller makes for itself.
	C.ClientSetHUD(class'SHud', None);
	// A player controller made here is a remote player's to the engine (RemoteRole AutonomousProxy): its pawn would
	// get no physics and play no actions without moves from a client. With RemoteRole None it is run as a host's
	// own player is (physics, actions, animation, root motion, the start stance), less its PlayerTick, which needs a
	// viewport. I3 != 0 keeps it a remote player.
	if (I3 == 0)
		C.RemoteRole = ROLE_None;
	Level.Game.RestartPlayer(C);
	Target = SBasePawn(C.Pawn);
	if (Target == None)
	{
		Status = -5;
		return;
	}
	if (C.StartSpot != None)
		StartSpotLocation = C.StartSpot.Location;
	V2 = Target.Location;
	// I2 != 0: leave the pawn where RestartPlayer put it (spawn protection, spawn shock).
	bPlacePending = false;
	if (I2 == 0)
	{
		PlaceLift = F3;
		PlaceSpot.X = F0; PlaceSpot.Y = F1; PlaceSpot.Z = F2;
		PlaceYaw = I1;
		PlaceCount = 0;
		PlaceAt = Level.TimeSeconds + 0.5;
		bPlacePending = true;
	}
}

// I0 the target controller's RemoteRole: 0 None, 2 SimulatedProxy, 3 AutonomousProxy (as made). A player
// controller whose RemoteRole is AutonomousProxy is a remote player's: its pawn's actions (ChangeEnumAction) and
// moves come from its client, which the rig's target does not have.
function DummyRole()
{
	if (Dummy == None)
	{
		Status = -1;
		return;
	}
	if (I0 == 0)
		Dummy.RemoteRole = ROLE_None;
	else if (I0 == 2)
		Dummy.RemoteRole = ROLE_SimulatedProxy;
	else
		Dummy.RemoteRole = ROLE_AutonomousProxy;
}

// Full rifle (clip and reserve) and full spy energy, for the local player (I0 0) or the target (I0 1).
function Refill()
{
	local SBasePawn P;
	local SWeaponDefense W;

	if (I0 == 1)
		P = Target;
	else if (Shooter != None)
		P = SBasePawn(Shooter.Pawn);
	if (P == None)
	{
		Status = -1;
		return;
	}
	W = SWeaponDefense(P.MyWeapon);
	if (W != None)
	{
		W.ClipAmmo = W.ClipMaxAmmo;
		W.Ammo = W.MaxAmmo;
	}
	if (SPawnAttaque(P) != None)
		SPawnAttaque(P).EnergyLevel = 100;
}

// I0 1 crouched, 0 standing: the target changes stance when its stance differs. I1 0: through ProcessCrouch (the
// crouch button's handler); I1 1: the actions and transition state ProcessCrouch starts (the target has no
// PlayerTick, and the button handler can refuse in a state the target never leaves without one). Spies start
// crouched.
function DummyCrouch()
{
	local bool bLoaded;

	if (Dummy == None || Target == None)
	{
		Status = -1;
		return;
	}
	if (Target.bIsCrouched == (I0 != 0))
		return;
	if (I1 == 0)
	{
		Dummy.bCrouchWithRoll = false;
		Dummy.m_LastSpecialMoveTime = Level.TimeSeconds - 1.0;
		Dummy.ProcessCrouch();
		return;
	}
	bLoaded = Target.WeaponLoaded != 0;
	Dummy.m_LastSpecialMoveTime = Level.TimeSeconds;
	if (I0 == 0)
	{
		Target.ChangeEnumAction(Target.SA_WANTS_TO_UNCROUCH);
		if (bLoaded)
		{
			Target.ChangeEnumAction(Target.SA_WAIT_CR2ST_2H);
			Dummy.ChangeState('s_PlayerWalkingWeapon_T_CrToSt');
		}
		else
		{
			Target.ChangeEnumAction(Target.SA_WAIT_CR2ST_0H);
			if (Target.SPawnType == SPAWNOFFENSE)
				Dummy.ChangeState('s_PlayerWalkingMario_T_CrToSt');
			else
				Dummy.ChangeState('s_PlayerWalkingWeapon_T_CrToSt');
		}
	}
	else
	{
		Target.ChangeEnumAction(Target.SA_WANTS_TO_CROUCH);
		if (bLoaded)
		{
			Target.ChangeEnumAction(Target.SA_WAIT_ST2CR_2H);
			Dummy.ChangeState('s_PlayerWalkingWeapon_T_StToCr');
		}
		else
		{
			Target.ChangeEnumAction(Target.SA_WAIT_ST2CR_0H);
			if (Target.SPawnType == SPAWNOFFENSE)
				Dummy.ChangeState('s_PlayerWalkingMario_T_StToCr');
			else
				Dummy.ChangeState('s_PlayerWalkingWeapon_T_StToCr');
		}
	}
}

// The target fires its weapon at a bone of the local player's pawn (I0, plus the offset F0..F2) as its owner's
// client would ask the server to: a spy's shocker through ServerFireWeaponWithLocation from its eye, a merc's rifle
// through ServerSetAccurateRotation and ServerFireWeapon (I1 the bullet in a row).
function DummyFire()
{
	local vector P, Pos, S, X, Y, Z;
	local rotator R;
	local SWeaponAttaque WA;
	local SWeaponDefense WD;

	if (Target == None || Shooter == None || Shooter.Pawn == None || Target.MyWeapon == None)
	{
		Status = -1;
		return;
	}
	P = BonePointOf(Shooter.Pawn, I0);
	P.X += F0; P.Y += F1; P.Z += F2;
	Pos = Target.Location + Target.EyePosition();
	WA = SWeaponAttaque(Target.MyWeapon);
	WD = SWeaponDefense(Target.MyWeapon);
	if (WA != None)
	{
		// TraceFireWithPos starts 30 forward, 8 up and 4 left of the position: aim from there.
		R = rotator(P - Pos);
		GetAxes(R, X, Y, Z);
		S = Pos + X * 30.0 + Z * 8.0 - Y * 4.0;
		R = rotator(P - S);
		GetAxes(R, X, Y, Z);
		S = Pos + X * 30.0 + Z * 8.0 - Y * 4.0;
		R = rotator(P - S);
		V0 = P;
		V1 = Pos;
		WA.ServerFireWeaponWithLocation(Pos, R.Yaw, R.Pitch, 0);
	}
	else if (WD != None)
	{
		R = rotator(P - Pos);
		V0 = P;
		V1 = Pos;
		WD.ServerSetAccurateRotation(R.Yaw, R.Pitch, 0);
		WD.ServerFireWeapon(Max(I1, 1));
	}
	else
		Status = -3;
	Out0 = R.Pitch;
	Out1 = R.Yaw;
}

// F0..F2 floor spot (the pawn stands CollisionHeight + 1 above it), I1 yaw, F3 the lift it falls from.
function PlaceTarget()
{
	local vector Spot;

	if (Target == None)
	{
		Status = -1;
		return;
	}
	Spot.X = F0; Spot.Y = F1; Spot.Z = F2;
	PlaceLift = F3;
	bPlacePending = false;
	PlaceTargetAt(Spot, I1);
}

function PlaceTargetAt(vector FloorSpot, int Yaw)
{
	local vector Spot;
	local rotator R;

	Spot = FloorSpot;
	Spot.Z += Target.CollisionHeight + 1.0 + PlaceLift;
	Target.SetLocation(Spot);
	// A move is not a fall: the fall height starts here (a target moved while it fell from its start kept that
	// start's height and took fall damage on the lane).
	if (Dummy != None)
		Dummy.fZMaxFalling = Spot.Z;
	bSettle = true;
	SettleUntil = Level.TimeSeconds + 3.0;
	Target.Velocity = vect(0,0,0);
	Target.Acceleration = vect(0,0,0);
	R.Yaw = Yaw;
	Target.SetRotation(R);
	if (Dummy != None)
	{
		Dummy.SetRotation(R);
		Dummy.MyDesiredRotation = R;
		Dummy.LocalMyDesiredRotation = R;
	}
}

// F0..F2 floor spot, I1 yaw: the local player's pawn, view included.
function Teleport()
{
	local vector Spot;
	local rotator R;

	if (Shooter == None || Shooter.Pawn == None)
	{
		Status = -1;
		return;
	}
	Spot.X = F0; Spot.Y = F1; Spot.Z = F2 + Shooter.Pawn.CollisionHeight + 1.0;
	Shooter.Pawn.SetLocation(Spot);
	Shooter.Pawn.Velocity = vect(0,0,0);
	Shooter.fZMaxFalling = Spot.Z;
	R.Yaw = I1;
	Shooter.Pawn.SetRotation(R);
	Shooter.SetRotation(R);
	Shooter.MyDesiredRotation = R;
	Shooter.LocalMyDesiredRotation = R;
}

// Aim the local player's view at a target bone (I0 > 0, or the middle of bones I0 and I1 when I1 > 0, plus the
// offset F0..F2) or at the point F0..F2 (I0 == 0).
// V1 is the eye the aim is taken from, V0 the point aimed at.
function Aim()
{
	local vector Eye, P;
	local rotator R;

	if (Shooter == None || Shooter.Pawn == None)
	{
		Status = -1;
		return;
	}
	Eye = Shooter.Pawn.Location + Shooter.Pawn.EyePosition();
	if (I0 > 0)
	{
		if (Target == None)
		{
			Status = -1;
			return;
		}
		P = BonePoint(I0);
		// I1 > 0: the middle of two bones (a limb capsule runs between them).
		if (I1 > 0)
			P = (P + BonePoint(I1)) * 0.5;
		P.X += F0; P.Y += F1; P.Z += F2;
	}
	else
	{
		P.X = F0; P.Y = F1; P.Z = F2;
	}
	R = rotator(P - Eye);
	R.Roll = 0;
	V0 = P;
	V1 = Eye;
	Shooter.MyDesiredRotation = R;
	Shooter.LocalMyDesiredRotation = R;
	Shooter.SetRotation(R);
	Out0 = R.Pitch;
	Out1 = R.Yaw;
}

// What the Fire key does: Button bFire (held) and the exec Fire once.
function PressFire()
{
	if (Shooter == None)
	{
		Status = -1;
		return;
	}
	Shooter.bFire = 1;
	Shooter.Fire(1.0);
}

function PressAltFire()
{
	if (Shooter == None)
	{
		Status = -1;
		return;
	}
	Shooter.bAltFire = 1;
	Shooter.AltFire(1.0);
}

// I0 kind (1 frag, 2 smoke); F0..F2 where; F3 fuse in seconds (0: the grenade's own TempsExplosion); I1 who
// throws it as far as the grenade's code knows (owner and instigator): 0 the local player, 1 the target.
function SpawnGrenade()
{
	local Pawn Who;
	local SProjGrenade G;
	local vector Spot;

	if (I1 == 1)
		Who = Target;
	else if (Shooter != None)
		Who = Shooter.Pawn;
	if (Who == None)
	{
		Status = -1;
		return;
	}
	Spot.X = F0; Spot.Y = F1; Spot.Z = F2;
	Instigator = Who;
	if (I0 == 2)
		G = Spawn(class'SProjSmoke', Who, , Spot);
	else
		G = Spawn(class'SProjFrag', Who, , Spot);
	if (G == None)
	{
		Status = -3;
		return;
	}
	G.Instigator = Who;
	Grenade = G;
	GrenadeSpawnedAt = Level.TimeSeconds;
	GrenadeGoneAt = 0;
	bGrenadeRigFuse = false;
	GrenadeLastLocation = G.Location;
	// The grenade's own fuse (SetTimer in PostNetBeginPlay) is kept; the rig ends it a frame later if it has not.
	if (F3 > 0)
		GrenadeFuseAt = Level.TimeSeconds + F3;
	else
		GrenadeFuseAt = Level.TimeSeconds + G.TempsExplosion + 0.05;
	V0 = G.Location;
}

// The floor under F0..F2: V0 the hit point (Out0 1) of a world trace down 3000 uu from 100 uu above it.
function Floor()
{
	local vector Start, End, HitLocation, HitNormal;
	local Actor A;

	Start.X = F0; Start.Y = F1; Start.Z = F2 + 100.0;
	End = Start;
	End.Z -= 3000.0;
	A = Trace(HitLocation, HitNormal, End, Start, false);
	Out0 = 0;
	if (A != None)
		Out0 = 1;
	V0 = HitLocation;
	V1 = HitNormal;
}

// I0 the mode (0 single, 1 burst, 2 auto) of the local player's rifle.
function SetROF()
{
	local SWeaponDefense W;

	if (Shooter == None || SBasePawn(Shooter.Pawn) == None)
	{
		Status = -1;
		return;
	}
	W = SWeaponDefense(SBasePawn(Shooter.Pawn).MyWeapon);
	if (W == None)
	{
		Status = -1;
		return;
	}
	if (I0 == 0)
		W.ROFMode = ROF_Single;
	else if (I0 == 1)
		W.ROFMode = ROF_Burst;
	else
		W.ROFMode = ROF_Auto;
	W.ServerSetROF(W.ROFMode);
}

function ClearHistory()
{
	local int i;

	if (Dummy == None)
	{
		Status = -1;
		return;
	}
	for (i = 0; i < 10; i++)
	{
		Dummy.LastDamages[i].DamageType = NoDamageType;
		Dummy.LastDamages[i].Damage = 0;
		Dummy.LastDamages[i].PillTag = 0;
		Dummy.LastDamages[i].Time = 0;
	}
}

// SPlayerController.UnPossess destroys the pawn; a pawn left without its controller is destroyed here.
function RemoveTarget()
{
	if (Dummy != None && Dummy.Pawn != None)
		Dummy.UnPossess();
	if (Target != None && !Target.bDeleteMe)
		Target.Destroy();
	Target = None;
	if (Dummy != None)
	{
		if (Dummy.PlayerReplicationInfo != None && Dummy.PlayerReplicationInfo.Team != None)
			Dummy.PlayerReplicationInfo.Team.RemoveFromTeam(Dummy);
		Dummy.Destroy();
	}
	Dummy = None;
}
