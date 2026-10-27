using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGPOControl: a named control zone (ZoneDescription, a cylinder of CylinderWidth by CylinderHeight) that
/// gameplay objects name as their ERControl. A chaff or sticky lock on such an object passes the lock length on
/// (LockChaff): the zone is "on" (state s_On) for that long and then returns to the state it had at start. The
/// description and cylinder replicate once; the zone itself is hidden and always relevant. See
/// spec/devices/gpo-control.md.
/// </summary>
public partial class SGPOControl
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_On.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>True while a lock relayed from one of its objects runs (state s_On).</summary>
    public bool IsLocked => GetStateName() == UcMeta.s_On.Name;

    /// <summary>Remembers the start state (the state LockChaff returns to).</summary>
    [Uc("SBase.SGPOControl.PostNetBeginPlay", Sha1 = "fa928ec4be3b7947e216fda8518676bdcaa16fc1")]
    [PortOf("uc:SBase.SGPOControl:PostNetBeginPlay", Spec = "spec/devices/gpo-control.md#rules")]
    protected override void PostNetBeginPlay() => PreviousState = GetStateName();

    /// <summary>A lock of <paramref name="Length"/> seconds on one of its objects: (re)starts the timer and enters s_On.</summary>
    [Uc("SBase.SGPOControl.LockChaff", Sha1 = "52585e52d7e624e7db68a77db1f0c0c02af22fb9")]
    [PortOf("uc:SBase.SGPOControl:LockChaff", Spec = "spec/devices/gpo-control.md#rules")]
    public void LockChaff(float Length)
    {
        SetTimer(Length, false);
        GotoState(UcMeta.s_On.Name);
    }

    /// <summary>Locked: no tick (the native tick is skipped), the timer returns to the start state.</summary>
    [Uc("SBase.SGPOControl.s_On")]
    public class s_On : UcActorState
    {
        public static readonly s_On Instance = new(UcMeta.s_On, null);

        protected s_On(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGPOControl.s_On.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGPOControl:s_On.BeginState", Spec = "spec/devices/gpo-control.md#rules")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SGPOControl.s_On.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGPOControl:s_On.EndState", Spec = "spec/devices/gpo-control.md#rules")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SGPOControl.s_On.Tick", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGPOControl:s_On.Tick", Spec = "spec/devices/gpo-control.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
        }

        [Uc("SBase.SGPOControl.s_On.Timer", Sha1 = "fd36ca15b752461107013123a430bca74c49eb55")]
        [PortOf("uc:SBase.SGPOControl:s_On.Timer", Spec = "spec/devices/gpo-control.md#rules")]
        public override void Timer(Actor self) => self.GotoState(((SGPOControl)self).PreviousState);
    }
}
