using System.Collections.Generic;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server;
using OpenScctGame.Play.Demo;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Play;

public partial class PlayRoot
{
    private List<DemoPresentationSample>? _demoPresentation;

    private void AdvanceDemoPresentation(DemoPlayer demo, PlaySession session)
    {
        bool atCheckpoint = demo.UsingCheckpoint && session.Client.LocalTick == 1;
        List<DemoPresentationSample> history = _demoPresentation ??= [];
        if (atCheckpoint)
        {
            if (!session.Client.HasDemoCheckpointPresentation)
                throw new InvalidDataException("This checkpoint predates sound and particle restoration.");
            session.Client.TakeDemoCheckpointPresentation(history);
            RestoreDemoParticles(demo, session, history);
        }
        else Fx?.OnDemoTick(session);

        TriggerMapEmitters(session);
        if (!atCheckpoint && session.Client.Map is not null) Map?.Emitters?.AdvanceDemo(Sim.DeltaTime);
        Audio?.AdvanceDemoTick(demo, history, atCheckpoint);
        history.Clear();
    }

    private void RestoreDemoParticles(DemoPlayer demo, PlaySession session, List<DemoPresentationSample> history)
    {
        var live = new Dictionary<int, float>();
        var triggers = new List<(string Id, float Age)>();
        Level level = session.Client.World.Level;
        float Age(DemoPresentationSample sample) =>
            (demo.CurrentTick - sample.Tick) / (float)demo.Header.TickRate;

        foreach (DemoPresentationSample sample in history)
        {
            SimEvent e = sample.Event;
            if (sample.IsLiveEmitter)
                live[e.ActorId] = Age(sample);
            else if (e.Kind == SimEventKind.Notify && e.What == Emitter.NAME_ReplicateTrigger &&
                level.FindActor(e.ActorId) is Emitter emitter && session.Client.Map?.Loaded?.RecordOf(emitter)?.Id is { } id)
                triggers.Add((id, Age(sample)));
        }

        Fx?.OnDemoTick(session, live.Count > 0 ? actor => live.GetValueOrDefault(actor.ActorId) : null);
        Map?.Emitters?.RestoreDemoTriggers(triggers);
        if (Fx is { } fx)
        {
            foreach (DemoPresentationSample sample in history)
                if (sample.Event.Kind == SimEventKind.SpawnEmitter && !sample.IsLiveEmitter)
                    fx.RestoreDemoEmitter(sample.Event.What.ToString(), sample.Event.Location, sample.Event.Rotation, Age(sample));
        }
    }
}
