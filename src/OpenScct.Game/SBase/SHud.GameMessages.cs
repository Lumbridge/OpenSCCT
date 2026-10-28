using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SHud.SetGameMessage: the game message queue the HUD pages show (spec/hud/messages.md rule 7). The game layer's
// message routing (HudMessages) and the controller's mission reminder both add to it.
public partial class SHud
{
    /// <summary>
    /// <c>SHud.SetGameMessage</c>: a text already queued is not added again; the queue is kept in priority order
    /// (lowest first, a new entry after the others of its priority). The page shows the head (rule 8).
    /// </summary>
    [Uc("SBase.SHud.SetGameMessage", Sha1 = "8d6b449a46a8f0f586f42dab56e4b74f88ffc2ef")]
    [PortOf("uc:SBase.SHud:SetGameMessage", Spec = "spec/hud/messages.md#rules")]
    public void SetGameMessage(string text, byte prio)
    {
        List<FStackedMessage> queue = GameMessageQueue;
        foreach (FStackedMessage m in queue)
        {
            if (m.Message == text)
            {
                return;
            }
        }

        var entry = new FStackedMessage { Message = text, prio = prio };
        if (queue.Count == 0 || prio >= queue[^1].prio)
        {
            queue.Add(entry);
        }
        else if (prio < queue[0].prio)
        {
            queue.Insert(0, entry);
        }
        else
        {
            // The script's search loop steps once more after it finds the first higher priority, so the entry lands
            // just after that one.
            int i = 0;
            while (i < queue.Count && queue[i].prio <= prio)
            {
                i++;
            }

            queue.Insert(Math.Min(i + 1, queue.Count), entry);
        }
    }
}
