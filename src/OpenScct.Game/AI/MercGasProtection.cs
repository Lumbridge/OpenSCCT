using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>React to visible gas/cameras or an audible camera, using the carried mask's normal input.</summary>
public sealed class MercGasProtection
{
    private SBasePawn? _pawn;
    private float _noticed = -1f;
    private float _safeAt;
    private float _retryAt;

    public void Apply(AiController bot, float now, ref AiPlan plan)
    {
        if (bot.Pawn is not SPAWNDEFENSE pawn) return;
        if (!ReferenceEquals(_pawn, pawn))
        {
            _pawn = pawn;
            _noticed = -1f;
            _safeAt = _retryAt = 0f;
        }
        if (pawn.bSleeping || pawn.bLyingOnGround || pawn.bStickyShocking || pawn.Health <= 0 ||
            !bot.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", System.StringComparison.Ordinal)) return;
        FVector eye = AiSight.Eye(pawn);
        bool Visible(FVector at) => AiSight.InView(at - eye, bot.Controller.Rotation, bot.Controller.FovAngle) &&
            bot.World.LineOpen(eye, at);
        bool threat = bot.World.InSmoke(pawn.Location, pawn.CollisionRadius);
        foreach (SSmokeEmitter smoke in bot.World.Smokes)
            if (!smoke.bDeleteMe && (smoke.Location - pawn.Location).Size() < smoke.RadiusEffect + 200f && Visible(smoke.Location)) threat = true;
        foreach (SStickyCam cam in bot.World.Level.DynamicActors<SStickyCam>())
            if (cam is not SCamNetwork && !cam.bDeleteMe && (cam.Location - pawn.Location).SizeSquared() < 550f * 550f &&
                Visible(cam.Location) && !bot.World.SmokeBetween(eye, cam.Location)) threat = true;
        foreach (SStickyCamEmitter gas in bot.World.Level.DynamicActors<SStickyCamEmitter>())
            if (!gas.bDeleteMe && (gas.RefLocation - pawn.Location).SizeSquared() < 250f * 250f &&
                bot.World.LineOpen(eye, gas.RefLocation) && (Visible(gas.RefLocation) || (gas.RefLocation - pawn.Location).Size() < 128f)) threat = true;
        foreach (AiNoise noise in bot.World.Noise.Noises)
            if (noise.Source is SStickyCam and not SCamNetwork && (noise.Location - pawn.Location).SizeSquared() < 550f * 550f &&
                AiNoiseBoard.Heard(bot.World, noise, eye, bot.Difficulty) > 0f) threat = true;
        if (threat && bot.Difficulty.GadgetUse > 0f)
        {
            if (_noticed < 0f) _noticed = now;
            _safeAt = now + 2f;
        }
        else if (now >= _safeAt) _noticed = -1f;
        bool want = _noticed >= 0f && now - _noticed >= bot.Difficulty.ReactionTime * 1.5f;
        if (!want && !pawn.bGasMask) return;
        SGadGasMask? mask = null;
        int slot = 0;
        for (SGadget? g = pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (!PawnInventory.IsSelectable(g)) continue;
            if (g is SGadGasMask m) { mask = m; break; }
            slot++;
        }
        if (mask is null || want && mask.MaskDuration <= 0f) return;
        // Do not let a frag or trap selection unload a mask while gas is nearby.
        bool laserSwitch = plan.Keys.Command1 == AiCommand.GadLaserSwitch || plan.Keys.Command2 == AiCommand.GadLaserSwitch;
        plan.Keys.Command1 = plan.Keys.Command2 = AiCommand.None;
        plan.Keys.bAltFire = 0;
        if (plan.Keys.bFire != 0) plan.Keys.Press(AiCommand.Fire);
        if (want && !ReferenceEquals(pawn.SelectedGadget, mask)) plan.Keys.Press(AiCommand.SelectGadget, slot);
        else if (want != pawn.bGasMask && now >= _retryAt)
        {
            plan.Keys.Press(AiCommand.AltFire);
            _retryAt = now + .5f;
        }
        else if (laserSwitch) plan.Keys.Press(AiCommand.GadLaserSwitch);
    }
}
