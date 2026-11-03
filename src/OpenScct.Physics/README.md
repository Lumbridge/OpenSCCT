# OpenScct.Physics

Collision and movement physics for the deterministic simulation, written in C# without
Godot. The implementation follows the [native physics specs](../../spec/native/physics-index.md).

The [physics guide](../../docs/PHYSICS.md) explains the design, pawn API and remaining
estimates. This page is a quick API reference.

```csharp
PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);   // collision, GE index, driver on the level
PhysicsDriver driver = PhysicsDriver.Of(pawn)!;
driver.setPhysics(pawn, EPhysics.PHYS_Falling);
driver.CheckArmsZone(pawn);                                 // raises Ledge, Pipe, ZipLine... on the controller
physics.Collision.LineCheck(out FCheckResult hit, pawn, end, start, new FVector(35f, 35f, 87.5f), K.Flags.Clearance);
```

## Namespaces

- `OpenScct.Physics`: `PhysicsWorld` (one map's physics), `K` (constants with provenance; nested classes per native).
- `OpenScct.Physics.Collision`:
  - `ICollisionWorld` / `CollisionWorld`: `LineCheck`, `MultiLineCheck`, `PointCheck`, `FastTrace`,
    `GetZoneIndex`, `GetZoneInfo`; colliders (`GetCollider`, `SetColliderEnabled`, `SetActorCollidersEnabled`,
    `SetColliderTransform`), `BindLevel`, `IsOverlapping`, `GetCylinder`, `GetCylinderFlags`, `KillZ`.
  - `FCheckResult`, `ETraceFlags` (the decoded bit layout plus the `TRACE_Bullet` channel).
  - `CollisionDataBuilder`: collision data by hand for tests, tools and native maps.
- `OpenScct.Physics.Movement`:
  - `PhysicsDriver` (`IActorPhysics`): dispatch (`performPhysics`, `startNewPhysics`, `setPhysics`), every pawn
    mode (`physWalking`, `physFalling`, `physRootMotion`, `physRepositionning`, `physVandamme`, `physOnPipe`,
    `physOnPipeH`, `physOnLadder`, `physLinear`, `physOnVerticalPlane`, `physLedge`), the movement natives
    (`MyDoJump`, `MycalcVelocity`, `SetZipSpeed`, `UnZipSpeed`, `EndRootMotion`, `bServerTestRootMotion`...), the
    GE natives (`CheckArmsZone`, `CheckFeetZone`, `CheckBackFeetZone`, `FindArmsGE`, `TestTopLedge`,
    `bTestTopLadder`, `TestTopPipe`, `CheckFence`, `CheckBarrier`, `CheckWallJump`, `CheckGEForWallJump`) and the
    engine moves (`MoveActor`, `FarMoveActor`, `CheckEncroachment`, `FindSpot`, `SetCollision`...).
  - `ISBasePawnPhysics`, `ISPlayerControllerPhysics`, `IRootMotionMesh` (implemented by the SBase port),
    `IPhysicsEvents`, `IPawnPhysicsStateOwner` and `PawnPhysicsState`, `IMovingBrush`, `IEncroacher`,
    `IEncroachable`, `IPhysicsModeHandler`.
- `OpenScct.Physics.Traversal`: `GEIndex`, `GEHit`, `GEZone`, `GEZones`, `GEQueries` (zone search by kind, ledge
  clearance, hang spot).
