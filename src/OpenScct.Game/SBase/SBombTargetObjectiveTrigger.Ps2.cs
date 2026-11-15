using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;

namespace OpenScct.Game.SBase;

public partial class SBombTargetObjectiveTrigger
{
    public AssetRef Ps2ModemMesh;
    public AssetRef Ps2ModemSound;
    public AssetRef Ps2ModemDestroyedSound;
    public ClassRef Ps2ModemDestroyedEmitter;
    public AssetRef Ps2ModemSmokeTexture;
    public AssetRef Ps2ModemFireTexture;
    private int _ps2InitialCaptureTime;
    private float _ps2CaptureStarted;
    public float Ps2TargetRadius;
    public float Ps2TargetHeight;

    // Source CheckLocalization (COM 0x18AFF7): strict horizontal cylinder and
    // independent absolute height, using the sizes saved before collision off.
    public bool Ps2Contains(FVector at)
    {
        FVector d = at - Location;
        return d.X * d.X + d.Y * d.Y < Ps2TargetRadius * Ps2TargetRadius && MathF.Abs(d.Z) < Ps2TargetHeight;
    }

    protected override void PreBeginPlay()
    {
        if (!bPS2Objective) { base.PreBeginPlay(); return; }
        _ps2InitialCaptureTime = CaptureTime + ((Level.Game as SGameInfo)?.MatchInfo?.Handicap ?? 0);
        CaptureTime = _ps2InitialCaptureTime;
        Ps2TargetRadius = CollisionRadius;
        Ps2TargetHeight = CollisionHeight;
        InteractionClass = ClassRef.None;
        StaticMesh = AssetRef.None;
        Skins.Clear();
        bHidden = true;
        // This hidden cylinder is still required by the owner's local gadget
        // placement check. The PC relevancy test otherwise excludes it before
        // the replacement's authored profile can reach that client.
        bAlwaysRelevant = true;
        bDamageable = false;
        bCanDamageActor = false;
        SetCollision(false, false, false);
    }

    private void Ps2ResetTarget()
    {
        CaptureTime = _ps2InitialCaptureTime;
        _ps2CaptureStarted = 0f;
        if (Ps2Computer is null)
            foreach (SComputerObjectiveTrigger console in XLevel.AllActors<SComputerObjectiveTrigger>())
                if (console.bPS2Objective && Ps2Contains(console.Location)) { Ps2Computer = console; break; }
        Ps2Computer?.Ps2UseAsProxy();
        Ps2Computer?.Ps2SetVisualState(3, 4f);
        Ps2Computer?.Ps2SetVisualState(8, 4f);
    }

    private void Ps2TriggerTarget(bool status)
    {
        base.Triggering(status);
        if (bTriggering) _ps2CaptureStarted = Level.TimeSeconds;
        else if (_ps2CaptureStarted > 0f)
        {
            CaptureTime = Math.Max(1, CaptureTime - SvmMath.Trunc(Level.TimeSeconds - _ps2CaptureStarted));
            _ps2CaptureStarted = 0f;
        }
    }
}
