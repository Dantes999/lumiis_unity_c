using System;
using System.Linq;
using Lumiis.Data.Skills;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>M6b: TryUseSkill je Skill-Typ, AllocateSkillPoint, Hast-/Kampfrausch-Integration - kein
    /// Netzwerkbezug (siehe docs/tasks/M6b-skills.md Abschnitt 7). Nutzt eine eigene, kleine, feste
    /// SkillCatalog-Fixture (nicht die echte data/skills.json - deren Validierung/Werte sind bereits Sache
    /// von SkillCatalogTests) mit denselben vier SkillIds/Typen wie die echten Daten: 1=Wuchtschlag
    /// (SingleStrike, 200% Multiplikator), 2=Kreisschlag (AreaAttack, RadiusWire 300, 150% Multiplikator),
    /// 3=Kampfrausch (AttackBuff, DurationMs 5000, 50% Bonus), 4=Hast (AttackSpeedBuff, DurationMs 5000,
    /// 5% Stufe 1 / 20% Stufe 17 - Stufe 17 bewusst exakt 20, wie in der echten Datei, siehe
    /// docs/tasks/M6b-skills.md Abschnitt 1).</summary>
    public class WorldSkillTests
    {
        private const int SingleStrikeSkillId = 1;
        private const int AreaAttackSkillId = 2;
        private const int AttackBuffSkillId = 3;
        private const int AttackSpeedBuffSkillId = 4;
        private const int SkillSpCost = 10;
        private const int SkillCooldownMs = 3000;

        /// <summary>17 identische Stufen-Einträge (SpCost/CooldownMs konstant, EffectValue über
        /// effectForLevel) - die per-Stufe-Validierung selbst ist bereits Sache von SkillCatalogTests.</summary>
        private static string LevelsJson(string effectField, int spCost, int cooldownMs, Func<int, int> effectForLevel)
        {
            return string.Join(",", Enumerable.Range(1, 17).Select(level =>
                $@"{{ ""spCost"": {spCost}, ""cooldownMs"": {cooldownMs}, ""{effectField}"": {effectForLevel(level)} }}"));
        }

        private static SkillCatalog BuildSkillCatalog()
        {
            string json = $@"{{ ""skills"": [
                {{ ""id"": {SingleStrikeSkillId}, ""name"": ""Wuchtschlag"", ""type"": ""SingleStrike"", ""radiusWire"": 0, ""durationMs"": 0,
                   ""levels"": [ {LevelsJson("damageMultiplierPercent", SkillSpCost, SkillCooldownMs, _ => 200)} ] }},
                {{ ""id"": {AreaAttackSkillId}, ""name"": ""Kreisschlag"", ""type"": ""AreaAttack"", ""radiusWire"": 300, ""durationMs"": 0,
                   ""levels"": [ {LevelsJson("damageMultiplierPercent", SkillSpCost, SkillCooldownMs, _ => 150)} ] }},
                {{ ""id"": {AttackBuffSkillId}, ""name"": ""Kampfrausch"", ""type"": ""AttackBuff"", ""radiusWire"": 0, ""durationMs"": 5000,
                   ""levels"": [ {LevelsJson("attackBonusPercent", SkillSpCost, SkillCooldownMs, _ => 50)} ] }},
                {{ ""id"": {AttackSpeedBuffSkillId}, ""name"": ""Hast"", ""type"": ""AttackSpeedBuff"", ""radiusWire"": 0, ""durationMs"": 5000,
                   ""levels"": [ {LevelsJson("cooldownReductionPercent", SkillSpCost, SkillCooldownMs, level => level == 17 ? 20 : 5)} ] }}
            ] }}";
            return SkillCatalog.LoadFromJson(json);
        }

        private static World NewWorld(Random? random = null) =>
            new World(maxPlayers: 10, random: random, skillCatalog: BuildSkillCatalog());

        private static Entity JoinPlayer(World world, int characterId, int x, int y,
            int attack = 20, int defense = 5, int meleeRangeWire = 150, int attackCooldownMs = 1000, int currentSp = 100)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _,
                maxHp: 100, attack: attack, defense: defense, meleeRangeWire: meleeRangeWire, attackCooldownMs: attackCooldownMs);
            entity!.MaxSp = Math.Max(entity.MaxSp, currentSp);
            entity.CurrentSp = currentSp;
            return entity;
        }

        private static Entity SpawnMonster(World world, int x, int y, int maxHp = 100, int defense = 0, int dodgeBp = 0)
        {
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                maxHp: maxHp, attack: 5, defense: defense);
            monster.DodgeBp = dodgeBp;
            return monster;
        }

        // ---- Gemeinsame Vorprüfungen ----

        [Fact]
        public void TryUseSkill_NotLearned_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);

            SkillUseResult result = world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.NotLearned, result.Reason);
        }

        [Fact]
        public void TryUseSkill_NotEnoughSp_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, currentSp: 0);
            player.SkillLevels[AttackBuffSkillId] = 1;

            SkillUseResult result = world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.NotEnoughSp, result.Reason);
        }

        [Fact]
        public void TryUseSkill_OwnCooldownNotReady_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);
            player.SkillLevels[AttackBuffSkillId] = 1;

            world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);
            SkillUseResult result = world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: SkillCooldownMs - 1);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.OnCooldown, result.Reason);
        }

        [Fact]
        public void TryUseSkill_FourSkillCooldowns_AreIndependent()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, currentSp: 1000);
            player.SkillLevels[SingleStrikeSkillId] = 1;
            player.SkillLevels[AreaAttackSkillId] = 1;
            player.SkillLevels[AttackBuffSkillId] = 1;
            player.SkillLevels[AttackSpeedBuffSkillId] = 1;
            Entity monster = SpawnMonster(world, 50000, 50000);

            SkillUseResult first = world.TryUseSkill(player.Id, SingleStrikeSkillId, monster.Id, nowMs: 0);
            // Wuchtschlag geht sofort in den eigenen Cooldown - Kreisschlag/Kampfrausch/Hast sind davon
            // unberührt, da jeder Skill seinen eigenen Eintrag in Entity.SkillCooldownReadyAtMs hat.
            SkillUseResult second = world.TryUseSkill(player.Id, AreaAttackSkillId, targetEntityId: 0, nowMs: 0);
            SkillUseResult third = world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);
            SkillUseResult fourth = world.TryUseSkill(player.Id, AttackSpeedBuffSkillId, targetEntityId: 0, nowMs: 0);

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.True(third.Success);
            Assert.True(fourth.Success);

            // Ein zweiter Wuchtschlag-Versuch sofort danach ist dagegen weiterhin im eigenen Cooldown.
            SkillUseResult repeat = world.TryUseSkill(player.Id, SingleStrikeSkillId, monster.Id, nowMs: 0);
            Assert.False(repeat.Success);
            Assert.Equal(UseSkillFailedReason.OnCooldown, repeat.Reason);
        }

        // ---- Wuchtschlag (SingleStrike) ----

        [Fact]
        public void TryUseSkill_SingleStrike_DealsDamageWithLevelMultiplier()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attack: 20, defense: 5);
            player.SkillLevels[SingleStrikeSkillId] = 1; // 200% Multiplikator laut Fixture
            Entity monster = SpawnMonster(world, 50000, 50000, maxHp: 100, defense: 5);

            SkillUseResult result = world.TryUseSkill(player.Id, SingleStrikeSkillId, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(SkillType.SingleStrike, result.SkillType);
            Assert.Single(result.Hits);
            AttackResult hit = result.Hits[0].Result;
            Assert.Equal(30, hit.Damage); // (20-5) * 200% = 30, statt 15 beim normalen Angriff
            Assert.Equal(SkillSpCost, 100 - player.CurrentSp); // SP wurden abgezogen (Start 100, siehe JoinPlayer-Default)
        }

        [Fact]
        public void TryUseSkill_SingleStrike_NoTarget_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);
            player.SkillLevels[SingleStrikeSkillId] = 1;

            SkillUseResult result = world.TryUseSkill(player.Id, SingleStrikeSkillId, targetEntityId: 0, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.TargetRequired, result.Reason);
        }

        [Fact]
        public void TryUseSkill_SingleStrike_OutOfRange_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, meleeRangeWire: 150);
            player.SkillLevels[SingleStrikeSkillId] = 1;
            Entity monster = SpawnMonster(world, 50000 + 500, 50000);

            SkillUseResult result = world.TryUseSkill(player.Id, SingleStrikeSkillId, monster.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.OutOfRange, result.Reason);
        }

        // ---- Kreisschlag (AreaAttack) ----

        [Fact]
        public void TryUseSkill_AreaAttack_HitsMultipleTargetsAndFiltersOutsideDeadOwnKind()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attack: 20, defense: 5);
            player.SkillLevels[AreaAttackSkillId] = 1; // RadiusWire 300 laut Fixture
            Entity inRange1 = SpawnMonster(world, 50000 + 100, 50000, maxHp: 100, defense: 5);
            Entity inRange2 = SpawnMonster(world, 50000, 50000 + 100, maxHp: 100, defense: 5);
            Entity outOfRange = SpawnMonster(world, 50000 + 1000, 50000, maxHp: 100, defense: 5);
            Entity deadMonster = SpawnMonster(world, 50000 - 100, 50000, maxHp: 100, defense: 5);
            deadMonster.CurrentHp = 0;
            world.TryJoin(2, "OtherPlayer", 50000 + 50, 50000, out Entity? otherPlayer, out _,
                maxHp: 100, attack: 10, defense: 0, meleeRangeWire: 150, attackCooldownMs: 1000);

            SkillUseResult result = world.TryUseSkill(player.Id, AreaAttackSkillId, targetEntityId: 0, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(SkillType.AreaAttack, result.SkillType);
            var hitIds = result.Hits.Select(h => h.TargetEntityId).ToList();
            Assert.Contains(inRange1.Id, hitIds);
            Assert.Contains(inRange2.Id, hitIds);
            Assert.DoesNotContain(outOfRange.Id, hitIds);
            Assert.DoesNotContain(deadMonster.Id, hitIds);
            Assert.DoesNotContain(otherPlayer!.Id, hitIds); // gleiches Kind wie der Nutzer (Spieler) - PvP bleibt ausgeschlossen.
            Assert.Equal(2, result.Hits.Count);
            foreach ((int _, AttackResult hit) in result.Hits)
                Assert.Equal(22, hit.Damage); // (20-5) * 150% = 22.5 -> Math.Round rundet auf gerade (Bankers Rounding) auf 22.
        }

        [Fact]
        public void TryUseSkill_AreaAttack_EachTargetDodgesIndependently()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attack: 20, defense: 5);
            player.SkillLevels[AreaAttackSkillId] = 1;
            Entity alwaysHit = SpawnMonster(world, 50000 + 100, 50000, maxHp: 100, defense: 5, dodgeBp: 0);
            Entity alwaysDodge = SpawnMonster(world, 50000, 50000 + 100, maxHp: 100, defense: 5, dodgeBp: 10000);

            SkillUseResult result = world.TryUseSkill(player.Id, AreaAttackSkillId, targetEntityId: 0, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(2, result.Hits.Count);
            AttackResult hitResult = result.Hits.Single(h => h.TargetEntityId == alwaysHit.Id).Result;
            AttackResult dodgeResult = result.Hits.Single(h => h.TargetEntityId == alwaysDodge.Id).Result;
            Assert.False(hitResult.Dodged);
            Assert.True(dodgeResult.Dodged);
        }

        // ---- Kampfrausch (AttackBuff) ----

        [Fact]
        public void TryUseSkill_AttackBuff_IncreasesAttackImmediatelyAndRevertsAfterExpiry()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attack: 20, defense: 5);
            player.SkillLevels[AttackBuffSkillId] = 1; // 50% Bonus laut Fixture
            int attackBeforeBuff = player.Attack;

            SkillUseResult result = world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(SkillType.AttackBuff, result.SkillType);
            Assert.Equal(BuffType.AttackBuff, result.BuffApplied);
            Assert.Equal(attackBeforeBuff + attackBeforeBuff / 2, player.Attack); // +50%, gerundet

            var (_, _, _, _, _, _, buffsExpired) = world.Tick(deltaSeconds: 0.0, nowMs: 5000); // DurationMs erreicht

            Assert.Contains(player, buffsExpired);
            Assert.Equal(attackBeforeBuff, player.Attack);
        }

        [Fact]
        public void TryUseSkill_AttackBuff_RecastDoesNotStack()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attack: 20, defense: 5, currentSp: 1000);
            player.SkillLevels[AttackBuffSkillId] = 1;

            world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: 0);
            int attackAfterFirstCast = player.Attack;

            world.TryUseSkill(player.Id, AttackBuffSkillId, targetEntityId: 0, nowMs: SkillCooldownMs); // eigener Cooldown abgelaufen

            Assert.Equal(attackAfterFirstCast, player.Attack); // nicht nochmal +50% obendrauf
            Assert.Single(player.ActiveBuffs); // ein Eintrag, kein zweiter parallel aktiver Buff desselben Typs
        }

        // ---- Hast (AttackSpeedBuff) ----

        [Theory]
        [InlineData(1, 5, 950)]   // Stufe 1: 5% Reduktion, 1000 * 95% = 950
        [InlineData(17, 20, 800)] // Stufe 17: EXAKT 20% Reduktion, 1000 * 80% = 800
        public void TryUseSkill_AttackSpeedBuff_ReducesEffectiveAttackCooldown(int hastLevel, int expectedPercent, int expectedCooldownMs)
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attackCooldownMs: 1000, currentSp: 1000);
            player.SkillLevels[AttackSpeedBuffSkillId] = hastLevel;
            Entity monster = SpawnMonster(world, 50000, 50000, maxHp: 1000);

            SkillUseResult buffResult = world.TryUseSkill(player.Id, AttackSpeedBuffSkillId, targetEntityId: 0, nowMs: 0);
            Assert.True(buffResult.Success);
            Assert.Equal(expectedPercent, player.ActiveBuffs[BuffType.AttackSpeedBuff].Strength);

            world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.Equal(expectedCooldownMs, player.AttackReadyAtMs);
        }

        [Fact]
        public void TryUseSkill_AttackSpeedBuff_RevertsToFullCooldownAfterExpiry()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000, attackCooldownMs: 1000, currentSp: 1000);
            player.SkillLevels[AttackSpeedBuffSkillId] = 1;
            Entity monster = SpawnMonster(world, 50000, 50000, maxHp: 1000);

            world.TryUseSkill(player.Id, AttackSpeedBuffSkillId, targetEntityId: 0, nowMs: 0);
            world.Tick(deltaSeconds: 0.0, nowMs: 5000); // DurationMs erreicht - Buff läuft ab.

            world.TryAttack(player.Id, monster.Id, nowMs: 5000);

            Assert.Equal(5000 + 1000, player.AttackReadyAtMs); // wieder voller Cooldown, keine Reduktion mehr.
        }

        // ---- AllocateSkillPoint ----

        [Fact]
        public void TryAllocateSkillPoint_NoUnspentPoints_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);
            player.UnspentSkillPoints = 0;

            bool success = world.TryAllocateSkillPoint(player.Id, AttackBuffSkillId);

            Assert.False(success);
            Assert.Equal(0, player.SkillLevels.GetValueOrDefault(AttackBuffSkillId));
        }

        [Fact]
        public void TryAllocateSkillPoint_AlreadyMaxLevel_Fails()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);
            player.UnspentSkillPoints = 5;
            player.SkillLevels[AttackBuffSkillId] = SkillCatalog.MaxSkillLevel;

            bool success = world.TryAllocateSkillPoint(player.Id, AttackBuffSkillId);

            Assert.False(success);
            Assert.Equal(SkillCatalog.MaxSkillLevel, player.SkillLevels[AttackBuffSkillId]);
            Assert.Equal(5, player.UnspentSkillPoints);
        }

        [Fact]
        public void TryAllocateSkillPoint_ValidRequest_IncreasesLevelAndConsumesPoint()
        {
            World world = NewWorld();
            Entity player = JoinPlayer(world, 1, 50000, 50000);
            player.UnspentSkillPoints = 2;

            bool success = world.TryAllocateSkillPoint(player.Id, AttackBuffSkillId);

            Assert.True(success);
            Assert.Equal(1, player.SkillLevels[AttackBuffSkillId]);
            Assert.Equal(1, player.UnspentSkillPoints);
        }
    }
}
