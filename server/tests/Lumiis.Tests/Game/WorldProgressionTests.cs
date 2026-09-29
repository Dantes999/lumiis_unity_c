using System;
using System.Linq;
using Lumiis.Data.Classes;
using Lumiis.Data.Progression;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>M6a: Formeln (RecomputeDerivedStats), XP (AwardXp), Punkteverteilung (TryAllocateStatPoint),
    /// Ausweichen (TryAttack) und Regeneration/Login-Regeln (Tick/TryJoin) - kein Netzwerkbezug, siehe
    /// docs/tasks/M6a-progression.md Abschnitt 7.</summary>
    public class WorldProgressionTests
    {
        private const int TestMaxLevel = 20;

        /// <summary>Entspricht exakt data/classes.json (Krieger) - Grundlage für den Level-1-Regressionstest
        /// gegen die bisherigen Werte (MaxHp 100/Attack 15/Defense 5, siehe M4a).</summary>
        private static ClassDefinition KriegerClassDef() =>
            new ClassDefinition("Krieger", meleeRangeWire: 150, attackCooldownMs: 800,
                baseHp: 68, baseSp: 12, baseAttack: 3, baseDefense: 3,
                baseSt: 6, baseHt: 4, baseDx: 3, baseIq: 3,
                hpPerHt: 8, hpPerLevel: 10, spPerIq: 6, spPerLevel: 4, attackPerSt: 2, htPerDefense: 2,
                dodgeBpPerDx: 50, dodgeCapBp: 4000, statPointsPerLevel: 3, skillPointsPerLevel: 1,
                regenDelayMs: 5000, regenIntervalMs: 1000, hpRegenPercent: 1, spRegenPercent: 2);

        /// <summary>Levelunterschieds-Tabelle 1:1 wie data/progression.json, aber mit konfigurierbarem
        /// XpToNext (konstant pro Level) und TestMaxLevel statt 25, um gezielt Levelaufstiege
        /// auszulösen/auszuschließen, ohne eine 19-25 Zeilen lange Tabelle von Hand schreiben zu müssen.</summary>
        private static ProgressionCatalog TestProgressionCatalog(long xpPerLevel = 100)
        {
            string xpEntries = string.Join(",", Enumerable.Repeat(xpPerLevel.ToString(), TestMaxLevel - 1));
            string json = @"{ ""maxLevel"": " + TestMaxLevel + @", ""xpToNext"": [" + xpEntries + @"], ""levelDiffPercent"": [
                { ""maxDiff"": -4, ""percent"": 120 }, { ""maxDiff"": -3, ""percent"": 115 }, { ""maxDiff"": -2, ""percent"": 110 },
                { ""maxDiff"": -1, ""percent"": 105 }, { ""maxDiff"": 0, ""percent"": 100 }, { ""maxDiff"": 1, ""percent"": 95 },
                { ""maxDiff"": 2, ""percent"": 90 }, { ""maxDiff"": 3, ""percent"": 80 }, { ""maxDiff"": 4, ""percent"": 70 },
                { ""maxDiff"": 5, ""percent"": 60 }, { ""maxDiff"": 6, ""percent"": 45 }, { ""maxDiff"": 7, ""percent"": 30 },
                { ""maxDiff"": 8, ""percent"": 20 }, { ""maxDiff"": 9, ""percent"": 15 }, { ""maxDiff"": 999999, ""percent"": 10 }
            ] }";
            return ProgressionCatalog.LoadFromJson(json);
        }

        private static World NewWorld(long xpPerLevel = 100, Random? random = null) =>
            new World(maxPlayers: 10, random: random, progressionCatalog: TestProgressionCatalog(xpPerLevel));

        private static Entity JoinWithClass(World world, int characterId, ClassDefinition classDef,
            int level = 1, long xp = 0, int spentSt = 0, int spentHt = 0, int spentDx = 0, int spentIq = 0,
            int unspentStatPoints = 0, int unspentSkillPoints = 0, int? currentHp = null, int? currentSp = null,
            int? x = null, int? y = null)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _,
                classDef: classDef, level: level, xp: xp,
                spentSt: spentSt, spentHt: spentHt, spentDx: spentDx, spentIq: spentIq,
                unspentStatPoints: unspentStatPoints, unspentSkillPoints: unspentSkillPoints,
                currentHp: currentHp, currentSp: currentSp);
            return entity!;
        }

        private static Entity SpawnDummyMonster(World world, int x, int y,
            int maxHp = 30, int attack = 5, int defense = 0, int meleeRangeWire = 150, int attackCooldownMs = 800) =>
            world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                maxHp, attack, defense, meleeRangeWire: meleeRangeWire, attackCooldownMs: attackCooldownMs);

        // ---- Formeln ----

        [Fact]
        public void RecomputeDerivedStats_Level1Krieger_MatchesPreM6aValues()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());

            Assert.Equal(100, player.MaxHp);
            Assert.Equal(15, player.Attack);
            Assert.Equal(5, player.Defense);
            Assert.Equal(30, player.MaxSp);
            Assert.Equal(150, player.DodgeBp); // 3 * 50
        }

        [Fact]
        public void RecomputeDerivedStats_HigherLevelAndStats_AppliesFormulas()
        {
            World world = NewWorld();
            // St=6+2=8, Ht=4+4=8, Dx=3+5=8, Iq=3+1=4, Level=3.
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), level: 3, spentSt: 2, spentHt: 4, spentDx: 5, spentIq: 1);

            Assert.Equal(68 + 8 * 8 + 2 * 10, player.MaxHp); // 152
            Assert.Equal(12 + 4 * 6 + 2 * 4, player.MaxSp);  // 44
            Assert.Equal(3 + 8 * 2, player.Attack);          // 19
            Assert.Equal(3 + 8 / 2, player.Defense);         // 7
            Assert.Equal(8 * 50, player.DodgeBp);            // 400
        }

        [Fact]
        public void RecomputeDerivedStats_DefenseFormula_TruncatesIntegerDivision()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), spentHt: 1); // Ht=5, 5/2=2 (nicht 2.5)

            Assert.Equal(3 + 2, player.Defense);
        }

        [Fact]
        public void RecomputeDerivedStats_DodgeCap_Applies()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), spentDx: 100); // Dx=103, *50=5150 > Cap 4000

            Assert.Equal(4000, player.DodgeBp);
        }

        // ---- XP ----

        [Theory]
        [InlineData(-100, 120)]
        [InlineData(-4, 120)]
        [InlineData(-3, 115)]
        [InlineData(-2, 110)]
        [InlineData(-1, 105)]
        [InlineData(0, 100)]
        [InlineData(1, 95)]
        [InlineData(2, 90)]
        [InlineData(3, 80)]
        [InlineData(4, 70)]
        [InlineData(5, 60)]
        [InlineData(6, 45)]
        [InlineData(7, 30)]
        [InlineData(8, 20)]
        [InlineData(9, 15)]
        [InlineData(10, 10)]
        [InlineData(100, 10)]
        public void AwardXp_LevelDiffTable_AppliesExpectedPercent(int levelDiff, int expectedPercent)
        {
            World world = NewWorld(xpPerLevel: 999_999); // verhindert Levelaufstiege, die den XP-Betrag verschleiern.
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), level: 10);

            (long xpGained, bool leveledUp) = world.AwardXp(player, monsterLevel: player.Level - levelDiff, monsterBaseXp: 100);

            Assert.Equal(Math.Max(1, 100 * expectedPercent / 100), xpGained);
            Assert.False(leveledUp);
        }

        [Fact]
        public void AwardXp_TinyBaseXpWithLowPercent_GrantsAtLeastOne()
        {
            World world = NewWorld(xpPerLevel: 999_999);
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), level: 19);

            // Levelunterschied >= 10 -> 10 Prozent von BaseXp=1 wuerde abgerundet 0 ergeben, mindestens 1 gilt trotzdem.
            (long xpGained, _) = world.AwardXp(player, monsterLevel: player.Level - 20, monsterBaseXp: 1);

            Assert.Equal(1, xpGained);
        }

        [Fact]
        public void AwardXp_LargeGain_AppliesMultipleLevelUpsInOneCall()
        {
            World world = NewWorld(xpPerLevel: 10);
            Entity player = JoinWithClass(world, 1, KriegerClassDef());

            (long xpGained, bool leveledUp) = world.AwardXp(player, monsterLevel: player.Level, monsterBaseXp: 35);

            Assert.Equal(35, xpGained);
            Assert.True(leveledUp);
            Assert.Equal(4, player.Level); // 35 XP bei konstant 10 XP/Level: 3 Aufstiege (10+10+10), Rest 5.
            Assert.Equal(5, player.Xp);
            Assert.Equal(9, player.UnspentStatPoints);  // 3 Level * StatPointsPerLevel 3.
            Assert.Equal(3, player.UnspentSkillPoints); // 3 Level * SkillPointsPerLevel 1.
            Assert.Equal(player.MaxHp, player.CurrentHp);
            Assert.Equal(player.MaxSp, player.CurrentSp);
        }

        [Fact]
        public void AwardXp_AtMaxLevel_GrantsNoXp()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), level: TestMaxLevel);

            (long xpGained, bool leveledUp) = world.AwardXp(player, monsterLevel: TestMaxLevel, monsterBaseXp: 1000);

            Assert.Equal(0, xpGained);
            Assert.False(leveledUp);
            Assert.Equal(0, player.Xp);
        }

        [Fact]
        public void AwardXp_WithoutProgressionCatalog_IsNoOp()
        {
            var world = new World(maxPlayers: 10); // kein ProgressionCatalog.
            Entity player = JoinWithClass(world, 1, KriegerClassDef());

            (long xpGained, bool leveledUp) = world.AwardXp(player, monsterLevel: 1, monsterBaseXp: 100);

            Assert.Equal(0, xpGained);
            Assert.False(leveledUp);
        }

        // ---- Punkteverteilung ----

        [Fact]
        public void TryAllocateStatPoint_NoUnspentPoints_IsRejected()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());
            int stBefore = player.St;

            bool success = world.TryAllocateStatPoint(player.Id, StatKind.St);

            Assert.False(success);
            Assert.Equal(stBefore, player.St);
            Assert.Equal(0, player.SpentSt);
        }

        [Fact]
        public void TryAllocateStatPoint_WithPoints_IncreasesStatAndDecreasesUnspent()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), unspentStatPoints: 2);

            bool success = world.TryAllocateStatPoint(player.Id, StatKind.Dx);

            Assert.True(success);
            Assert.Equal(1, player.SpentDx);
            Assert.Equal(1, player.UnspentStatPoints);
        }

        [Fact]
        public void TryAllocateStatPoint_HtPoint_RaisesCurrentHpBySameDifferenceAsMaxHp()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), unspentStatPoints: 1);
            player.CurrentHp -= 10; // simulierter Vorschaden.
            int maxHpBefore = player.MaxHp;
            int currentHpBefore = player.CurrentHp;

            world.TryAllocateStatPoint(player.Id, StatKind.Ht);

            int maxHpDiff = player.MaxHp - maxHpBefore;
            Assert.True(maxHpDiff > 0);
            Assert.Equal(currentHpBefore + maxHpDiff, player.CurrentHp);
        }

        [Fact]
        public void TryAllocateStatPoint_IqPoint_RaisesCurrentSpBySameDifferenceAsMaxSp()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), unspentStatPoints: 1);
            player.CurrentSp -= 5;
            int maxSpBefore = player.MaxSp;
            int currentSpBefore = player.CurrentSp;

            world.TryAllocateStatPoint(player.Id, StatKind.Iq);

            int maxSpDiff = player.MaxSp - maxSpBefore;
            Assert.True(maxSpDiff > 0);
            Assert.Equal(currentSpBefore + maxSpDiff, player.CurrentSp);
        }

        [Fact]
        public void TryAllocateStatPoint_StPoint_DoesNotChangeCurrentHp()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), unspentStatPoints: 1);
            int currentHpBefore = player.CurrentHp;

            world.TryAllocateStatPoint(player.Id, StatKind.St);

            Assert.Equal(currentHpBefore, player.CurrentHp);
        }

        // ---- Ausweichen ----

        [Fact]
        public void TryAttack_TargetDodgeBp10000_AlwaysDodges_NoDamageCooldownConsumedTargetAcquired()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), x: 50000, y: 50000);
            Entity monster = SpawnDummyMonster(world, 50000, 50000);
            monster.DodgeBp = 10000;

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.True(result.Dodged);
            Assert.Equal(0, result.Damage);
            Assert.Equal(30, monster.CurrentHp); // unveraendert.
            Assert.True(player.AttackReadyAtMs > 0); // Cooldown trotzdem verbraucht.
            Assert.Equal(player.Id, monster.TargetEntityId); // reaktive Zielerfassung laeuft trotzdem.
        }

        [Fact]
        public void TryAttack_TargetDodgeBp0_NeverDodges()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), x: 50000, y: 50000);
            Entity monster = SpawnDummyMonster(world, 50000, 50000); // DodgeBp bleibt 0 (Default, kein ClassDef).

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.False(result.Dodged);
            Assert.True(result.Damage > 0);
        }

        // ---- Regeneration ----

        [Fact]
        public void Tick_WithinRegenDelayAfterCombat_DoesNotRegenerate()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());
            player.CurrentHp = player.MaxHp - 20;
            player.LastCombatAtMs = 1000;

            (_, _, _, _, _, System.Collections.Generic.List<Entity> regenerated) = world.Tick(0.0, nowMs: 1000 + 4999); // < RegenDelayMs 5000

            Assert.Empty(regenerated);
            Assert.Equal(player.MaxHp - 20, player.CurrentHp);
        }

        [Fact]
        public void Tick_AfterRegenDelay_RegeneratesOnceThenWaitsForNextInterval()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());
            int startHp = player.MaxHp - 3;
            player.CurrentHp = startHp;
            player.LastCombatAtMs = 0;

            (_, _, _, _, _, System.Collections.Generic.List<Entity> regenerated) = world.Tick(0.0, nowMs: 5000); // >= RegenDelayMs

            Assert.Contains(player, regenerated);
            Assert.True(player.CurrentHp > startHp);
            Assert.True(player.CurrentHp <= player.MaxHp);

            int hpAfterFirst = player.CurrentHp;
            (_, _, _, _, _, System.Collections.Generic.List<Entity> regenerated2) = world.Tick(0.0, nowMs: 5500); // < RegenIntervalMs seit letztem Regen.

            Assert.DoesNotContain(player, regenerated2);
            Assert.Equal(hpAfterFirst, player.CurrentHp);
        }

        [Fact]
        public void Tick_AlreadyFullHp_NeverExceedsMaxAndIsNotReported()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());
            player.LastCombatAtMs = 0;

            (_, _, _, _, _, System.Collections.Generic.List<Entity> regenerated) = world.Tick(0.0, nowMs: 5000);

            Assert.DoesNotContain(player, regenerated);
            Assert.Equal(player.MaxHp, player.CurrentHp);
        }

        [Fact]
        public void Tick_DeadPlayer_DoesNotRegenerate()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef());
            player.CurrentHp = 0;
            player.LastCombatAtMs = 0;

            (_, _, _, _, _, System.Collections.Generic.List<Entity> regenerated) = world.Tick(0.0, nowMs: 5000);

            Assert.DoesNotContain(player, regenerated);
            Assert.Equal(0, player.CurrentHp);
        }

        // ---- Login-Regeln ----

        [Fact]
        public void TryJoin_StoredHpAndSp_JoinsWithStoredValues()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), currentHp: 82, currentSp: 20);

            Assert.Equal(82, player.CurrentHp);
            Assert.Equal(20, player.CurrentSp);
        }

        [Fact]
        public void TryJoin_NullHpAndSp_JoinsFull()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), currentHp: null, currentSp: null);

            Assert.Equal(player.MaxHp, player.CurrentHp);
            Assert.Equal(player.MaxSp, player.CurrentSp);
        }

        [Fact]
        public void TryJoin_HpZeroOrBelow_RevivesFullAtMapCenter()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), currentHp: 0, currentSp: 5, x: 12345, y: 6789);

            Assert.Equal(player.MaxHp, player.CurrentHp);
            Assert.Equal(player.MaxSp, player.CurrentSp);
            Assert.Equal(GameConstants.MapWidth / 2, player.IntX);
            Assert.Equal(GameConstants.MapHeight / 2, player.IntY);
        }

        [Fact]
        public void TryJoin_HpAboveMax_IsClampedToMax()
        {
            World world = NewWorld();
            Entity player = JoinWithClass(world, 1, KriegerClassDef(), currentHp: 99999, currentSp: 99999);

            Assert.Equal(player.MaxHp, player.CurrentHp);
            Assert.Equal(player.MaxSp, player.CurrentSp);
        }
    }
}
