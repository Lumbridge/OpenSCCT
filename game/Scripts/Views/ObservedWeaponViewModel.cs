using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScctGame.Views;

/// <summary>
/// A spectator recording has the merc's third-person weapon, but not its owner's view-model channels.
/// Builds a detached presentation weapon from that recorded attachment without changing the replay world.
/// </summary>
public sealed class ObservedWeaponViewModel
{
    private SPAWNDEFENSE? _pawn;
    private SWeaponDefenseDummy? _dummy;
    private SWeaponDefense? _weapon;
    private Name _reloadSequence;
    public int ShotCount { get; private set; }

    public SWeaponDefense? Update(SBasePawn? target, float seconds)
    {
        if (target is not SPAWNDEFENSE { bDeleteMe: false, bHidden: false, Health: > 0 } pawn)
            return Clear();

        NetworkPawnAttachments state = NetworkPawnAttachments.For(pawn);
        SWeaponDefense? carried = state.ActiveSlot == 0 ? pawn.PrimaryWeapon as SWeaponDefense : pawn.SidearmWeapon;
        SWeaponDefenseDummy? dummy = carried?.Dummy ?? pawn.WeaponDummy;
        if (dummy is not { bDeleteMe: false } || !(state.WeaponInHand ?? dummy.InHand))
            return Clear();

        if (!ReferenceEquals(_pawn, pawn) || !ReferenceEquals(_dummy, dummy) ||
            dummy is MercSidearmDummy sidearm && _weapon is MercSidearm pistol && sidearm.Kind != pistol.Kind)
        {
            _pawn = pawn;
            _dummy = dummy;
            _weapon = dummy switch
            {
                MercSidearmDummy side => new MercSidearm { Kind = side.Kind },
                SWeaponDefenseDummy_Uzi => new SWeaponDefense_Uzi(),
                SWeaponDefenseDummy_Shotgun => new SWeaponDefense_Shotgun(),
                _ => new SWeaponDefense(),
            };
            if (_weapon is MercSidearm selected) selected.Configure();
            _weapon.Instigator = pawn;
            ShotCount = dummy.MuzzleCount;
            _reloadSequence = default;
        }

        SWeaponDefense weapon = _weapon!;
        weapon.ViewAnim.Advance(MathF.Max(0f, seconds), weapon,
            static (actor, channel) => ((SWeaponDefense)actor).ViewAnim.SetAlpha(channel, 0f));
        weapon.AnimateWaiting();
        // Body reload clips are recorded for observers. Align the reconstructed first-person reload with them.
        var action = pawn.PawnAnim[SBasePawn.WEAPONCHANNEL];
        bool reloading = action.Sequence.ToString().StartsWith("Relo", StringComparison.Ordinal) && action.BlendAlpha > 0f;
        if (reloading)
        {
            var clip = ViewModelClips.DataOf(weapon.AFirstReloSt);
            weapon.ViewAnim.SetAlpha(SWeaponDefense.ACTIONCHANNEL, action.BlendAlpha);
            weapon.ViewAnim.Play(SWeaponDefense.ACTIONCHANNEL, weapon.AFirstReloSt, clip);
            float progress = Math.Clamp(action.Frame / MathF.Max(0.001f, action.AnimLast), 0f, 1f);
            weapon.ViewAnim.SetFrame(SWeaponDefense.ACTIONCHANNEL, progress * (clip.Frames - 1), clip);
            if (progress >= 1f) weapon.ViewAnim.Stop(SWeaponDefense.ACTIONCHANNEL);
        }
        else if (!_reloadSequence.IsNone)
        {
            weapon.ViewAnim.Stop(SWeaponDefense.ACTIONCHANNEL);
            weapon.ViewAnim.SetAlpha(SWeaponDefense.ACTIONCHANNEL, 0f);
        }
        _reloadSequence = reloading ? action.Sequence : default;
        if (dummy.MuzzleCount != ShotCount && !reloading)
        {
            weapon.ViewAnim.SetAlpha(SWeaponDefense.ACTIONCHANNEL, 1f);
            weapon.ViewAnim.Play(SWeaponDefense.ACTIONCHANNEL, weapon.AFirstShooSt, ViewModelClips.DataOf(weapon.AFirstShooSt));
        }
        ShotCount = dummy.MuzzleCount;
        if (weapon is MercSidearm observedSidearm) observedSidearm.ReloadUntil = reloading ? 1f : 0f;
        return weapon;
    }

    private SWeaponDefense? Clear()
    {
        _pawn = null;
        _dummy = null;
        _weapon = null;
        return null;
    }
}
