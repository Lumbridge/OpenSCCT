namespace OpenScct.Net;

/// <summary>
/// Which network behaviour a host runs (spec/reloaded/FEATURES.md <c>rl:net.rates</c>; docs/NETCODE.md, "Send rate and
/// budget: Stock and Enhanced").
/// </summary>
public enum NetProfile : byte
{
    /// <summary>
    /// The original rate preset: replication at 20 Hz on the internet, 35 Hz with the lanplay switch.
    /// Uses the same modern synchronization and independent client budgets as Enhanced.
    /// </summary>
    Stock,

    /// <summary>
    /// Replication every world tick (<see cref="K.EnhancedServerTickRate"/>, 60 Hz) with a send budget per client
    /// (<see cref="SendBudgetMode.PerClientAdaptive"/>), so one slow client no longer holds back the others.
    /// </summary>
    Enhanced,
}

/// <summary>What a <see cref="NetProfile"/> means for a host's send rate and driver settings.</summary>
public static class NetProfiles
{
    /// <summary>The replication rate of <paramref name="profile"/> when none was asked for.</summary>
    public static int DefaultRate(NetProfile profile, bool lanPlay) => profile switch
    {
        NetProfile.Enhanced => K.EnhancedServerTickRate,
        _ => lanPlay ? K.LanServerMaxTickRate : K.NetServerMaxTickRate,
    };

    /// <summary>The asked-for rate (an override such as <c>--rate</c>), or the profile's default when it is 0.</summary>
    public static int Rate(NetProfile profile, bool lanPlay, int requested) =>
        requested > 0 ? requested : DefaultRate(profile, lanPlay);

    /// <summary>
    /// Both profiles use explicit pawn snapshots and independent budgets. The rate preset determines the
    /// replication passes against which the update frequency gate is evaluated.
    /// </summary>
    public static NetDriverSettings Apply(NetDriverSettings net, NetProfile profile, int netRate) =>
        net with
        {
            Budget = SendBudgetMode.PerClientAdaptive,
            NetServerMaxTickRate = netRate,
            LanServerMaxTickRate = netRate,
            PawnProxies = PawnProxyMode.Snapshots,
        };

    /// <summary>Parses <c>stock</c> or <c>enhanced</c> (any case).</summary>
    public static bool TryParse(string? text, out NetProfile profile)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "stock":
                profile = NetProfile.Stock;
                return true;
            case "enhanced":
                profile = NetProfile.Enhanced;
                return true;
            default:
                profile = NetProfile.Enhanced;
                return false;
        }
    }
}
