using System.Globalization;

namespace OpenScct.Net;

/// <summary>A remote endpoint as numbered by one transport. 0 is "none"; valid ids start at 1.</summary>
public readonly record struct EndpointId(int Value)
{
    public static EndpointId None => default;

    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
