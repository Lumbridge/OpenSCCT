using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFish: an aquarium fish swimming around its placed location (spec/devices/fish.md). It swims straight for a
/// random time, then turns for a random time; when it leaves its box (fEloignementMax X and Y, 0.7 of them) it turns
/// back towards the centre (s_outbocal), and its pitch drifts up or down within the box's height. Every function is
/// simulated and the class is bNoDelete, so every machine swims its own copy of each fish (cosmetic; its random
/// numbers are the world's).
/// </summary>
public partial class SFish
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_inbocal.Instance, s_outbocal.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The sequence the fish loops on its mesh (a skeletal fish), or None (the native channel, for views).</summary>
    public Name LoopedAnim { get; private set; }

    [Uc("SBase.SFish.PostBeginPlay", Sha1 = "c5ce6e2731d67563349f740d5165fa27a75694f9")]
    [PortOf("uc:SBase.SFish:PostBeginPlay", Spec = "spec/devices/fish.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        SetPhysics(EPhysics.PHYS_MovingBrush);
        posinit = Location;
        if (!bPilote)
        {
            InitRandomPos();
        }

        fAngleVCurrent = 0f;
        bTourne = false;
        InitSpeed();
        fCurrentSpeed = fWantedSpeed;
    }

    /// <summary>Starts somewhere in 0.7 of the box around the placed location, facing a random way.</summary>
    [Uc("SBase.SFish.InitRandomPos", Sha1 = "619119ac4cfc18cb9851a075367ce514b51d090f")]
    [PortOf("uc:SBase.SFish:InitRandomPos", Spec = "spec/devices/fish.md#rules")]
    internal void InitRandomPos()
    {
        FVector pos;
        pos.X = posinit.X + (FRand() * fEloignementMaxX * 1.4f) - (fEloignementMaxX * 0.7f);
        pos.Y = posinit.Y + (FRand() * fEloignementMaxY * 1.4f) - (fEloignementMaxY * 0.7f);
        pos.Z = posinit.Z + (FRand() * fEloignementMaxZ * 1.4f) - (fEloignementMaxZ * 0.7f);
        SetLocation(pos);
        FRotator rot = default;
        rot.Yaw = SvmMath.Trunc((FRand() * 65536f) - 32768f);
        fAngleVNext = (FRand() * (fAngleVmax - fAngleVmin)) + fAngleVmin;
        rot.Pitch = SvmMath.Trunc(fAngleVNext);
        rot.Roll = 0;
        Rotation = rot;
    }

    /// <summary>Steers the pitch back inside the box's height; true when the fish left 0.7 of the box in X or Y.</summary>
    [Uc("SBase.SFish.TestOut", Sha1 = "bdadbb11f96b378c41c86877270d8e4560f2eed3")]
    [PortOf("uc:SBase.SFish:TestOut", Spec = "spec/devices/fish.md#rules")]
    internal bool TestOut()
    {
        FVector pos = Location;
        float fDist = pos.Z - posinit.Z;
        if (fDist > fEloignementMaxZ)
        {
            fAngleVNext = FRand() * fAngleVmin;
        }
        else if (fDist < -fEloignementMaxZ)
        {
            fAngleVNext = FRand() * fAngleVmax;
        }

        fDist = pos.X - posinit.X;
        if (fDist > fEloignementMaxX * 0.7f || fDist < -fEloignementMaxX * 0.7f)
        {
            return true;
        }

        fDist = pos.Y - posinit.Y;
        return fDist > fEloignementMaxY * 0.7f || fDist < -fEloignementMaxY * 0.7f;
    }

    /// <summary>Turns by the current yaw rate (wrapped to -32768..32767) and eases the pitch towards fAngleVNext.</summary>
    [Uc("SBase.SFish.RotateFish", Sha1 = "91f61546301f88cdc680f49ed69c0732057f7f71")]
    [PortOf("uc:SBase.SFish:RotateFish", Spec = "spec/devices/fish.md#rules")]
    internal void RotateFish(float DeltaTime)
    {
        FRotator rot = Rotation;
        float fRotH = fCurrentRotH * DeltaTime;
        rot.Yaw += SvmMath.Trunc(fRotH);
        if (rot.Yaw > 32767)
        {
            rot.Yaw -= 65536;
        }
        else if (rot.Yaw < -32768)
        {
            rot.Yaw += 65536;
        }

        fAngleVCurrent = ((fAngleVCurrent * 40f) + fAngleVNext) / 41f;
        rot.Pitch = SvmMath.Trunc(fAngleVCurrent);
        Rotation = rot;
    }

    /// <summary>Swims fCurrentSpeed along its facing for the tick.</summary>
    [Uc("SBase.SFish.Forward", Sha1 = "e46889795de9b2a6d4d3daf903cabf5afb8c2cec")]
    [PortOf("uc:SBase.SFish:Forward", Spec = "spec/devices/fish.md#rules")]
    internal void Forward(float DeltaTime)
    {
        FVector pos = Location;
        FVector move = Rotation.Vector() * fCurrentSpeed * DeltaTime;
        pos += move;
        SetLocation(pos);
    }

    /// <summary>
    /// Picks a new speed and how long the phase lasts; a turn also picks a yaw rate (its side from the clock's
    /// hundredths) and a pitch (its sign from the clock's thousandths), and loops AnimsFish[1], a straight swim AnimsFish[0].
    /// </summary>
    [Uc("SBase.SFish.InitSpeed", Sha1 = "342820a1f535d839a70b0b107e5a8f2ebc0ad446")]
    [PortOf("uc:SBase.SFish:InitSpeed", Spec = "spec/devices/fish.md#rules")]
    internal void InitSpeed()
    {
        fWantedSpeed = fSpeedMin + (FRand() * (fSpeedMax - fSpeedMin));
        if (bTourne)
        {
            if (!AnimsFish[1].IsNone)
            {
                LoopAnim(AnimsFish[1]);
            }

            fDateChange = Level.TimeSeconds + (FRand() * (fTempsTourneMax - fTempsTourneMin)) + fTempsTourneMin;
            fWantedRotH = fRotHmin + (FRand() * (fRotHmax - fRotHmin));
            if ((SvmMath.Trunc(Level.TimeSeconds * 100f) & 1) != 0)
            {
                fWantedRotH = -fWantedRotH;
            }

            fAngleVNext = (FRand() * (fAngleVmax - fAngleVmin)) + fAngleVmin;
            if ((SvmMath.Trunc(Level.TimeSeconds * 333.333f) & 1) != 0)
            {
                fAngleVNext = -fAngleVNext;
            }
        }
        else
        {
            if (!AnimsFish[0].IsNone)
            {
                LoopAnim(AnimsFish[0]);
            }

            fDateChange = Level.TimeSeconds + (FRand() * (fTempsAvanceMax - fTempsAvanceMin)) + fTempsAvanceMin;
        }
    }

    /// <summary>Outside the box: turns towards the placed location, harder and slower when it faces away.</summary>
    [Uc("SBase.SFish.BackToIn", Sha1 = "4f8ba2236a2b87ec22f998879a78633b8489b83d")]
    [PortOf("uc:SBase.SFish:BackToIn", Spec = "spec/devices/fish.md#rules")]
    internal void BackToIn(float DeltaTime)
    {
        RotateFish(DeltaTime);
        FRotator dir = (posinit - Location).Rotation();
        float fDiff = dir.Yaw - Rotation.Yaw;
        if (fDiff > 32767f)
        {
            fDiff -= 65536f;
        }
        else if (fDiff < -32768f)
        {
            fDiff += 65536f;
        }

        bool bDanslaxe = SvmMath.Abs(fDiff) < 16384f;
        fCurrentSpeed = bDanslaxe
            ? ((fCurrentSpeed * 10f) + fSpeedMax) / 11f
            : ((fCurrentSpeed * 10f) + (fSpeedMax * 0.5f)) / 11f;
        if (fDiff > 0f)
        {
            fWantedRotH = bDanslaxe ? fRotHmax * 0.5f : fRotHmax;
        }
        else
        {
            fWantedRotH = bDanslaxe ? -fRotHmax * 0.5f : -fRotHmax;
        }
    }

    /// <summary>
    /// True when the fish is back inside 0.7 of the box in X and Y (it then stops turning); outside, a fish past the
    /// box's edge is put back on it. Steers the pitch as <see cref="TestOut"/>.
    /// </summary>
    [Uc("SBase.SFish.TestIn", Sha1 = "900615639689ce0fc64a262418d2d32a45237e0e")]
    [PortOf("uc:SBase.SFish:TestIn", Spec = "spec/devices/fish.md#rules")]
    internal bool TestIn()
    {
        bool bIn = true;
        FVector pos = Location;
        float fDist = pos.Z - posinit.Z;
        if (fDist > fEloignementMaxZ)
        {
            fAngleVNext = FRand() * fAngleVmin;
        }
        else if (fDist < -fEloignementMaxZ)
        {
            fAngleVNext = FRand() * fAngleVmax;
        }

        fDist = pos.X - posinit.X;
        if (fDist > fEloignementMaxX * 0.7f)
        {
            bIn = false;
            if (fDist > fEloignementMaxX)
            {
                pos.X = posinit.X + fEloignementMaxX;
            }
        }
        else if (fDist < -fEloignementMaxX * 0.7f)
        {
            bIn = false;
            if (fDist < -fEloignementMaxX)
            {
                pos.X = posinit.X - fEloignementMaxX;
            }
        }

        fDist = pos.Y - posinit.Y;
        if (fDist > fEloignementMaxY * 0.7f)
        {
            bIn = false;
            if (fDist > fEloignementMaxY)
            {
                pos.Y = posinit.Y + fEloignementMaxY;
            }
        }
        else if (fDist < -fEloignementMaxY * 0.7f)
        {
            bIn = false;
            if (fDist < -fEloignementMaxY)
            {
                pos.Y = posinit.Y - fEloignementMaxY;
            }
        }

        if (bIn)
        {
            fWantedRotH = 0f;
        }
        else
        {
            SetLocation(pos);
        }

        return bIn;
    }

    /// <summary>Native LoopAnim on channel 0 at rate 1: the views loop the sequence on the fish's skeletal mesh.</summary>
    [UcNative("AActor::execLoopAnim")]
    [PortOf("nat:Engine.Actor.LoopAnim", Spec = "spec/devices/fish.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Only the looped sequence is kept (no notifies, tween or rate): the fish meshes are cosmetic")]
    internal void LoopAnim(Name Sequence) => LoopedAnim = Sequence;

    /// <summary>Inside the box: swims straight or turns by phases; a straight swim that leaves the box goes to s_outbocal.</summary>
    [Uc("SBase.SFish.s_inbocal")]
    public class s_inbocal : UcActorState
    {
        public static readonly s_inbocal Instance = new(UcMeta.s_inbocal, null);

        protected s_inbocal(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SFish.s_inbocal.Tick", Sha1 = "fbdf602a7de6a3b1cf0b45ef268b19a2c45d8c0b")]
        [PortOf("uc:SBase.SFish:s_inbocal.Tick", Spec = "spec/devices/fish.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var fish = (SFish)self;
            fish.fCurrentRotH = fish.fWantedRotH;
            if (fish.Level.TimeSeconds > fish.fDateChange)
            {
                fish.bTourne = !fish.bTourne;
                fish.InitSpeed();
                if (fish.fWantedSpeed > fish.fCurrentSpeed)
                {
                    fish.fCurrentSpeed = fish.fWantedSpeed;
                }
            }

            if (fish.bTourne)
            {
                fish.RotateFish(DeltaTime);
                fish.fCurrentSpeed = ((fish.fCurrentSpeed * 10f) + fish.fSpeedMin) / 11f;
                fish.Forward(DeltaTime);
            }
            else
            {
                fish.Forward(DeltaTime);
                if (fish.TestOut())
                {
                    fish.GotoState(nameof(s_outbocal));
                }
                else
                {
                    fish.fCurrentSpeed = ((fish.fCurrentSpeed * 10f) + fish.fSpeedMin) / 11f;
                }
            }
        }
    }

    /// <summary>Outside the box: turns back towards the placed location until inside again, then a straight swim.</summary>
    [Uc("SBase.SFish.s_outbocal")]
    public class s_outbocal : UcActorState
    {
        public static readonly s_outbocal Instance = new(UcMeta.s_outbocal, null);

        protected s_outbocal(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SFish.s_outbocal.Tick", Sha1 = "6e0aa415d920d7a6bfda504e2f5cbe39bd9b9a1e")]
        [PortOf("uc:SBase.SFish:s_outbocal.Tick", Spec = "spec/devices/fish.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var fish = (SFish)self;
            fish.fCurrentRotH = ((fish.fCurrentRotH * 8f) + fish.fWantedRotH) / 9f;
            if (fish.TestIn())
            {
                fish.bTourne = false;
                fish.InitSpeed();
                fish.fWantedRotH = 0f;
                fish.fDateChange = fish.Level.TimeSeconds + (fish.FRand() * (fish.fTempsTourneMax - fish.fTempsTourneMin)) + fish.fTempsTourneMin;
                fish.GotoState(nameof(s_inbocal));
            }
            else
            {
                fish.BackToIn(DeltaTime);
                fish.Forward(DeltaTime);
            }
        }
    }
}
