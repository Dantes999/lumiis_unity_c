using System.Collections.Generic;
using Lumiis.Client;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Client
{
    /// <summary>Testet ClientWorld isoliert (kein Netzwerk, kein Unity) - insbesondere den vom User beobachteten
    /// Fall: ein neuer Spieler joint NACH dem eigenen Join, EntitySpawn/EntityMove kommen also verzögert an.</summary>
    public class ClientWorldTests
    {
        private static readonly Appearance SomeAppearance = new Appearance { BodyId = 1, ArmorId = 2 };

        [Fact]
        public void EntitySpawn_ArrivingAfterOwnJoin_CreatesAndMovesNewEntity()
        {
            var world = new ClientWorld();
            var spawnedIds = new List<int>();
            world.EntitySpawned += e => spawnedIds.Add(e.Id);

            // 1) Eigener Join zuerst (wie beim echten Client).
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);
            Assert.Equal(new[] { 1 }, spawnedIds);
            Assert.True(world.Entities.ContainsKey(1));

            // Etwas Zeit vergeht, in der nichts weiter passiert (entspricht mehreren Frames/Ticks).
            world.Tick(currentTime: 1.0);

            // 2) Ein neuer Spieler joint SPÄTER - der Server schickt dafür ein EntitySpawn an bereits verbundene Clients.
            world.HandleEntitySpawn(entityId: 2, name: "Bot1", x: 40000, y: 40000, appearance: SomeAppearance);

            Assert.Equal(new[] { 1, 2 }, spawnedIds);
            Assert.True(world.Entities.ContainsKey(2));
            Assert.Equal("Bot1", world.Entities[2].Name);
            Assert.Equal(40000, world.Entities[2].IntX);

            // 3) Direkt danach bewegt sich der neue Spieler.
            world.HandleEntityMove(entityId: 2, startX: 40000, startY: 40000, targetX: 40500, targetY: 40000, speed: 500, receivedAtTime: 1.0);
            Assert.True(world.Entities[2].IsMoving);

            world.Tick(currentTime: 2.0); // 1 s später bei Speed 500 -> Distanz 500 -> exakt am Ziel
            Assert.Equal(40500, world.Entities[2].IntX);
            Assert.False(world.Entities[2].IsMoving);
        }

        [Fact]
        public void EntitySpawn_MultipleLaterJoiners_AllCreated()
        {
            var world = new ClientWorld();
            var spawnedIds = new List<int>();
            world.EntitySpawned += e => spawnedIds.Add(e.Id);

            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);
            world.Tick(currentTime: 1.0);

            world.HandleEntitySpawn(entityId: 2, name: "Bot1", x: 10000, y: 10000, appearance: SomeAppearance);
            world.Tick(currentTime: 2.0);
            world.HandleEntitySpawn(entityId: 3, name: "Bot2", x: 20000, y: 20000, appearance: SomeAppearance);
            world.Tick(currentTime: 3.0);
            world.HandleEntitySpawn(entityId: 4, name: "Bot3", x: 30000, y: 30000, appearance: SomeAppearance);

            Assert.Equal(new[] { 1, 2, 3, 4 }, spawnedIds);
            Assert.Equal(4, world.Entities.Count);
        }

        [Fact]
        public void HandleJoinAccepted_AndHandleEntitySpawn_ApplySameAppearance()
        {
            // Beide Pfade (eigene Figur über JoinAccepted, andere über EntitySpawn) laufen durch denselben
            // ApplyAppearance-Code - hier verifiziert über das sichtbare Ergebnis (kein zweiter Codepfad).
            var world = new ClientWorld();
            var appearance = new Appearance { BodyId = 1, HairStyleId = 2, HairColorId = 3, ArmorId = 4, HelmetId = 0, WeaponId = 1, ShieldId = 0 };

            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: appearance);
            world.HandleEntitySpawn(entityId: 2, name: "Other", x: 0, y: 0, appearance: appearance);

            Assert.Equal(appearance, world.Entities[1].Appearance);
            Assert.Equal(appearance, world.Entities[2].Appearance);
        }

        [Fact]
        public void HandleEntityMove_UpdatesFacingFromMovementDelta()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);

            world.HandleEntityMove(entityId: 1, startX: 0, startY: 0, targetX: 1000, targetY: 0, speed: 500, receivedAtTime: 0.0);

            Assert.Equal(FacingDirection.Side, world.Entities[1].Facing.Direction);
            Assert.False(world.Entities[1].Facing.FlipX);
        }
    }
}
