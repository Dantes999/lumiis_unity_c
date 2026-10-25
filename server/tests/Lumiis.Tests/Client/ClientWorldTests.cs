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
            world.HandleEntitySpawn(entityId: 2, name: "Bot1", kind: EntityKind.Player, typeId: 0, x: 40000, y: 40000, appearance: SomeAppearance);

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

            world.HandleEntitySpawn(entityId: 2, name: "Bot1", kind: EntityKind.Player, typeId: 0, x: 10000, y: 10000, appearance: SomeAppearance);
            world.Tick(currentTime: 2.0);
            world.HandleEntitySpawn(entityId: 3, name: "Bot2", kind: EntityKind.Player, typeId: 0, x: 20000, y: 20000, appearance: SomeAppearance);
            world.Tick(currentTime: 3.0);
            world.HandleEntitySpawn(entityId: 4, name: "Bot3", kind: EntityKind.Player, typeId: 0, x: 30000, y: 30000, appearance: SomeAppearance);

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
            world.HandleEntitySpawn(entityId: 2, name: "Other", kind: EntityKind.Player, typeId: 0, x: 0, y: 0, appearance: appearance);

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

        // ---- Bugfix nach M5a: HandleEntityDamaged/HandleEntityHpChanged teilen sich denselben Schreibpfad
        // (ApplyHp) - hier über das sichtbare Ergebnis verifiziert, kein zweiter Codepfad. ----

        [Fact]
        public void HandleEntityDamaged_UpdatesCurrentHpAndMaxHp()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);

            world.HandleEntityDamaged(targetEntityId: 1, hpAfter: 70, maxHp: 100);

            Assert.Equal(70, world.Entities[1].CurrentHp);
            Assert.Equal(100, world.Entities[1].MaxHp);
        }

        [Fact]
        public void HandleEntityHpChanged_UpdatesCurrentHpAndMaxHp()
        {
            // Deckt genau die vom User gemeldete Lücke ab: eine HP-Änderung OHNE Treffer (Heiltrank,
            // Respawn, ...) muss denselben Zustand aktualisieren wie ein Treffer.
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);
            world.HandleEntityDamaged(targetEntityId: 1, hpAfter: 70, maxHp: 100);

            world.HandleEntityHpChanged(entityId: 1, currentHp: 100, maxHp: 100);

            Assert.Equal(100, world.Entities[1].CurrentHp);
            Assert.Equal(100, world.Entities[1].MaxHp);
        }

        [Fact]
        public void HandleEntityHpChanged_UnknownEntity_IsNoError()
        {
            var world = new ClientWorld();
            world.HandleEntityHpChanged(entityId: 999, currentHp: 50, maxHp: 100); // darf nicht werfen.
            Assert.False(world.Entities.ContainsKey(999));
        }

        // ---- M6a: ApplyCharacterStats - eigene HP laufen ueber die BESTEHENDE gemeinsame ApplyHp, kein
        // zweiter Speicherort (siehe docs/tasks/M6a-progression.md Abschnitt 6). ----

        private static CharacterStatsPacket SomeCharacterStats(int currentHp = 70, int maxHp = 100) => new CharacterStatsPacket
        {
            Level = 3,
            Xp = 45,
            XpToNext = 55,
            St = 8,
            Ht = 5,
            Dx = 3,
            Iq = 4,
            UnspentStatPoints = 2,
            UnspentSkillPoints = 1,
            MaxHp = maxHp,
            CurrentHp = currentHp,
            MaxSp = 40,
            CurrentSp = 20,
            Attack = 19,
            Defense = 7,
            DodgeBp = 150
        };

        [Fact]
        public void ApplyCharacterStats_UpdatesOwnHpViaSharedApplyHp_NoSecondStorage()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);

            world.ApplyCharacterStats(SomeCharacterStats(currentHp: 70, maxHp: 100));

            // Derselbe Schreibpfad wie HandleEntityDamaged/HandleEntityHpChanged (ApplyHp) - eigene HP landen
            // in ClientEntity, nicht (auch) in CharacterStats (das Feld existiert dort bewusst nicht, siehe
            // ClientWorld.CharacterState).
            Assert.Equal(70, world.Entities[1].CurrentHp);
            Assert.Equal(100, world.Entities[1].MaxHp);
        }

        [Fact]
        public void ApplyCharacterStats_UpdatesAllOtherFields()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);

            world.ApplyCharacterStats(SomeCharacterStats());

            Assert.Equal(3, world.CharacterStats.Level);
            Assert.Equal(45, world.CharacterStats.Xp);
            Assert.Equal(55, world.CharacterStats.XpToNext);
            Assert.Equal(8, world.CharacterStats.St);
            Assert.Equal(5, world.CharacterStats.Ht);
            Assert.Equal(3, world.CharacterStats.Dx);
            Assert.Equal(4, world.CharacterStats.Iq);
            Assert.Equal(2, world.CharacterStats.UnspentStatPoints);
            Assert.Equal(1, world.CharacterStats.UnspentSkillPoints);
            Assert.Equal(40, world.CharacterStats.MaxSp);
            Assert.Equal(20, world.CharacterStats.CurrentSp);
            Assert.Equal(19, world.CharacterStats.Attack);
            Assert.Equal(7, world.CharacterStats.Defense);
            Assert.Equal(150, world.CharacterStats.DodgeBp);
        }

        [Fact]
        public void ApplyCharacterStats_WithoutLocalEntity_DoesNotThrowAndStillUpdatesCharacterStats()
        {
            var world = new ClientWorld(); // kein HandleJoinAccepted - LocalEntityId bleibt -1.

            world.ApplyCharacterStats(SomeCharacterStats());

            Assert.Equal(3, world.CharacterStats.Level);
        }

        [Fact]
        public void Reset_ClearsCharacterStatsToSentinel()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 0, y: 0, localName: "Me", appearance: SomeAppearance);
            world.ApplyCharacterStats(SomeCharacterStats());

            world.Reset();

            Assert.Equal(0, world.CharacterStats.Level); // Sentinel: noch kein Paket seit dem Reset erhalten.
        }

        // ---- combat-visuals Abschnitt 5: SetFacingOverride ----

        [Fact]
        public void SetFacingOverride_TurnsEntityTowardsTarget()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);

            world.SetFacingOverride(entityId: 1, targetX: 51000, targetY: 50000); // Ziel rechts -> Side, kein FlipX.

            Assert.Equal(FacingDirection.Side, world.Entities[1].Facing.Direction);
            Assert.False(world.Entities[1].Facing.FlipX);
        }

        [Fact]
        public void SetFacingOverride_TargetToLeft_FlipsX()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);

            world.SetFacingOverride(entityId: 1, targetX: 49000, targetY: 50000); // Ziel links -> Side + FlipX.

            Assert.Equal(FacingDirection.Side, world.Entities[1].Facing.Direction);
            Assert.True(world.Entities[1].Facing.FlipX);
        }

        [Fact]
        public void SetFacingOverride_OverwrittenByNextMove_NoLastingOverride()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);

            world.SetFacingOverride(entityId: 1, targetX: 49000, targetY: 50000); // kurzzeitig nach links drehen.
            Assert.True(world.Entities[1].Facing.FlipX);

            // Der naechste echte Move-Befehl (z. B. Server bestaetigt eine Bewegung nach unten) ueberschreibt
            // die Blickrichtung wieder ganz normal bewegungsbasiert - kein dauerhafter Override.
            world.HandleEntityMove(entityId: 1, startX: 50000, startY: 50000, targetX: 50000, targetY: 49000, speed: 500, receivedAtTime: 1.0);

            Assert.Equal(FacingDirection.Down, world.Entities[1].Facing.Direction);
        }

        [Fact]
        public void HandleJoinAccepted_AppliesMapInfo()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(1, 50000, 65000, "Me", SomeAppearance,
                mapId: "greenfield", mapName: "Gruenes Feld", mapWidth: 100000, mapHeight: 80000);

            Assert.Equal("greenfield", world.MapId);
            Assert.Equal("Gruenes Feld", world.MapName);
            Assert.Equal(100000, world.MapWidth);
            Assert.Equal(80000, world.MapHeight);
        }

        [Fact]
        public void HandleMapChanged_ClearsOtherEntitiesKeepsLocalIdentityAndStats()
        {
            var world = new ClientWorld();
            var despawned = new List<int>();
            var spawned = new List<int>();
            world.EntityDespawned += id => despawned.Add(id);
            world.EntitySpawned += e => spawned.Add(e.Id);

            world.HandleJoinAccepted(1, 50000, 50000, "Me", SomeAppearance, mapId: "village", mapName: "Dorf");
            world.HandleEntitySpawn(2, "Bot", EntityKind.Player, 0, 50000, 50000, SomeAppearance);
            world.ApplyCharacterStats(new CharacterStatsPacket { Level = 7, Xp = 42, MaxHp = 100, CurrentHp = 90 });
            despawned.Clear();
            spawned.Clear();

            world.HandleMapChanged(1, 50000, 65000, "greenfield", "Gruenes Feld", 100000, 100000);

            Assert.Equal("greenfield", world.MapId);
            Assert.Equal("Gruenes Feld", world.MapName);
            Assert.Equal(1, world.LocalEntityId);
            Assert.Single(world.Entities);
            Assert.True(world.Entities.ContainsKey(1));
            Assert.False(world.Entities.ContainsKey(2));
            Assert.Equal(65000, world.Entities[1].IntY);
            Assert.Equal("Me", world.Entities[1].Name);
            Assert.Equal(SomeAppearance, world.Entities[1].Appearance);
            Assert.Contains(2, despawned);
            Assert.Contains(1, spawned); // eigene Figur wird an der Zielposition neu angelegt
            Assert.Equal(7, world.CharacterStats.Level); // Fortschritt bleibt (kein Reset wie bei Disconnect)
            Assert.Equal(42, world.CharacterStats.Xp);
        }

        [Fact]
        public void ClearEntities_KeepsMapInfoAndProgress()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(1, 50000, 50000, "Me", SomeAppearance, mapId: "greenfield", mapName: "Feld");
            world.ApplyCharacterStats(new CharacterStatsPacket { Level = 3, Xp = 5 });
            world.HandleEntitySpawn(2, "Bot", EntityKind.Player, 0, 1, 1, SomeAppearance);

            world.ClearEntities();

            Assert.Empty(world.Entities);
            Assert.Equal(-1, world.LocalEntityId);
            Assert.Equal("greenfield", world.MapId);
            Assert.Equal("Feld", world.MapName);
            Assert.Equal(3, world.CharacterStats.Level);
        }

        [Fact]
        public void SetFacingOverride_UnknownEntity_DoesNotThrow()
        {
            var world = new ClientWorld();
            world.SetFacingOverride(entityId: 999, targetX: 0, targetY: 0); // keine Exception, kein Effekt.
        }
    
        [Fact]
        public void HandleEntityMove_WhileMoving_DoesNotSnapBackToServerStart()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);
            world.Tick(0.0);
            world.HandleEntityMove(1, 50000, 50000, 51000, 50000, speed: 500, receivedAtTime: 0.0);
            world.Tick(0.1); // angezeigt: 50050

            // Der Server meldet einen leicht zurückliegenden Start (Latenz/Tick-Raster) - die Figur darf
            // dadurch nicht sichtbar zurückspringen.
            world.HandleEntityMove(1, 50030, 50000, 51030, 50000, speed: 500, receivedAtTime: 0.1);
            Assert.Equal(50050, world.Entities[1].X, 3);

            // Der Versatz wird weich ausgeblendet und die Figur läuft danach exakt auf dem Server-Pfad.
            world.Tick(0.2);
            Assert.InRange(world.Entities[1].X, 50080, 50100);
            world.Tick(1.1);
            Assert.Equal(50530, world.Entities[1].X, 0);
        }

        [Fact]
        public void HandleEntityStop_FarAway_SnapsImmediately()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);
            world.Tick(0.0);

            world.HandleEntityStop(1, 20000, 20000); // z. B. Respawn - kein Hinübergleiten über die Karte.
            Assert.Equal(20000, world.Entities[1].IntX);
            Assert.Equal(20000, world.Entities[1].IntY);
        }

        [Fact]
        public void HandleEntityStop_SmallOffset_BlendsOutWithoutJump()
        {
            var world = new ClientWorld();
            world.HandleJoinAccepted(entityId: 1, x: 50000, y: 50000, localName: "Me", appearance: SomeAppearance);
            world.Tick(0.0);

            world.HandleEntityStop(1, 50040, 50000);
            Assert.Equal(50000, world.Entities[1].IntX);
            world.Tick(1.0);
            Assert.Equal(50040, world.Entities[1].IntX);
        }
    
        [Fact]
        public void HandleEntitySpawn_StoresLevel()
        {
            var world = new ClientWorld();
            world.HandleEntitySpawn(5, "Wildhund", EntityKind.Monster, 1, 100, 100, SomeAppearance, level: 3);
            Assert.Equal(3, world.Entities[5].Level);
        }
    }
}
