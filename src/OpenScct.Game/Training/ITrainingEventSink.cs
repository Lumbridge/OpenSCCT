using OpenScct.Core.Engine;

namespace OpenScct.Game.Training;

/// <summary>Optional recording of authoritative outcomes. These labels are never policy observations.</summary>
public interface ITrainingEventSink
{
    void RecordEvent(string kind, Controller? actor = null, Controller? target = null,
        int? amount = null, string? detail = null, int? objectId = null);
}
