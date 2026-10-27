using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the spy bullet launcher, its wall marker and the mark expiry (spec/equipment/spy-bullet.md).
public static partial class K
{
    [Provenance(ProvenanceKind.Decoded, "UseSpyBullet 0x10aa17d0: origin = hand bone + 30 X")]
    public const float SpyBulletHandForward = 30f;

    [Provenance(ProvenanceKind.Decoded, "UseSpyBullet 0x10aa17d0: origin = hand bone - 4 Y")]
    public const float SpyBulletHandRight = -4f;

    [Provenance(ProvenanceKind.Decoded, "UseSpyBullet 0x10aa17d0: origin = hand bone + 8 Z")]
    public const float SpyBulletHandUp = 8f;

    [Provenance(ProvenanceKind.Decoded, "UseSpyBullet 0x10aa17d0: end = origin + 15000 X")]
    public const float SpyBulletRange = 15000f;

    [Provenance(ProvenanceKind.Decoded, "ThrowBullet 0x10aa1a00: the shot and hit sounds at volume 1, radius 100")]
    public const float SpyBulletSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SSpyBulletLauncher.MyLocalMessage AddMessageToStack(MessageOnWall, 3)")]
    public const int SpyBulletWallMessageTime = 3;

    [Provenance(ProvenanceKind.Decoded, "ThrowBullet 0x10aa1a00 HitBySpyBullet with EAX = 0; SProjMarqueurSpy.Tick HitBySpyBullet(0)")]
    public const int SpyBulletTeam = 0;

    [Provenance(ProvenanceKind.Decoded, "ThrowBullet 0x10aa1a00: a merc hit sends BroadcastTeamLocalizedMessage(0, SGameMessages, 13) (EnemyLocked)")]
    public const int SpyBulletMsgEnemyLocked = 13;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.Tick: a merc's mark wears off with SGameMessages 14 (SpyOff) to team 0")]
    public const int SpyBulletMsgSpyOff = 14;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.Tick: a spy's mark wears off with SGameMessages 61 (SpyUnLockedByDef) to team 1")]
    public const int SpyBulletMsgSpyUnlocked = 61;

    [Provenance(ProvenanceKind.Decoded, "SProjMarqueurSpy.Tick: a listed merc beyond 1.1 x fDetectionRadius is dropped")]
    public const float SpyZoneDropFactor = 1.1f;
}
