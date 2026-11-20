using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using OpenScct.Game.Tests.Combat;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class WeaponAttachmentTests
{
    private const string M4 = "Svm.M4A1S";
    private const int DotAndLaser = WeaponAttachments.RedDot | WeaponAttachments.Laser;
    private const int DotLaserSuppressor = DotAndLaser | WeaponAttachments.Suppressor;
    private const int Common = DotLaserSuppressor | WeaponAttachments.Acog;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, WeaponAttachments.RedDot)]
    [InlineData(19, WeaponAttachments.RedDot)]
    [InlineData(20, DotAndLaser)]
    [InlineData(29, DotAndLaser)]
    [InlineData(30, DotLaserSuppressor)]
    [InlineData(39, DotLaserSuppressor)]
    [InlineData(40, DotLaserSuppressor | WeaponAttachments.ExtendedMagazine)]
    [InlineData(49, DotLaserSuppressor | WeaponAttachments.ExtendedMagazine)]
    [InlineData(50, Common | WeaponAttachments.ExtendedMagazine)]
    public void M4UnlocksEachItemAtItsOwnKillThreshold(int kills, int expected) =>
        Assert.Equal(expected, WeaponAttachments.Available(M4, kills));

    [Fact]
    public void AllMercGunsSupportBothOpticsAndExtendedMagazinesApplyOnlyToEligiblePrimaries()
    {
        foreach (var rifle in MercRifles.All)
            Assert.Equal(rifle.ClassPath is "Svm.PumpShotgun" or "Svm.BoltRifle" ? Common : Common | WeaponAttachments.ExtendedMagazine,
                WeaponAttachments.Supported(rifle.ClassPath));
        foreach (var pistol in Sidearms.All)
            Assert.Equal(pistol.Kind == SidearmKind.Suppressed ? Common & ~WeaponAttachments.Suppressor : Common,
                WeaponAttachments.Supported(pistol.CarryId));
        Assert.Equal(Common | WeaponAttachments.ExtendedMagazine, WeaponAttachments.Supported("SBase.SWeaponDefense"));
        Assert.Equal(Common | WeaponAttachments.ExtendedMagazine, WeaponAttachments.Supported("SBase.SWeaponDefense_Uzi"));
        Assert.Equal(Common, WeaponAttachments.Supported("SBase.SWeaponDefense_Shotgun"));
        Assert.Equal(0, WeaponAttachments.Supported(Loadouts.SpyWeapon.Path));
    }

    [Fact]
    public void ValidationRequiresBothTheUnlocksAndThePointBudget()
    {
        int sightAndSuppressor = WeaponAttachments.RedDot | WeaponAttachments.Suppressor;
        Assert.Equal(3, WeaponAttachments.Cost(sightAndSuppressor));
        Assert.False(WeaponAttachments.IsValid(M4, sightAndSuppressor, 29));
        Assert.True(WeaponAttachments.IsValid(M4, sightAndSuppressor, 30));
        Assert.False(WeaponAttachments.IsValid(M4, sightAndSuppressor | WeaponAttachments.Laser, 30));
        Assert.Equal(16, WeaponAttachments.Acog);
        Assert.Equal(2, WeaponAttachments.Cost(WeaponAttachments.Acog));
        Assert.False(WeaponAttachments.IsValid(M4, WeaponAttachments.Acog, 49));
        Assert.True(WeaponAttachments.IsValid(M4, WeaponAttachments.Acog | WeaponAttachments.Laser, 50));
        Assert.False(WeaponAttachments.IsValid(M4, WeaponAttachments.Acog | WeaponAttachments.RedDot, 50));
        Assert.False(WeaponAttachments.IsValid(M4, WeaponAttachments.Acog | WeaponAttachments.Suppressor, 50));
        Assert.False(WeaponAttachments.IsValid(M4, 32, 1000));
        Assert.False(WeaponAttachments.IsValid("SBase.SWeaponDefense_Shotgun", WeaponAttachments.ExtendedMagazine, 1000));
    }

    [Theory]
    [InlineData(M4, WeaponAttachments.Suppressor | WeaponAttachments.Laser, 20, WeaponAttachments.Laser)]
    [InlineData(M4, WeaponAttachments.ExtendedMagazine | WeaponAttachments.RedDot, 30, WeaponAttachments.RedDot)]
    [InlineData(M4, WeaponAttachments.ExtendedMagazine | WeaponAttachments.RedDot, 40, WeaponAttachments.ExtendedMagazine | WeaponAttachments.RedDot)]
    [InlineData(M4, WeaponAttachments.Acog | WeaponAttachments.RedDot, 10, 0)]
    [InlineData(M4, WeaponAttachments.Acog | WeaponAttachments.RedDot, 50, 0)]
    [InlineData(M4, WeaponAttachments.Acog | WeaponAttachments.Suppressor, 30, 0)]
    [InlineData(M4, 32 | WeaponAttachments.Laser, 50, 0)]
    [InlineData("SBase.SWeaponDefense_Shotgun", WeaponAttachments.ExtendedMagazine | WeaponAttachments.RedDot, 50, 0)]
    [InlineData("Svm.BoltRifle", WeaponAttachments.Acog | WeaponAttachments.ExtendedMagazine, 50, WeaponAttachments.Acog)]
    [InlineData("Svm.BoltRifle", WeaponAttachments.Acog | WeaponAttachments.ExtendedMagazine, 40, 0)]
    [InlineData("Svm.BoltRifle", WeaponAttachments.Suppressor | WeaponAttachments.Laser | WeaponAttachments.ExtendedMagazine, 50, 0)]
    public void SavedSelectionsRetainEarnedItemsOnlyFromLegitimateCombinations(string gun, int saved, int kills, int expected) =>
        Assert.Equal(expected, WeaponAttachments.EarnedSelection(gun, saved, kills));

    [Fact]
    public void DirectWeaponInitializationAlsoRejectsConflictingOpticsAndUnsupportedMagazines()
    {
        var rifle = new M4A1S();
        WeaponAttachments.Apply(rifle, WeaponAttachments.Acog | WeaponAttachments.RedDot | WeaponAttachments.ExtendedMagazine);
        Assert.Equal(0, rifle.AttachmentMask);
        Assert.Equal(20, rifle.ClipMaxAmmo);
        var shotgun = new SWeaponDefense_Shotgun();
        WeaponAttachments.Apply(shotgun, WeaponAttachments.ExtendedMagazine);
        Assert.Equal(0, shotgun.AttachmentMask);
        Assert.Equal(6, shotgun.ClipMaxAmmo);
    }

    [Theory]
    [InlineData("SBase.SWeaponDefense", 20, 30, 100)]
    [InlineData("SBase.SWeaponDefense_Uzi", 20, 30, 300)]
    [InlineData(M4, 20, 30, 80)]
    [InlineData("Svm.AK47", 20, 30, 90)]
    [InlineData("Svm.Galil", 20, 30, 105)]
    [InlineData("Svm.Famas", 20, 30, 100)]
    [InlineData("Svm.CompactSMG", 20, 30, 100)]
    [InlineData("Svm.PumpShotgun", 6, 6, 44)]
    [InlineData("Svm.BoltRifle", 5, 5, 40)]
    [InlineData("Svm.DesignatedMarksman", 20, 30, 60)]
    [InlineData("SBase.SWeaponDefense_Shotgun", 6, 6, 44)]
    [InlineData("OpenScct.Sidearm_suppressed", 12, 12, 48)]
    [InlineData("OpenScct.Sidearm_automatic", 20, 20, 80)]
    [InlineData("OpenScct.Sidearm_magnum44", 6, 6, 24)]
    [InlineData("OpenScct.Sidearm_desert_eagle", 7, 7, 21)]
    [InlineData("OpenScct.Sidearm_unsuppressed", 12, 12, 48)]
    public void SpawningEachGunRespectsItsMagazineSupportReserveAndOptic(string gun, int standard, int extended, int reserve)
    {
        foreach (int requested in new[] { 0, WeaponAttachments.ExtendedMagazine, WeaponAttachments.Acog })
        {
            var scene = new CombatScene(ENetMode.NM_ListenServer);
            var (owner, pawn) = scene.Player(false, FVector.Zero);
            owner.GameReplicationInfo = new GameReplicationInfo { RiflesEnabled = true, SidearmsEnabled = true };
            var team = new STeamDefenseur();
            // Custom maps can still require one of the original weapons.
            if (gun is "SBase.SWeaponDefense" or "SBase.SWeaponDefense_Uzi" or "SBase.SWeaponDefense_Shotgun")
                team.ClassWeapon = [new ClassRef(gun)];
            pawn.PlayerReplicationInfo!.Team = team;
            pawn.PlayerReplicationInfo.SvmRank = 50;
            for (int i = 0; i < 50; i++) WeaponCareer.RecordKill(owner, gun);
            Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
            bool pistol = Sidearms.Find(gun) != null;
            owner.ReceiveCombatCall(new CombatCall(new Name(pistol ? "ServerSpawnInventory" : "ServerSpawnWeapon"))
                { Target = pawn, Name1 = new Name(gun), Int1 = requested }, FromOwner: true);
            var weapon = Assert.IsAssignableFrom<SWeaponDefense>(pistol ? pawn.SidearmWeapon : pawn.PrimaryWeapon);
            Assert.Equal(gun, WeaponAttachments.Key(weapon));
            int capacity = requested == WeaponAttachments.ExtendedMagazine ? extended : standard;
            Assert.Equal(standard == extended && requested == WeaponAttachments.ExtendedMagazine ? 0 : requested,
                weapon.AttachmentMask);
            Assert.Equal(capacity, weapon.ClipAmmo);
            Assert.Equal(capacity, weapon.ClipMaxAmmo);
            Assert.Equal(capacity + reserve, weapon.Ammo);
            Assert.Equal(capacity + reserve, weapon.MaxAmmo);
        }
    }

    [Theory]
    [InlineData("SBase.SWeaponDefense", true, M4)]
    [InlineData("SBase.SWeaponDefense", false, "Svm.CompactSMG")]
    [InlineData("SBase.SWeaponDefense_Uzi", true, "Svm.CompactSMG")]
    [InlineData("SBase.SWeaponDefense_Shotgun", true, "Svm.PumpShotgun")]
    public void AFormerPrimaryOnANormalMapMigratesWithoutTransferringItsAttachments(string former, bool rifles, string expected)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (owner, pawn) = scene.Player(false, FVector.Zero);
        owner.GameReplicationInfo = new GameReplicationInfo { RiflesEnabled = rifles };
        pawn.PlayerReplicationInfo!.Team = new STeamDefenseur();
        pawn.PlayerReplicationInfo.SvmRank = 50;
        for (int i = 0; i < 50; i++) WeaponCareer.RecordKill(owner, former);
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        Loadouts.ServerSpawnWeapon(pawn, new ClassRef(former), WeaponAttachments.Acog | WeaponAttachments.ExtendedMagazine);
        var replacement = Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon);
        Assert.Equal(expected, WeaponAttachments.Key(replacement));
        Assert.Equal(0, replacement.AttachmentMask);
        Assert.Equal(expected == "Svm.PumpShotgun" ? 6 : 20, replacement.ClipAmmo);
        Assert.Equal(0, WeaponCareer.GetKills(owner, expected));
    }

    [Theory]
    [InlineData("Svm.CompactSMG", true)]
    [InlineData("Svm.PumpShotgun", true)]
    [InlineData("Svm.BoltRifle", false)]
    [InlineData("Svm.DesignatedMarksman", false)]
    public void StockOnlyHostsOfferTheTwoReplacementGunsAndEnforceThePrecisionRifleOption(string gun, bool supplied)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (owner, pawn) = scene.Player(false, FVector.Zero);
        owner.GameReplicationInfo = new GameReplicationInfo { RiflesEnabled = false };
        pawn.PlayerReplicationInfo!.Team = new STeamDefenseur();
        pawn.PlayerReplicationInfo.SvmRank = 1;
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        Loadouts.ServerSpawnWeapon(pawn, new ClassRef(gun));
        if (supplied) Assert.Equal(gun, WeaponAttachments.Key(Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon)));
        else Assert.Null(pawn.MyWeapon);
        Assert.Equal(!supplied, pawn.GearGrantWeapon);
    }

    [Fact]
    public void WeaponProgressFollowsThePlayerAndWeaponAcrossRoundSnapshots()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (first, _) = scene.Player(false, FVector.Zero);
        var (other, _) = scene.Player(false, new FVector(200, 0, 0));
        string pistol = Sidearms.Get(SidearmKind.Unsuppressed).CarryId;
        for (int kill = 0; kill < 20; kill++) WeaponCareer.RecordKill(first, M4);
        WeaponCareer.RecordKill(first, pistol);
        WeaponCareer.RecordKill(other, "Svm.Galil");

        var saved = first.WeaponProgress.Snapshot();
        first.WeaponProgress = new WeaponCareer(saved);
        saved[WeaponCareerCounters.IndexOf(M4)] = 0;
        Assert.Equal(20, WeaponCareer.GetKills(first, M4));
        Assert.Equal(1, WeaponCareer.GetKills(first, pistol));
        Assert.Equal(0, WeaponCareer.GetKills(first, "Svm.Galil"));
        Assert.Equal(0, WeaponCareer.GetKills(other, M4));
        Assert.Equal(1, WeaponCareer.GetKills(other, "Svm.Galil"));
        Assert.True(WeaponAttachments.IsValid(M4, WeaponAttachments.Laser, WeaponCareer.GetKills(first, M4)));
        Assert.False(WeaponAttachments.IsValid(M4, WeaponAttachments.Laser, WeaponCareer.GetKills(other, M4)));
    }

    [Fact]
    public void ServerGrantUsesTheOwningPlayersKillsAndPointBudget()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (first, firstPawn) = scene.Player(false, FVector.Zero);
        var (other, otherPawn) = scene.Player(false, new FVector(200, 0, 0));
        foreach (var (pc, pawn) in new[] { (first, firstPawn), (other, otherPawn) })
        {
            pc.GameReplicationInfo = new GameReplicationInfo { RiflesEnabled = true };
            pawn.PlayerReplicationInfo!.Team = new STeamDefenseur();
            pawn.PlayerReplicationInfo.SvmRank = 30;
            Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        }
        const int extendedAndAcog = WeaponAttachments.ExtendedMagazine | WeaponAttachments.Acog;
        for (int i = 0; i < 50; i++) WeaponCareer.RecordKill(first, M4);
        for (int i = 0; i < 49; i++) WeaponCareer.RecordKill(other, M4);
        // Fifty kills on another rifle cannot supply the missing M4 kill.
        for (int i = 0; i < 50; i++) WeaponCareer.RecordKill(other, "Svm.Galil");
        static void Request(SPlayerController pc, SBasePawn pawn, int mask) =>
            pc.ReceiveCombatCall(new CombatCall(new Name("ServerSpawnWeapon"))
                { Target = pawn, Name1 = new Name(M4), Int1 = mask }, FromOwner: true);

        Request(first, otherPawn, extendedAndAcog);
        Assert.Null(otherPawn.MyWeapon);
        Assert.True(otherPawn.GearGrantWeapon);
        Request(other, otherPawn, extendedAndAcog);
        Request(first, firstPawn, extendedAndAcog);
        Assert.Equal(20, Assert.IsType<M4A1S>(otherPawn.MyWeapon).ClipAmmo);
        Assert.Equal(0, otherPawn.PrimaryAttachmentMask);
        Assert.Equal(30, Assert.IsType<M4A1S>(firstPawn.MyWeapon).ClipAmmo);
        Assert.Equal(extendedAndAcog, firstPawn.PrimaryAttachmentMask);
        Assert.Equal(0, WeaponAttachments.Granted(firstPawn, M4, WeaponAttachments.RedDot | WeaponAttachments.Acog));
        Assert.Equal(0, WeaponAttachments.Granted(firstPawn, M4, WeaponAttachments.Suppressor | WeaponAttachments.Acog));
    }

    [Theory]
    [InlineData(SDamageType.SDTBase, CombatDeathCause.Rifle, 0, 1)]
    [InlineData(SDamageType.SDTSnipeShot, CombatDeathCause.Sniper, 0, 1)]
    [InlineData(SDamageType.SDamage_Explosion, CombatDeathCause.FragGrenade, 0, 0)]
    [InlineData(SDamageType.SDTBase, CombatDeathCause.Rifle, 1, 0)]
    public void OnlyEnemyBulletKillsCreditTheGunThatActuallyHit(SDamageType damage, CombatDeathCause cause, int victimTeam, int expected)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Merc?Team=1", $"?Name=Other?Team={victimTeam}", "?Name=Spy?Team=0");
        var killer = players[0];
        var victim = players[1];
        var actor = Assert.IsAssignableFrom<SBasePawn>(killer.Pawn);
        var body = Assert.IsAssignableFrom<SBasePawn>(victim.Pawn);
        body.LastBulletWeapon = world.Level.SpawnActor(new M4A1S(), actor.Location, default, actor, actor);
        using var shot = CombatDeathContext.Push(new(cause));
        world.Game.Killed(killer, victim, body, damage);
        Assert.Equal(expected, WeaponCareer.GetKills(killer, M4));
        Assert.Equal(0, WeaponCareer.GetKills(killer, "SBase.SWeaponDefense"));
    }

    [Theory]
    [InlineData("Svm.CompactSMG")]
    [InlineData("Svm.PumpShotgun")]
    [InlineData("Svm.BoltRifle")]
    [InlineData("Svm.DesignatedMarksman")]
    public void NewPrimaryKillsCreditOnlyTheirOwnGunInTheLiveCareerAndRoundReport(string gun)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Merc?Team=1", "?Name=Spy?Team=0");
        var killer = players[0];
        var victim = players[1];
        var merc = Assert.IsAssignableFrom<SBasePawn>(killer.Pawn);
        var spy = Assert.IsAssignableFrom<SBasePawn>(victim.Pawn);
        killer.GameReplicationInfo!.RiflesEnabled = true;
        killer.PlayerReplicationInfo!.SvmRank = 1;
        Assert.Equal(1, EquipmentUnlocks.RequiredLevel(gun));
        PawnInventory.DeleteAllInventory(merc);
        Loadouts.OpenGrant(merc, EGameMode.GM_Multi);
        Loadouts.ServerSpawnWeapon(merc, new ClassRef(gun), WeaponAttachments.Acog);
        var issued = Assert.IsAssignableFrom<SWeaponDefense>(merc.MyWeapon);
        Assert.Equal(gun, WeaponAttachments.Key(issued));
        Assert.Equal(0, issued.AttachmentMask);
        spy.LastBulletWeapon = issued;
        using var shot = CombatDeathContext.Push(new(CombatDeathCause.Rifle));
        world.Game.Killed(killer, victim, spy, SDamageType.SDTBase);
        Assert.Equal(1, WeaponCareer.GetKills(killer, gun));
        int index = WeaponCareerCounters.IndexOf(gun);
        Assert.True(index >= 12, "New weapons must be appended after the twelve previously signed ticket counters.");
        var reported = WeaponCareerCounters.FromFeats(killer.PlayerReplicationInfo.Stats!.Feats);
        for (int i = 0; i < reported.Length; i++)
            Assert.Equal(i == index ? 1 : 0, reported[i]);
    }

    [Fact]
    public void MercGetsTheTorchWithZeroKillsAndAnEmptyAttachmentSelection()
    {
        var scene = new CombatScene();
        var (_, pawn) = scene.Player(false, FVector.Zero);
        var merc = Assert.IsAssignableFrom<SPAWNDEFENSE>(pawn);
        Assert.True(WeaponAttachments.IsValid(M4, 0, 0));
        Loadouts.AddDefaultGadgets(merc, EGameMode.GM_Multi);
        Assert.IsType<SGadTorche>(merc.Torche);
    }
}
