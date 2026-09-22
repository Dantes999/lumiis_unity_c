using System.Collections.Generic;
using Lumiis.Server;
using Lumiis.Server.Game;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Reine Entscheidungslogik (GameServer.SelectDueForSave), kein GameServer, keine DB, kein
    /// Netzwerk nötig - siehe M2b-3.</summary>
    public class PositionSaveDecisionTests
    {
        private const long IntervalMs = 30_000;

        private static Entity MakeEntity(int id, long lastSaveAtMs)
        {
            var entity = new Entity(id, characterId: id, "Bot" + id, 0, 0) { LastSaveAtMs = lastSaveAtMs };
            return entity;
        }

        [Fact]
        public void SelectDueForSave_JustBelowInterval_IsNotDue()
        {
            Entity entity = MakeEntity(1, lastSaveAtMs: 0);

            List<Entity> due = GameServer.SelectDueForSave(new[] { entity }, nowMs: IntervalMs - 1, IntervalMs);

            Assert.Empty(due);
        }

        [Fact]
        public void SelectDueForSave_ExactlyAtInterval_IsDue()
        {
            Entity entity = MakeEntity(1, lastSaveAtMs: 0);

            List<Entity> due = GameServer.SelectDueForSave(new[] { entity }, nowMs: IntervalMs, IntervalMs);

            Assert.Contains(entity, due);
        }

        [Fact]
        public void SelectDueForSave_WellPastInterval_IsDue()
        {
            Entity entity = MakeEntity(1, lastSaveAtMs: 0);

            List<Entity> due = GameServer.SelectDueForSave(new[] { entity }, nowMs: IntervalMs * 3, IntervalMs);

            Assert.Contains(entity, due);
        }

        [Fact]
        public void SelectDueForSave_MixOfDueAndNotDue_ReturnsOnlyDue()
        {
            Entity dueEntity = MakeEntity(1, lastSaveAtMs: 0);
            Entity freshEntity = MakeEntity(2, lastSaveAtMs: IntervalMs - 1);

            List<Entity> due = GameServer.SelectDueForSave(new[] { dueEntity, freshEntity }, nowMs: IntervalMs, IntervalMs);

            Assert.Contains(dueEntity, due);
            Assert.DoesNotContain(freshEntity, due);
        }

        [Fact]
        public void SelectDueForSave_NoEntities_ReturnsEmpty()
        {
            List<Entity> due = GameServer.SelectDueForSave(System.Array.Empty<Entity>(), nowMs: 1_000_000, IntervalMs);

            Assert.Empty(due);
        }
    }
}
