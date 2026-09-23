using System;
using System.Text;
using Lumiis.Data.Maps;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldTests
    {
        /// <summary>Baut eine synthetische 1000x1000-Zellen-CollisionMap (deckt GameConstants.MapWidth/Height
        /// exakt ab, siehe M3a-collision.md) direkt aus Bytes - kein echtes File nötig.</summary>
        private static CollisionMap BuildCollisionMap(params (int X, int Y)[] blockedCells)
        {
            const int width = 1000;
            const int height = 1000;
            int cellCount = width * height;
            byte[] packed = new byte[(cellCount + 7) / 8];
            foreach ((int x, int y) in blockedCells)
            {
                int index = y * width + x;
                packed[index / 8] |= (byte)(1 << (index % 8));
            }

            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);

            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, width, height);
        }

        /// <summary>Wie BuildCollisionMap, aber ausnahmslos jede Zelle blockiert - für Wander-KI-Tests, bei
        /// denen der zufällig gezogene Zielpunkt garantiert (unabhängig vom Zufallswert) blockiert sein soll.</summary>
        private static CollisionMap BuildFullyBlockedCollisionMap()
        {
            const int width = 1000;
            const int height = 1000;
            int cellCount = width * height;
            byte[] packed = new byte[(cellCount + 7) / 8];
            for (int i = 0; i < packed.Length; i++) packed[i] = 0xFF;

            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);

            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, width, height);
        }

        [Fact]
        public void TryJoin_NullPosition_SpawnsAtMapCenter()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin(characterId: 1, "Bot", x: null, y: null, out Entity? entity, out _);

            Assert.True(ok);
            Assert.NotNull(entity);
            Assert.Equal(GameConstants.MapWidth / 2, entity!.IntX);
            Assert.Equal(GameConstants.MapHeight / 2, entity.IntY);
        }

        [Fact]
        public void TryJoin_GivenPosition_SpawnsThere()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin(characterId: 1, "Bot", x: 12345, y: 6789, out Entity? entity, out _);

            Assert.True(ok);
            Assert.Equal(12345, entity!.IntX);
            Assert.Equal(6789, entity.IntY);
        }

        [Fact]
        public void TryJoin_PositionOutsideMap_IsClamped()
        {
            var world = new World(maxPlayers: 10);

            world.TryJoin(characterId: 1, "Bot", x: -5000, y: GameConstants.MapHeight + 5000, out Entity? entity, out _);

            Assert.Equal(0, entity!.IntX);
            Assert.Equal(GameConstants.MapHeight, entity.IntY);
        }

        [Fact]
        public void TryJoin_SameCharacterIdTwice_IsRejectedAsAlreadyOnline()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.CharacterAlreadyOnline, reason);
        }

        [Fact]
        public void TryJoin_DifferentCharacterIdsSameName_BothSucceed()
        {
            // World prüft Namens-Eindeutigkeit nicht mehr selbst (seit M2b0 case-insensitiv global per
            // DB-Unique-Index sichergestellt, siehe LumiisDbContext) - zwei verschiedene CharacterIds mit
            // demselben Namen könnten in der Praxis nicht existieren, World selbst hindert es aber nicht.
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 2, "Bot", null, null, out Entity? entity, out _);

            Assert.True(ok);
            Assert.NotNull(entity);
        }

        [Fact]
        public void TryJoin_ServerFull_IsRejected()
        {
            var world = new World(maxPlayers: 1);
            world.TryJoin(characterId: 1, "First", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 2, "Second", null, null, out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.ServerFull, reason);
        }

        [Fact]
        public void Remove_FreesCharacterIdForRejoin()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);

            bool removed = world.Remove(entity!.Id);
            bool rejoined = world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

            Assert.True(removed);
            Assert.True(rejoined);
        }

        [Fact]
        public void Remove_UnknownId_ReturnsFalse()
        {
            var world = new World(maxPlayers: 10);
            Assert.False(world.Remove(999));
        }

        [Fact]
        public void SetDestination_ClampsToMapBounds()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);

            world.SetDestination(entity!.Id, -5000, GameConstants.MapHeight + 5000);

            Assert.Equal(0, entity.TargetX);
            Assert.Equal(GameConstants.MapHeight, entity.TargetY);
        }

        [Fact]
        public void SetDestination_UnknownId_ReturnsFalse()
        {
            var world = new World(maxPlayers: 10);
            Assert.False(world.SetDestination(999, 0, 0));
        }

        [Fact]
        public void Tick_ReturnsArrivedEntities()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);
            world.SetDestination(entity!.Id, entity.IntX + 100, entity.IntY);

            (var arrived, _) = world.Tick(10.0, nowMs: 0); // genug Zeit, um definitiv anzukommen

            Assert.Contains(entity, arrived);
            Assert.False(entity.IsMoving);
        }

        // ---- M3a: Kollision (siehe docs/tasks/M3a-collision.md Abschnitt 3) ----

        [Fact]
        public void SetDestination_WithOpenCollisionMap_FreeMovement_Unchanged()
        {
            var world = new World(maxPlayers: 10, BuildCollisionMap());
            world.TryJoin(characterId: 1, "Bot", x: 10000, y: 10000, out Entity? entity, out _);

            world.SetDestination(entity!.Id, 20000, 15000);

            Assert.Equal(20000, entity.TargetX);
            Assert.Equal(15000, entity.TargetY);
        }

        [Fact]
        public void SetDestination_DirectlyIntoWall_StopsExactlyBefore()
        {
            // Senkrechte Wand bei Zelle x=10 (y=0..19); Spieler bei Zelle (5,10), Ziel weit dahinter bei
            // Zelle (15,10) - die Bewegung darf Zelle 10 nie erreichen.
            var blocked = new System.Collections.Generic.List<(int, int)>();
            for (int y = 0; y <= 19; y++) blocked.Add((10, y));
            var world = new World(maxPlayers: 10, BuildCollisionMap(blocked.ToArray()));
            world.TryJoin(characterId: 1, "Bot", x: 550, y: 1050, out Entity? entity, out _); // Zelle (5,10)

            world.SetDestination(entity!.Id, 1550, 1050); // Zelle (15,10), jenseits der Wand

            Assert.Equal(950, entity.TargetX); // Zellenmitte von Zelle 9 (letzte begehbare vor der Wand)
            Assert.Equal(1050, entity.TargetY);
        }

        [Fact]
        public void SetDestination_ParallelToWall_Unaffected()
        {
            // Dieselbe Wand wie oben, aber die Bewegung verläuft komplett links davon (Zelle x=5 konstant) -
            // darf von der Wand bei x=10 nicht beeinflusst werden.
            var blocked = new System.Collections.Generic.List<(int, int)>();
            for (int y = 0; y <= 19; y++) blocked.Add((10, y));
            var world = new World(maxPlayers: 10, BuildCollisionMap(blocked.ToArray()));
            world.TryJoin(characterId: 1, "Bot", x: 550, y: 100, out Entity? entity, out _); // Zelle (5,1)

            world.SetDestination(entity!.Id, 550, 1900); // Zelle (5,19), Ziel exakt wie angefragt erreichbar

            Assert.Equal(550, entity.TargetX);
            Assert.Equal(1900, entity.TargetY);
        }

        [Fact]
        public void SetDestination_TargetBeyondMapBorder_ClampedFirstThenCheckedForCollision()
        {
            // Offene Karte, ein Zelle größer als GameConstants.MapWidth/Height/100 (1001 statt 1000): Math.Clamp
            // erlaubt als Zielwert exakt GameConstants.MapWidth (100000), das entspricht Zelle 1000 - bei einer
            // genau 1000 Zellen breiten Karte wäre das bereits aus deren Sicht "außerhalb" (siehe
            // CollisionMap.IsBlocked) und würde fälschlich wie eine Kollision aussehen. Das ist eine enge,
            // vorbestehende Eigenheit des inklusiven Clamps (nicht Gegenstand dieser Aufgabe) - hier bewusst
            // umgangen, um wirklich nur "wird zuerst geklemmt, dann auf Kollision geprüft" zu testen, nicht
            // diese unabhängige Grenzwert-Eigenheit. Mit der echten (1000x1000, Rand bemalt blockiert)
            // village-Karte ist das ohnehin unsichtbar, weil die Randzeile/-spalte sowieso blockiert ist.
            const int width = 1001;
            const int height = 1001;
            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);
            byte[] packed = new byte[(width * height + 7) / 8];
            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            CollisionMap openMap = CollisionMap.LoadFromBytes(bytes, width, height);

            var world = new World(maxPlayers: 10, openMap);
            world.TryJoin(characterId: 1, "Bot", x: 10000, y: 10000, out Entity? entity, out _);

            world.SetDestination(entity!.Id, GameConstants.MapWidth * 10, GameConstants.MapHeight * 10);

            Assert.Equal(GameConstants.MapWidth, entity.TargetX);
            Assert.Equal(GameConstants.MapHeight, entity.TargetY);
        }

        [Fact]
        public void SetDestination_StartCellBlocked_NoMovementTriggered()
        {
            // Sollte nicht vorkommen (siehe World.SetDestination-Kommentar), aber abgesichert: steht die
            // Entity schon auf einer blockierten Zelle, löst SetDestination trotzdem keine Bewegung aus.
            var world = new World(maxPlayers: 10, BuildCollisionMap((5, 5)));
            world.TryJoin(characterId: 1, "Bot", x: 550, y: 550, out Entity? entity, out _); // Zelle (5,5), blockiert

            bool ok = world.SetDestination(entity!.Id, 900, 900);

            Assert.True(ok); // gültiger Aufruf (Entity existiert), nur keine Bewegung ausgelöst.
            Assert.False(entity.IsMoving);
        }

        // ---- M3b: Sichtbarkeitsbereich (AOI, siehe docs/tasks/M3b-aoi.md Abschnitt 5) ----

        [Fact]
        public void ComputeVisibility_WithinRadius_SeeEachOther()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "A", x: 50000, y: 50000, out Entity? a, out _);
            world.TryJoin(characterId: 2, "B", x: 50000, y: 50500, out Entity? b, out _); // 500 < AoiRadiusWire

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.Contains(b!.Id, visibility[a!.Id]);
            Assert.Contains(a.Id, visibility[b.Id]);
        }

        [Fact]
        public void ComputeVisibility_OutsideRadius_DoNotSeeEachOther()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "A", x: 50000, y: 50000, out Entity? a, out _);
            world.TryJoin(characterId: 2, "B", x: 50000, y: 50000 + GameConstants.AoiRadiusWire + 1, out Entity? b, out _);

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.DoesNotContain(b!.Id, visibility[a!.Id]);
            Assert.DoesNotContain(a.Id, visibility[b.Id]);
        }

        [Fact]
        public void ComputeVisibility_ExactlyAtRadius_IsInclusive()
        {
            // Grenzfall bewusst inklusiv (siehe World.ComputeVisibility-Kommentar).
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "A", x: 50000, y: 50000, out Entity? a, out _);
            world.TryJoin(characterId: 2, "B", x: 50000, y: 50000 + GameConstants.AoiRadiusWire, out Entity? b, out _);

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.Contains(b!.Id, visibility[a!.Id]);
        }

        [Fact]
        public void ComputeVisibility_IsSymmetric()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "A", x: 40000, y: 40000, out Entity? a, out _);
            world.TryJoin(characterId: 2, "B", x: 40500, y: 40500, out Entity? b, out _);
            world.TryJoin(characterId: 3, "C", x: 90000, y: 90000, out Entity? c, out _);

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.Equal(visibility[a!.Id].Contains(b!.Id), visibility[b.Id].Contains(a.Id));
            Assert.Equal(visibility[a.Id].Contains(c!.Id), visibility[c.Id].Contains(a.Id));
        }

        [Fact]
        public void ComputeVisibility_EntityNeverSeesItself()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "A", x: 50000, y: 50000, out Entity? a, out _);

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.DoesNotContain(a!.Id, visibility[a.Id]);
        }

        [Fact]
        public void ComputeVisibility_EmptyWorld_ReturnsEmptyDictionary()
        {
            var world = new World(maxPlayers: 10);

            var visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            Assert.Empty(visibility);
        }

        // ---- M3c: Wander-KI (siehe docs/tasks/M3c-monsters.md Abschnitt 7) ----

        [Fact]
        public void Tick_DueMonster_PicksTargetWithinWanderRadiusOfAnchor()
        {
            // Min==Max: NextWanderAtMs ist deterministisch nowMs+1000, unabhängig vom Zufallswert.
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(42));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0);

            (_, System.Collections.Generic.List<Entity> startedWandering) = world.Tick(0.0, nowMs: 1000);

            Assert.Contains(monster, startedWandering);
            Assert.True(monster.IsMoving);
            Assert.InRange(monster.TargetX, 50000 - 500, 50000 + 500);
            Assert.InRange(monster.TargetY, 50000 - 500, 50000 + 500);
        }

        [Fact]
        public void Tick_DueMonsterWithBlockedTarget_SkipsMovementWithoutCrashing()
        {
            // Jede Zelle blockiert - der zufällig gezogene Zielpunkt ist garantiert blockiert, egal welcher
            // Random-Wert gezogen wird.
            var world = new World(maxPlayers: 10, BuildFullyBlockedCollisionMap(), random: new Random(1));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0);

            (_, System.Collections.Generic.List<Entity> startedWandering) = world.Tick(0.0, nowMs: 1000);

            Assert.DoesNotContain(monster, startedWandering);
            Assert.False(monster.IsMoving);
        }

        [Fact]
        public void Tick_DueMonster_ReschedulesNextWanderWithinConfiguredInterval()
        {
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(7));
            Entity monster = world.SpawnMonster(mobId: 2, "Waldschrat", x: 50000, y: 50000, speedWire: 200,
                wanderRadiusWire: 300, wanderIntervalMinMs: 2000, wanderIntervalMaxMs: 5000, nowMs: 0);
            long dueAt = monster.NextWanderAtMs;

            world.Tick(0.0, nowMs: dueAt);

            Assert.InRange(monster.NextWanderAtMs, dueAt + 2000, dueAt + 5000);
        }

        [Fact]
        public void Tick_MonsterWithBlockedTarget_StillReschedulesNextWander()
        {
            // "kein Retry-Loop" bedeutet: übersprungen wird nur die Bewegung, nicht die nächste Fälligkeit.
            var world = new World(maxPlayers: 10, BuildFullyBlockedCollisionMap(), random: new Random(3));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0);
            long dueAt = monster.NextWanderAtMs;

            world.Tick(0.0, nowMs: dueAt);

            Assert.Equal(dueAt + 1000, monster.NextWanderAtMs);
        }

        [Fact]
        public void Tick_MonsterNotYetDue_DoesNotWander()
        {
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(9));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 5000, wanderIntervalMaxMs: 5000, nowMs: 0);

            (_, System.Collections.Generic.List<Entity> startedWandering) = world.Tick(0.0, nowMs: 100);

            Assert.Empty(startedWandering);
            Assert.False(monster.IsMoving);
        }
    }
}
