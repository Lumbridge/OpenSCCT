using System.Numerics;

namespace OpenScct.Animation;

public enum ParameterKind { Float, Integer, Boolean, Trigger }
public sealed class GraphParameter
{
    public string Name { get; set; } = "Speed";
    public ParameterKind Kind { get; set; }
    public float Default { get; set; }
}
public enum GraphNodeKind { Clip, Blend1D, Blend2D, Layer, Additive }
public sealed class GraphNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public GraphNodeKind Kind { get; set; }
    public string? Clip { get; set; }
    public string Parameter { get; set; } = "Speed";
    public string ParameterY { get; set; } = "Direction";
    public float Rate { get; set; } = 1;
    public float Weight { get; set; } = 1;
    public List<GraphInput> Inputs { get; set; } = [];
    public List<string> Mask { get; set; } = [];
    public Vector2 Position { get; set; }
}
public sealed class GraphInput
{
    public string Node { get; set; } = "";
    public Vector2 Point { get; set; }
}
public sealed class GraphState
{
    public string Name { get; set; } = "Idle";
    public string Node { get; set; } = "";
    public Vector2 Position { get; set; }
}
public enum ConditionOperator { Greater, Less, Equal, NotEqual, True, False, Triggered }
public sealed class GraphTransition
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Parameter { get; set; } = "";
    public ConditionOperator Operator { get; set; }
    public float Value { get; set; }
    public float BlendSeconds { get; set; } = .15f;
    public float ExitTime { get; set; }
}
public sealed class AnimationGraph
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Animation graph";
    public string Skeleton { get; set; } = "";
    public string Entry { get; set; } = "Idle";
    public List<GraphParameter> Parameters { get; set; } = [];
    public List<GraphNode> Nodes { get; set; } = [];
    public List<GraphState> States { get; set; } = [];
    public List<GraphTransition> Transitions { get; set; } = [];
}
public enum SequenceTrackKind { Skeletal, Transform, Camera, Audio, Event }
public sealed class SequenceSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public float Start { get; set; }
    public float Duration { get; set; } = 1;
    public float Offset { get; set; }
    public float Rate { get; set; } = 1;
    public string? Clip { get; set; }
    public string? Asset { get; set; }
    public float BlendIn { get; set; }
    public float BlendOut { get; set; }
}
public sealed class SequenceTrack
{
    public string Name { get; set; } = "Track";
    public SequenceTrackKind Kind { get; set; }
    public string Target { get; set; } = "";
    public List<SequenceSegment> Segments { get; set; } = [];
    public List<BoneTrack> TransformTracks { get; set; } = [];
    public List<AnimationKey> FieldOfView { get; set; } = [];
    public List<AnimationEvent> Events { get; set; } = [];
}
public sealed class AnimationSequence
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Sequence";
    public float Duration { get; set; } = 5;
    public bool Loop { get; set; }
    public List<SequenceTrack> Tracks { get; set; } = [];
}
