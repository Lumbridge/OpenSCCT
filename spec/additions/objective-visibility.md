# Inactive objective visibility

Authored SVMaps can set the boolean `HideWhileInactive` on an `SObjectiveTrigger` or subclass in
`sim/gameplay.json`. `false` keeps an inactive device visible; `true` hides it. Activating a device still uses its
normal reset, and inactive devices remain unusable. Editor builds write `false` by default, including for unlinked
devices. The editor lowers an objective's setting onto its devices, with device overrides taking precedence.

Maps that omit the property retain the original game's visibility rules. This is an OpenSCCT extension and is not
written to original-game exports. The setting is map data on both server and clients; normal `bHidden` replication
communicates subsequent changes. Preparation must not reveal a pending device that opts into hiding, and an
inactive client-side hunt disk must retain the authored setting rather than applying its legacy hide rule.
