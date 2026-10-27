using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int TriggerPound_Open = 1;
    private const int TriggerPound_Close = 55;

    /// <summary>Opens and closes over and over while triggered (OtherTime open, StayOpenTime closed).</summary>
    [Uc("Engine.Mover.TriggerPound")]
    public class TriggerPound : MoverState
    {
        public static readonly TriggerPound Instance = new(UcMeta.TriggerPound, null);

        protected TriggerPound(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerPound@code", Sha1 = "d0ffd0e687f43e252468193b7327e5985b7158ba")]
        [PortOf("uc:Engine.Mover:TriggerPound@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).TriggerPound_Code(ref frame);

        [Uc("Engine.Mover.TriggerPound.BeginState", Sha1 = "985a57088ba974bfbcbdf3a6e7220cf0bc3d8a83")]
        [PortOf("uc:Engine.Mover:TriggerPound.BeginState", Spec = "spec/devices/mover.md#state-machines")]
        public override void BeginState(Actor self) => ((Mover)self).numTriggerEvents = 0;

        [Uc("Engine.Mover.TriggerPound.Trigger", Sha1 = "0a12078b6328402ae9b494f5a35c6163139d6acd")]
        [PortOf("uc:Engine.Mover:TriggerPound.Trigger", Spec = "spec/devices/mover.md#triggering")]
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.Trigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerPound.UnTrigger", Sha1 = "316cfb6362a569eef6c01263e60a801f8d73918a")]
        [PortOf("uc:Engine.Mover:TriggerPound.UnTrigger", Spec = "spec/devices/mover.md#triggering")]
        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.UnTrigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerPound.SubTrigger", Sha1 = "eaba50e8868e0fc62a90ce3b50992cd1a955d8e5")]
        [PortOf("uc:Engine.Mover:TriggerPound.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents++;
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.GotoState(nameof(TriggerPound), NAME_Open);
            self.bAllowLock = false;
        }

        [Uc("Engine.Mover.TriggerPound.SubUnTrigger", Sha1 = "a39a8d95c7c11666d107ca63accf4ccd3fca7eff")]
        [PortOf("uc:Engine.Mover:TriggerPound.SubUnTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubUnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents--;
            if (self.numTriggerEvents <= 0)
            {
                self.numTriggerEvents = 0;
                self.SavedTrigger = null;
                self.Instigator = null;
                self.GotoState(nameof(TriggerPound), NAME_Close);
                self.bAllowLock = false;
            }
        }
    }

    private void TriggerPound_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case TriggerPound_Open:
                Segment(ref f, TriggerPound_Open);
                bClosed = false;
                if (DelayTime > 0f)
                {
                    bDelaying = true;
                    f.Pc = 2;
                    Sleep(DelayTime);
                    return;
                }

                goto case 2;
            case 2:
                mark = Segment(ref f, 2);
                DoOpen();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 3;
                FinishInterpolation();
                return;
            case 3:
                f.Pc = 4;
                Sleep(OtherTime);
                return;
            case 4:
            case TriggerPound_Close:
                Segment(ref f, TriggerPound_Close);
                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 56;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 56;
            case 56:
                mark = Segment(ref f, 56);
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 57;
                FinishInterpolation();
                return;
            case 57:
                f.Pc = 58;
                Sleep(StayOpenTime);
                return;
            case 58:
                if (bTriggerOnceOnly)
                {
                    GotoState(Name.None);
                    return;
                }

                if (SavedTrigger is not null)
                {
                    goto case TriggerPound_Open;
                }

                f.Pc = LatentFrame.NoCode;
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }
}
