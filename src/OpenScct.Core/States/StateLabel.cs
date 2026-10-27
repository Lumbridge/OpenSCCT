namespace OpenScct.Core;

/// <summary>A label in a state's latent code: its name, stable id and entry Pc (1 or more).</summary>
public readonly record struct StateLabel(Name Name, int Id, int Pc);
