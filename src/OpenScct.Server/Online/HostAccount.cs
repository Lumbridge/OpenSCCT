namespace OpenScct.Server.Online;

/// <summary>
/// A listen server's own signed-in account: its id on the master, its claimed name and the cosmetics it has unlocked
/// (as the master last told the host's game).
/// </summary>
public sealed record HostAccount(long AccountId, string Name, System.Collections.Generic.IReadOnlyList<string>? Unlocked = null);
