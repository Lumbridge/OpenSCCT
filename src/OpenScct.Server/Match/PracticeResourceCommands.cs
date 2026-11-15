using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;

namespace OpenScct.Server;

/// <summary>The same practice resource command for listen games and dedicated servers.</summary>
public static class PracticeResourceCommands
{
    public const string Help = "!infinite [on|off|toggle] - Infinite ammo, gadgets and gas mask air for this practice round. No argument shows the current setting.";

    public static string Run(Level level, string argument)
    {
        if (level.GetService<PracticeRules>() is not { On: true } rules)
            return "Infinite resources can only be changed in practice mode.";
        GameReplicationInfo? gri = level.Info.GRI;
        bool on = gri is not null ? !gri.PracticeFiniteResources : rules.InfiniteResources;
        switch (argument.Trim().ToLowerInvariant())
        {
            case "": return on ? "Infinite resources are on." : "Infinite resources are off.";
            case "on": on = true; break;
            case "off": on = false; break;
            case "toggle": on = !on; break;
            default: return Help;
        }
        rules.InfiniteResources = on;
        if (gri is not null) gri.PracticeFiniteResources = !on;
        return on ? "Infinite resources on: ammo, gadget charges and mask air are no longer consumed."
            : "Infinite resources off: ammo, gadget charges and mask air are consumed normally.";
    }
}
