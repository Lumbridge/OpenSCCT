using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// One replicated function call of the combat and equipment ports, named after the original function
/// (<c>ClientDamage</c>, <c>PlayAnimDmg</c>, <c>ClientStickyShock</c>, <c>DamagePawn_C2S</c>,
/// <c>ServerFireWeapon</c>...). Only the arguments the call uses are set; the receiving side runs it with
/// <see cref="SBase.SPlayerController.ReceiveCombatCall"/> (controller calls) or on the named actor.
/// </summary>
public readonly record struct CombatCall(Name Function)
{
    /// <summary>The actor the call runs on when it is not the controller (a weapon, a gadget, a pawn).</summary>
    public Actor? Target { get; init; }

    public int Int1 { get; init; }

    public int Int2 { get; init; }

    public int Int3 { get; init; }

    public float Float1 { get; init; }

    public bool Bool1 { get; init; }

    public FVector Vector1 { get; init; }

    public FVector Vector2 { get; init; }

    public FRotator Rotation { get; init; }

    public Actor? Actor1 { get; init; }

    public Actor? Actor2 { get; init; }

    public SDamageType DamageType { get; init; }

    public Name Name1 { get; init; }

    public override string ToString() => Function.ToString();
}

/// <summary>
/// Where combat calls leave this machine: <see cref="ClientCall"/> for a server-to-owner call on a remote player,
/// <see cref="ServerCall"/> for an owner-to-server call made on a client. The simulation runs the call itself when
/// the other end is on this machine (a listen host's own player, a standalone game), so the network layer only
/// carries the remote ones. Install with <c>Level.SetService&lt;ICombatNet&gt;(net)</c>.
/// </summary>
public interface ICombatNet
{
    /// <summary>A reliable call from the server to <paramref name="Owner"/>'s machine.</summary>
    void ClientCall(PlayerController Owner, in CombatCall Call);

    /// <summary>A reliable call from <paramref name="Sender"/>'s machine to the server.</summary>
    void ServerCall(PlayerController Sender, in CombatCall Call);

    /// <summary>
    /// True when <paramref name="Owner"/>'s machine has <paramref name="A"/> (its channel is open there): the targets
    /// of a broadcast call (spec/native/net-replication.md rule 20).
    /// </summary>
    bool HasActor(PlayerController Owner, Actor A) => true;
}

/// <summary>The combat network of a level without a network layer: nothing leaves the machine.</summary>
[Provenance(ProvenanceKind.Estimated, "No network layer: every call stays on this machine")]
public sealed class NullCombatNet : ICombatNet
{
    public static readonly NullCombatNet Instance = new();

    private NullCombatNet()
    {
    }

    public void ClientCall(PlayerController Owner, in CombatCall Call)
    {
    }

    public void ServerCall(PlayerController Sender, in CombatCall Call)
    {
    }
}
