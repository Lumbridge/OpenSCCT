using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SExtincteur: a wall extinguisher that sprays a cloud for 10 s when shot, makes a HUD noise for every
/// player and re-arms (spec/devices/extincteur.md). It never breaks.
/// </summary>
public partial class SExtincteur
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_Used.Instance, s_Waiting.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name NAME_Kill = new("Kill");

    /// <summary>Stops any cloud and re-arms.</summary>
    [Uc("SBase.SExtincteur.Reset", Sha1 = "ac569379df65d47cbf247b74446d84640b765abb")]
    [PortOf("uc:SBase.SExtincteur:Reset", Spec = "spec/devices/extincteur.md#rules")]
    public override void Reset()
    {
        KillEmitter();
        GotoState(nameof(s_Waiting));
    }

    /// <summary>
    /// Emitter.Kill on the cloud: it stops emitting and the puffs already out live their lifetime, then the cloud
    /// actor goes. Views get a Kill notify on the emitter.
    /// </summary>
    [UcNative("AEmitter::execKill")]
    [PortOf("nat:Engine.Emitter.Kill", Spec = "spec/devices/extincteur.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Emitter.Kill replication and the end of the cloud on clients are extincteur.md open question 2")]
    private void KillEmitter()
    {
        if (Emitter is not { bDeleteMe: false } cloud)
        {
            return;
        }

        XLevel.World.Events.Notify(cloud, NAME_Kill);
        if (cloud.LifeSpan == 0f || cloud.LifeSpan > DeviceK.ExtincteurPuffLife)
        {
            cloud.LifeSpan = DeviceK.ExtincteurPuffLife;
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Emitter = Live(Emitter);
    }
}
