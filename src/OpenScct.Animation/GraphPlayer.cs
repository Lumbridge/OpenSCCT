namespace OpenScct.Animation;

public sealed record GraphPlaybackState(string State, float Time, string? Previous, float PreviousTime, float BlendTime, float BlendDuration, Dictionary<string, float> Parameters);

/// <summary>Tick-driven graph state, suitable for server snapshots and editor inspection.</summary>
public sealed class GraphPlayer
{
    private readonly AnimationProject _project;
    private readonly AnimationGraph _graph;
    private readonly SkeletonDefinition _skeleton;
    private readonly Dictionary<string, float> _parameters = new(StringComparer.Ordinal);
    private string _state;
    private string? _previous;
    private float _time, _previousTime, _blendTime, _blendDuration;
    private bool _startPending = true;

    public GraphPlayer(AnimationProject project, AnimationGraph graph)
    {
        _project = project; _graph = graph;
        _skeleton = project.Skeletons.Single(s => s.Id == graph.Skeleton);
        _state = graph.Entry;
        foreach (GraphParameter p in graph.Parameters) _parameters.Add(p.Name, p.Default);
    }
    public string State => _state;
    public float Time => _time;
    public void Set(string name, float value)
    {
        GraphParameter p = _graph.Parameters.FirstOrDefault(p => p.Name == name) ?? throw new ArgumentException("Unknown graph parameter: " + name);
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _parameters[name] = p.Kind switch { ParameterKind.Integer => MathF.Truncate(value), ParameterKind.Boolean or ParameterKind.Trigger => value != 0 ? 1 : 0, _ => value };
    }
    public GraphPlaybackState Capture() => new(_state, _time, _previous, _previousTime, _blendTime, _blendDuration, new(_parameters, StringComparer.Ordinal));
    public void Restore(GraphPlaybackState state)
    {
        if (!_graph.States.Any(s => s.Name == state.State) || (state.Previous is not null && !_graph.States.Any(s => s.Name == state.Previous)))
            throw new InvalidDataException("Graph snapshot references an unknown state.");
        if (!float.IsFinite(state.Time) || state.Time < 0 || !float.IsFinite(state.PreviousTime) || state.PreviousTime < 0 ||
            !float.IsFinite(state.BlendTime) || state.BlendTime < 0 || !float.IsFinite(state.BlendDuration) || state.BlendDuration < 0)
            throw new InvalidDataException("Invalid graph playback time.");
        foreach (GraphParameter p in _graph.Parameters) Set(p.Name, state.Parameters.GetValueOrDefault(p.Name, p.Default));
        _state = state.State; _time = state.Time; _previous = state.Previous;
        _previousTime = state.PreviousTime; _blendTime = state.BlendTime; _blendDuration = state.BlendDuration;
        _startPending = false;
    }
    public IReadOnlyList<AnimationEvent> Advance(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var events = new List<AnimationEvent>();
        var sources = new HashSet<(string Clip, float Rate)>();
        Collect(_graph.States.Single(s => s.Name == _state).Node, sources);
        foreach (var source in sources.OrderBy(s => s.Clip, StringComparer.Ordinal).ThenBy(s => s.Rate))
            events.AddRange(ClipEvaluator.Events(_project.Clips.Single(c => c.Id == source.Clip), _time * source.Rate, (_time + delta) * source.Rate, _startPending));
        _startPending = false;
        _time += delta; _previousTime += delta; _blendTime += delta;
        if (_blendTime >= _blendDuration) _previous = null;
        foreach (GraphTransition t in _graph.Transitions)
        {
            if (t.From != _state || _time < t.ExitTime || !Matches(t)) continue;
            _previous = t.BlendSeconds > 0 ? _state : null; _previousTime = _time;
            _state = t.To; _time = 0; _blendTime = 0; _blendDuration = t.BlendSeconds;
            if (t.Operator == ConditionOperator.Triggered) _parameters[t.Parameter] = 0;
            sources.Clear(); Collect(_graph.States.Single(s => s.Name == _state).Node, sources);
            foreach (var source in sources.OrderBy(s => s.Clip, StringComparer.Ordinal).ThenBy(s => s.Rate))
                events.AddRange(ClipEvaluator.Events(_project.Clips.Single(c => c.Id == source.Clip), 0, 0, true));
            break;
        }
        return events;
    }
    private void Collect(string id, HashSet<(string Clip, float Rate)> clips)
    {
        GraphNode node = _graph.Nodes.Single(n => n.Id == id);
        if (node.Kind == GraphNodeKind.Clip) { if (node.Clip is { } clip && node.Rate > 0) clips.Add((clip, node.Rate)); return; }
        if (node.Inputs.Count == 0) return;
        foreach (var input in Weights(node)) if (input.Weight > 0) Collect(input.Input.Node, clips);
    }
    public IReadOnlyDictionary<string, float> InputWeights()
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        void Visit(string id, float weight)
        {
            result[id] = result.GetValueOrDefault(id) + weight;
            GraphNode node = _graph.Nodes.Single(n => n.Id == id);
            if (node.Inputs.Count == 0) return;
            var inputs = Weights(node); float sum = inputs.Sum(i => i.Weight);
            foreach (var input in inputs)
                Visit(input.Input.Node, weight * (node.Kind is GraphNodeKind.Layer or GraphNodeKind.Additive ? input.Weight : sum > 0 ? input.Weight / sum : 0));
        }
        float alpha = _previous is not null && _blendDuration > 0 ? Math.Clamp(_blendTime / _blendDuration, 0, 1) : 1;
        Visit(_graph.States.Single(s => s.Name == _state).Node, alpha);
        if (_previous is not null) Visit(_graph.States.Single(s => s.Name == _previous).Node, 1 - alpha);
        return result;
    }
    private bool Matches(GraphTransition t)
    {
        float value = _parameters.GetValueOrDefault(t.Parameter);
        return t.Operator switch
        {
            ConditionOperator.Greater => value > t.Value, ConditionOperator.Less => value < t.Value,
            ConditionOperator.Equal => value == t.Value, ConditionOperator.NotEqual => value != t.Value,
            ConditionOperator.True or ConditionOperator.Triggered => value != 0, _ => value == 0,
        };
    }
    public BoneTransform[] Evaluate()
    {
        BoneTransform[] pose = Node(_graph.States.Single(s => s.Name == _state).Node, _time, new(StringComparer.Ordinal));
        if (_previous is not null)
        {
            BoneTransform[] previous = Node(_graph.States.Single(s => s.Name == _previous).Node, _previousTime, new(StringComparer.Ordinal));
            float alpha = _blendDuration > 0 ? Math.Clamp(_blendTime / _blendDuration, 0, 1) : 1;
            for (int i = 0; i < pose.Length; i++) pose[i] = BoneTransform.Blend(previous[i], pose[i], alpha);
        }
        return pose;
    }
    private BoneTransform[] Node(string id, float time, HashSet<string> visiting)
    {
        if (!visiting.Add(id)) throw new InvalidDataException("Animation graph contains a node cycle.");
        GraphNode node = _graph.Nodes.Single(n => n.Id == id);
        BoneTransform[] result;
        if (node.Kind == GraphNodeKind.Clip)
            result = ClipEvaluator.Evaluate(_skeleton, _project.Clips.Single(c => c.Id == node.Clip), time * node.Rate);
        else if (node.Inputs.Count == 0) result = _skeleton.Bones.Select(b => b.Rest).ToArray();
        else
        {
            List<(GraphInput Input, float Weight)> inputs = Weights(node);
            result = Node(inputs[0].Input.Node, time, visiting);
            float total = inputs[0].Weight;
            for (int n = 1; n < inputs.Count; n++)
            {
                var (input, weight) = inputs[n];
                if (weight <= 0) continue;
                BoneTransform[] next = Node(input.Node, time, visiting);
                float alpha = node.Kind is GraphNodeKind.Layer or GraphNodeKind.Additive ? weight : weight / (total + weight);
                for (int b = 0; b < result.Length; b++)
                {
                    if (node.Mask.Count > 0 && !node.Mask.Contains(_skeleton.Bones[b].Name, StringComparer.Ordinal)) continue;
                    result[b] = node.Kind == GraphNodeKind.Additive ? ClipEvaluator.Additive(result[b], next[b], _skeleton.Bones[b].Rest, alpha)
                        : BoneTransform.Blend(result[b], next[b], alpha);
                }
                total += weight;
            }
        }
        visiting.Remove(id);
        return result;
    }
    private List<(GraphInput Input, float Weight)> Weights(GraphNode node)
    {
        float x = _parameters.GetValueOrDefault(node.Parameter);
        if (node.Kind == GraphNodeKind.Blend1D)
        {
            GraphInput[] ordered = node.Inputs.OrderBy(i => i.Point.X).ToArray();
            if (x <= ordered[0].Point.X) return [(ordered[0], 1)];
            for (int i = 1; i < ordered.Length; i++)
                if (x < ordered[i].Point.X)
                {
                    float a = (x - ordered[i - 1].Point.X) / (ordered[i].Point.X - ordered[i - 1].Point.X);
                    return [(ordered[i - 1], 1 - a), (ordered[i], a)];
                }
            return [(ordered[^1], 1)];
        }
        if (node.Kind == GraphNodeKind.Blend2D)
        {
            var point = new System.Numerics.Vector2(x, _parameters.GetValueOrDefault(node.ParameterY));
            foreach (GraphInput input in node.Inputs)
                if (System.Numerics.Vector2.DistanceSquared(input.Point, point) < 1e-10f) return [(input, 1)];
            return node.Inputs.Select(i => (i, 1 / System.Numerics.Vector2.DistanceSquared(i.Point, point))).ToList();
        }
        float layerWeight = node.Weight * _parameters.GetValueOrDefault(node.Parameter, 1);
        return node.Inputs.Select((i, index) => (i, index == 0 ? 1 : Math.Clamp(layerWeight, 0, 1))).ToList();
    }
}
