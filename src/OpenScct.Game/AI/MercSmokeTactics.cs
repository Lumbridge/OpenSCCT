using OpenScct.Core;

namespace OpenScct.Game.AI;

public static class MercSmokeTactics
{
    /// <summary>A short blind burst at a body actually seen where smoke has now appeared. Never reads live pawn state.</summary>
    public static bool LastSeenShot(AiWorld world, FVector eye, AiContact contact, float now, out FVector point, float maxAge = 2.5f)
    {
        point = default;
        if (contact.Pose is not { Alive: true } pose || now - contact.PoseTime > maxAge ||
            now - contact.LastConfirmedAt > maxAge + .5f || !world.InSmoke(pose.Position) ||
            !world.SmokeBetween(eye, pose.Position) || !world.LineOpen(eye, pose.Body)) return false;
        point = pose.Body;
        return true;
    }
}
