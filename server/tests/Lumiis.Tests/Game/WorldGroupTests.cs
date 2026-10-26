using System.Collections.Generic;
using System.Linq;
using Lumiis.Server.Game;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Monster-Gruppen (spawns.json groupId): gemeinsame Aggro und gemeinsames Wandern.</summary>
    public class WorldGroupTests
    {
        private const int X = 50000;
        private const int Y = 50000;

        private static Entity JoinPlayer(World world, int attack = 15)
        {
            world.TryJoin(1, "P1", X, Y, out Entity? entity, out _,
                maxHp: 100, attack: attack, defense: 5, meleeRangeWire: 150, attackCooldownMs: 800);
            return entity!;
        }

        private static Entity SpawnMember(World world, int x, int y, int groupId, int? spawnPointId = null, int respawnSeconds = 0)
        {
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700, wanderRadiusWire: 500,
                wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0, 100, attack: 5, defense: 0,
                yangMin: 0, yangMax: 0, spawnPointId: spawnPointId, respawnSeconds: respawnSeconds);
            world.AssignGroup(monster, groupId);
            return monster;
        }

        [Fact]
        public void HittingOneMember_AlertsWholeGroup_EvenFarApart_ButNotOtherGroups()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity hit = SpawnMember(world, X + 120, Y, groupId: 1);
            Entity mateFar = SpawnMember(world, X + 3000, Y, groupId: 1);
            Entity mateBehind = SpawnMember(world, X - 300, Y, groupId: 1);
            Entity otherGroupNear = SpawnMember(world, X + 400, Y + 300, groupId: 2);

            world.TryPlayerSwing(player.Id, hit.Id, 1, 0, nowMs: 0);

            Assert.Equal(player.Id, hit.TargetEntityId);
            Assert.Equal(player.Id, mateFar.TargetEntityId);
            Assert.Equal(player.Id, mateBehind.TargetEntityId);
            Assert.Null(otherGroupNear.TargetEntityId);
        }

        [Fact]
        public void Group_WandersTogether_LeaderMovesAllMembersToSameArea()
        {
            var world = new World(maxPlayers: 10, random: new System.Random(3));
            Entity a = SpawnMember(world, X, Y, groupId: 1);
            Entity b = SpawnMember(world, X + 120, Y, groupId: 1);
            Entity c = SpawnMember(world, X, Y + 120, groupId: 1);
            var members = new[] { a, b, c };

            // Alle drei sind bei 1000 ms fällig (Intervall fix 1000); nur der Anführer entscheidet.
            world.Tick(0.0, nowMs: 1000);
            Assert.True(a.IsMoving);
            // Mitläufer laufen nicht im selben Tick los, sondern versetzt (oder bleiben eine Runde stehen).
            Assert.All(new[] { b, c }, m => Assert.False(m.IsMoving));

            var started = new Dictionary<int, long>();
            for (long t = 1040; t < 2000; t += 40) // vor der nächsten Runde des Anführers
            {
                world.Tick(0.0, nowMs: t);
                foreach (Entity m in members)
                    if (m.IsMoving && !started.ContainsKey(m.Id)) started[m.Id] = t;
            }

            // Wer mitläuft, landet in der Nähe der anderen (lockere Formation, kein starrer Ring).
            foreach (Entity m in members)
            foreach (Entity n in members)
            {
                if (!started.ContainsKey(m.Id) || !started.ContainsKey(n.Id)) continue;
                int mx = m.HasWaypoint ? m.WaypointX : m.TargetX, my = m.HasWaypoint ? m.WaypointY : m.TargetY;
                int nx = n.HasWaypoint ? n.WaypointX : n.TargetX, ny = n.HasWaypoint ? n.WaypointY : n.TargetY;
                long dx = mx - nx, dy = my - ny;
                Assert.True(dx * dx + dy * dy <= 340L * 340L, $"Ziele zu weit auseinander: {m.Id}/{n.Id}");
            }
            Assert.True(started.Count >= 2, "mindestens ein Mitläufer folgt dem Anführer");
        }

        [Fact]
        public void Group_Members_DoNotAllStartOnTheSameTick()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var world = new World(maxPlayers: 10, random: new System.Random(seed));
                Entity a = SpawnMember(world, X, Y, groupId: 1);
                Entity b = SpawnMember(world, X + 120, Y, groupId: 1);
                Entity c = SpawnMember(world, X, Y + 120, groupId: 1);
                world.Tick(0.0, nowMs: 1000);
                Assert.False(b.IsMoving && c.IsMoving, $"Seed {seed}: ganze Gruppe startet gleichzeitig");
            }
        }

        [Fact]
        public void Wandering_IsSlowerThanChasing()
        {
            var world = new World(maxPlayers: 10, random: new System.Random(1));
            Entity a = SpawnMember(world, X, Y, groupId: 0);
            world.Tick(0.0, nowMs: 1000);
            Assert.True(a.IsMoving);
            Assert.True(a.Speed < a.MovementSpeed * 70 / 100, $"Wandertempo {a.Speed} von {a.MovementSpeed}");
        }

        [Fact]
        public void AlertedPack_ReactsWithDelay_AndSurroundsInsteadOfQueueing()
        {
            var world = new World(maxPlayers: 10, random: new System.Random(5));
            Entity player = JoinPlayer(world, attack: 1);
            Entity hit = SpawnMember(world, X + 120, Y, groupId: 1);
            Entity b = SpawnMember(world, X + 650, Y + 60, groupId: 1);
            Entity c = SpawnMember(world, X + 650, Y - 60, groupId: 1);

            world.TryAttack(player.Id, hit.Id, nowMs: 0);
            Assert.Equal(player.Id, b.TargetEntityId);
            world.Tick(0.0, nowMs: 40);
            Assert.False(b.IsMoving && c.IsMoving, "Rudel startet nicht im selben Tick");

            for (long t = 80; t <= 800; t += 40) world.Tick(0.0, nowMs: t);
            var targets = new[] { hit, b, c }.Select(m => (m.TargetX, m.TargetY)).Distinct().Count();
            Assert.Equal(3, targets); // jeder hat seinen eigenen Anlaufpunkt
        }

        [Fact]
        public void RespawnedMember_RejoinsItsGroup()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, attack: 1000);
            Entity victim = SpawnMember(world, X + 120, Y, groupId: 5, spawnPointId: 1, respawnSeconds: 2);
            Entity mate = SpawnMember(world, X + 2000, Y, groupId: 5, spawnPointId: 2, respawnSeconds: 2);

            world.TryPlayerSwing(player.Id, victim.Id, 1, 0, nowMs: 0);
            Assert.DoesNotContain(world.Entities, e => e.Id == victim.Id);
            mate.TargetEntityId = null;

            world.Tick(0.0, nowMs: 2500);
            Entity respawned = world.Entities.Single(e => e.SpawnPointId == 1);
            Assert.Equal(5, respawned.GroupId);
            respawned.TargetEntityId = null;

            // Ein Treffer am Kameraden alarmiert auch das neu erschienene Mitglied.
            world.TryAttack(player.Id, respawned.Id, nowMs: 5000);
            Assert.Equal(player.Id, mate.TargetEntityId);
        }
    }
}
