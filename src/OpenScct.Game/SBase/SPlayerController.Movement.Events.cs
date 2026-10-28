using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController GE events: the zone and traversal natives raise them, the movement states handle them.
// Every global version is empty (nothing happens in a state without a handler). Written from
// spec/movement/native-contracts.md (events the natives raise).
public partial class SPlayerController
{
    public interface ILedge
    {
        void Ledge(SPlayerController self);
    }

    public interface IWaistLedge
    {
        void WaistLedge(SPlayerController self, bool bCanGoNoCrouch);
    }

    public interface IFeetLedge
    {
        void FeetLedge(SPlayerController self);
    }

    public interface IAutoFeetLedge
    {
        void AutoFeetLedge(SPlayerController self);
    }

    public interface IPipe
    {
        void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd);
    }

    public interface ITopPipe
    {
        void TopPipe(SPlayerController self, bool bLeft);
    }

    public interface ITopPipeCrouch
    {
        void TopPipeCrouch(SPlayerController self, bool bLeft);
    }

    public interface INarrowLadder
    {
        void NarrowLadder(SPlayerController self);
    }

    public interface ITopLadder
    {
        void TopLadder(SPlayerController self);
    }

    public interface IHandOverHand
    {
        void HandOverHand(SPlayerController self);
    }

    public interface IOrthoPipeH
    {
        void OrthoPipeH(SPlayerController self, bool bToLeft);
    }

    public interface IZipLine
    {
        void ZipLine(SPlayerController self, FVector locationEnd, FRotator orientationEnd);
    }

    public interface IFenceTop
    {
        void FenceTop(SPlayerController self, FVector locationEnd, FRotator orientationEnd);
    }

    public interface INoGE
    {
        void NoGE(SPlayerController self);
    }

    public interface IChangeLedge
    {
        void ChangeLedge(SPlayerController self, int iType, FVector P1, FVector P2);
    }

    public void eventLedge()
    {
        if (StateNode is ILedge s)
        {
            s.Ledge(this);
        }
    }

    public void eventWaistLedge(bool bCanGoNoCrouch)
    {
        if (ReloadedWaistLedgeBlocked())
        {
            return;
        }

        if (StateNode is IWaistLedge s)
        {
            s.WaistLedge(this, bCanGoNoCrouch);
        }
    }

    public void eventFeetLedge()
    {
        if (StateNode is IFeetLedge s)
        {
            s.FeetLedge(this);
        }
    }

    public void eventAutoFeetLedge()
    {
        if (StateNode is IAutoFeetLedge s)
        {
            s.AutoFeetLedge(this);
        }
    }

    public void eventPipe(FVector locationEnd, FRotator orientationEnd)
    {
        if (StateNode is IPipe s)
        {
            s.Pipe(this, locationEnd, orientationEnd);
        }
    }

    public void eventTopPipe(bool bLeft)
    {
        if (StateNode is ITopPipe s)
        {
            s.TopPipe(this, bLeft);
        }
    }

    public void eventTopPipeCrouch(bool bLeft)
    {
        if (StateNode is ITopPipeCrouch s)
        {
            s.TopPipeCrouch(this, bLeft);
        }
    }

    public void eventNarrowLadder()
    {
        if (StateNode is INarrowLadder s)
        {
            s.NarrowLadder(this);
        }
    }

    public void eventTopLadder()
    {
        if (StateNode is ITopLadder s)
        {
            s.TopLadder(this);
        }
    }

    public void eventHandOverHand()
    {
        if (StateNode is IHandOverHand s)
        {
            s.HandOverHand(this);
        }
    }

    public void eventOrthoPipeH(bool bToLeft)
    {
        if (StateNode is IOrthoPipeH s)
        {
            s.OrthoPipeH(this, bToLeft);
        }
    }

    public void eventZipLine(FVector locationEnd, FRotator orientationEnd)
    {
        if (StateNode is IZipLine s)
        {
            s.ZipLine(this, locationEnd, orientationEnd);
        }
    }

    public void eventFenceTop(FVector locationEnd, FRotator orientationEnd)
    {
        if (StateNode is IFenceTop s)
        {
            s.FenceTop(this, locationEnd, orientationEnd);
        }
    }

    public void eventNoGE()
    {
        if (StateNode is INoGE s)
        {
            s.NoGE(this);
        }
    }

    public void eventChangeLedge(int iType, FVector P1, FVector P2)
    {
        if (StateNode is IChangeLedge s)
        {
            s.ChangeLedge(this, iType, P1, P2);
        }
    }

    [Uc("SBase.SPlayerController.Ledge", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:Ledge", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void Ledge()
    {
    }

    [Uc("SBase.SPlayerController.WaistLedge", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:WaistLedge", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void WaistLedge(bool bCanGoNoCrouch)
    {
    }

    [Uc("SBase.SPlayerController.FeetLedge", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:FeetLedge", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void FeetLedge()
    {
    }

    [Uc("SBase.SPlayerController.AutoFeetLedge", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:AutoFeetLedge", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void AutoFeetLedge()
    {
    }

    [Uc("SBase.SPlayerController.Pipe", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:Pipe", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void Pipe(FVector locationEnd, FRotator orientationEnd)
    {
    }

    [Uc("SBase.SPlayerController.TopPipe", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:TopPipe", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void TopPipe(bool bLeft)
    {
    }

    [Uc("SBase.SPlayerController.TopPipeCrouch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:TopPipeCrouch", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void TopPipeCrouch(bool bLeft)
    {
    }

    [Uc("SBase.SPlayerController.NarrowLadder", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:NarrowLadder", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void NarrowLadder()
    {
    }

    [Uc("SBase.SPlayerController.TopLadder", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:TopLadder", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void TopLadder()
    {
    }

    [Uc("SBase.SPlayerController.HandOverHand", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:HandOverHand", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void HandOverHand()
    {
    }

    [Uc("SBase.SPlayerController.OrthoPipeH", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:OrthoPipeH", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void OrthoPipeH(bool bToLeft)
    {
    }

    [Uc("SBase.SPlayerController.ZipLine", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ZipLine", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void ZipLine(FVector locationEnd, FRotator orientationEnd)
    {
    }

    [Uc("SBase.SPlayerController.FenceTop", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:FenceTop", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void FenceTop(FVector locationEnd, FRotator orientationEnd)
    {
    }

    [Uc("SBase.SPlayerController.NoGE", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:NoGE", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void NoGE()
    {
    }

    [Uc("SBase.SPlayerController.ChangeLedge", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ChangeLedge", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void ChangeLedge(int iType, FVector P1, FVector P2)
    {
    }

    [Uc("SBase.SPlayerController.TopSynchro", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:TopSynchro", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public virtual void TopSynchro()
    {
    }
}
