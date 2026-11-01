using OpenScctGame.Hud;

namespace OpenScctGame.Smoke;

/// <summary>Checks the merc rain loop and its exit against the converted Magma animation.</summary>
public sealed class RainHudSmoke : SmokeTest
{
    public const string TestName = "rain-hud";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        HudAssets? assets = HudAssets.TryLoad(null, out string? error);
        if (assets is null)
        {
            return SmokeResult.Fail(error ?? "no UI content");
        }

        foreach (int fps in new[] { 30, 60, 144 })
        {
            var page = new MercHudPage(assets);
            var snapshot = new HudSnapshot { Valid = true };
            var drops = page.Area("gouttes/Gouttes")!;
            snapshot.Merc.Wet = true;
            for (int i = 0; i < 13 * fps; i++)
            {
                page.Update(snapshot, 1f / fps);
            }

            if (!drops.Visible || !drops.Playing)
            {
                return SmokeResult.Fail($"rain loop stopped while wet at {fps} fps");
            }

            snapshot.Merc.Wet = false;
            for (int i = 0; i < 13 * fps; i++)
            {
                page.Update(snapshot, 1f / fps);
            }

            if (drops.Visible || page.Area("gouttes")!.Visible)
            {
                return SmokeResult.Fail($"rain overlay still visible after leaving rain at {fps} fps (frame {drops.Frame})");
            }

            snapshot.Merc.Wet = true;
            page.Update(snapshot, 1f / fps);
            if (!drops.Visible || !drops.Playing)
            {
                return SmokeResult.Fail($"rain overlay did not restart on reentry at {fps} fps");
            }
        }

        return SmokeResult.Pass("merc rain loops, clears after exit, and restarts at 30/60/144 fps");
    }
}
