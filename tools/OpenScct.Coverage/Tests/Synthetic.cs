using System.Collections.Generic;

namespace OpenScct.Coverage.Tests;

/// <summary>Small made-up inventories for the tests (names only, no game data).</summary>
internal static class Synthetic
{
    public static InventoryUnit Uc(string pkg, string cls, string func, long size = 100, bool slice = false, string? state = null) =>
        new(state is null ? $"uc:{pkg}.{cls}:{func}" : $"uc:{pkg}.{cls}:{state}.{func}",
            state is null ? "uc-func" : "uc-state-func", pkg, cls, state, func, size, "aa", "test", slice, slice ? "listed" : null);

    public static InventoryUnit Code(string pkg, string cls, string state, long size = 40) =>
        new($"uc:{pkg}.{cls}:{state}@code", "uc-state-code", pkg, cls, state, null, size, "bb", "test", false, null);

    public static InventoryUnit Nat(string pkg, string cls, string func, long size = 6) =>
        new($"nat:{pkg}.{cls}.{func}", "nat", pkg, cls, null, func, size, "cc", "test", false, null);

    public static InventoryUnit Def(string pkg, string cls, bool slice = false) =>
        new($"def:{pkg}.{cls}", "def", pkg, cls, null, null, 0, null, "test", slice, slice ? "listed" : null);

    public static InventoryUnit Rep(string pkg, string cls, string member) =>
        new($"rep:{pkg}.{cls}.{member}", "rep-var", pkg, cls, null, member, 0, null, "net", false, null);

    public static Inventory Small() => new(new List<InventoryUnit>
    {
        Uc("SBase", "SFoo", "Trigger", 300, slice: true),
        Uc("SBase", "SFoo", "Timer", 100, slice: true, state: "s_Active"),
        Code("SBase", "SFoo", "s_Active"),
        Def("SBase", "SFoo", slice: true),
        Rep("SBase", "SFoo", "bOn"),
        Nat("Engine", "Actor", "PlayAnim"),
        Uc("Engine", "Actor", "Reset", 50),
        Def("Engine", "Actor"),
        Uc("Engine", "Engine", "Init", 10),
        Uc("SBase", "SXBoxLiveRef", "LogonEvent", 400),
        Nat("SBase", "SXBoxLiveRef", "Logon"),
        Def("SBase", "SXBoxLiveRef"),
    });
}
