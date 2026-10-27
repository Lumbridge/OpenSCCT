using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFish_Big: a big fish (sharks, mantas, dolphins) that swims in smooth curves: inside its box it always turns
/// by an eased yaw rate (0 while swimming straight) and eases its speed towards the wanted one (spec/devices/fish.md).
/// </summary>
public partial class SFish_Big
{
    public static new readonly StateTable ClassStates = new(SFish.ClassStates, s_inbocal.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Inside the box: eased turns and speed; leaving the box goes to s_outbocal.</summary>
    [Uc("SBase.SFish_Big.s_inbocal")]
    public new class s_inbocal : SFish.s_inbocal
    {
        public static new readonly s_inbocal Instance = new(UcMeta.s_inbocal, SFish.s_inbocal.Instance);

        protected s_inbocal(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SFish_Big.s_inbocal.Tick", Sha1 = "17a4519e65e8edb125a94b9665a88d658bb9471f")]
        [PortOf("uc:SBase.SFish_Big:s_inbocal.Tick", Spec = "spec/devices/fish.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var fish = (SFish)self;
            fish.fCurrentRotH = ((fish.fCurrentRotH * 8f) + fish.fWantedRotH) / 9f;
            if (fish.Level.TimeSeconds > fish.fDateChange)
            {
                fish.bTourne = !fish.bTourne;
                fish.InitSpeed();
                if (!fish.bTourne)
                {
                    fish.fWantedRotH = 0f;
                }
            }

            fish.RotateFish(DeltaTime);
            fish.Forward(DeltaTime);
            if (fish.TestOut())
            {
                fish.GotoState(nameof(SFish.s_outbocal));
            }
            else
            {
                fish.fCurrentSpeed = ((fish.fCurrentSpeed * 10f) + fish.fWantedSpeed) / 11f;
            }
        }
    }
}
