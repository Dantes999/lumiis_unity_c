using System;
using System.IO;
using System.Linq;
using System.Threading;
using LiteNetLib;
using Lumiis.Data;
using Lumiis.Data.Classes;
using Lumiis.Data.Items;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumiis.Tests.Integration
{
    /// <summary>Echtes LiteNetLib auf Loopback (freier Port), DB per EF-Core-InMemory statt echtem Postgres -
    /// Logik-Tests, kein Beweis für echtes Postgres-Verhalten (siehe Lumiis.Tests.Auth-Klassenkommentare).
    /// Server und Clients werden manuell gepumpt, mit Timeout statt festen Sleeps.</summary>
    public class GameServerIntegrationTests
    {
        private const string TokenSecret = "integration-test-token-secret-at-least-32b";

        private sealed class NullLogger : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
        }

        /// <summary>Bündelt einen GameServer mit einer eigenen InMemory-Datenbank und TokenService -
        /// CreateCharacter() legt Account+Character an und gibt (CharacterId, Token) zurück, direkt für
        /// JoinRequestPacket nutzbar.</summary>
        private sealed class ServerFixture
        {
            private readonly Func<LumiisDbContext> _dbFactory;

            public GameServer Server { get; }
            public TokenService TokenService { get; }

            public ServerFixture(Func<long>? nowMs = null, Func<long>? tokenNowUnixSeconds = null, CollisionMap? collisionMap = null,
                MobCatalog? mobCatalog = null, SpawnCatalog? spawnCatalog = null, ClassCatalog? classCatalog = null,
                ItemCatalog? itemCatalog = null, NpcCatalog? npcCatalog = null, NpcSpawnCatalog? npcSpawnCatalog = null,
                bool allowDebugCommands = false)
            {
                DbContextOptions<LumiisDbContext> options = new DbContextOptionsBuilder<LumiisDbContext>()
                    .UseInMemoryDatabase("gameserver-test-" + Guid.NewGuid())
                    .Options;
                _dbFactory = () => new LumiisDbContext(options);
                TokenService = new TokenService(TokenSecret, tokenNowUnixSeconds);

                var config = new ServerConfig
                {
                    Port = 0, // freier Port, vom OS vergeben
                    MaxPlayers = 10,
                    RejectGraceMs = 100, // echte Uhr, kleiner Wert - siehe M1-fixes.md Punkt 2
                    AllowDebugCommands = allowDebugCommands
                };
                Server = new GameServer(config, new NullLogger(), TokenService, _dbFactory, nowMs, collisionMap, mobCatalog, spawnCatalog, classCatalog,
                    itemCatalog, npcCatalog, npcSpawnCatalog);
            }

            public (int CharacterId, string Token) CreateCharacter(string name, int? positionX = null, int? positionY = null, int? accountId = null)
            {
                using LumiisDbContext db = _dbFactory();
                int ownerAccountId = accountId ?? CreateAccount(db);

                var character = new Character
                {
                    AccountId = ownerAccountId,
                    Name = name,
                    BodyId = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    PositionX = positionX,
                    PositionY = positionY
                };
                db.Characters.Add(character);
                db.SaveChanges();

                return (character.Id, TokenService.Issue(ownerAccountId));
            }

            public int CreateAccount()
            {
                using LumiisDbContext db = _dbFactory();
                return CreateAccount(db);
            }

            public LumiisDbContext OpenDb() => _dbFactory();

            private static int CreateAccount(LumiisDbContext db)
            {
                var account = new Account
                {
                    Username = "acc_" + Guid.NewGuid().ToString("N")[..12],
                    PasswordHash = new byte[] { 0 },
                    PasswordSalt = new byte[] { 0 },
                    PasswordIterations = 1,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Accounts.Add(account);
                db.SaveChanges();
                return account.Id;
            }
        }

        private static bool Pump(Func<bool> condition, GameServer server, params TestClient[] clients)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                server.Update();
                foreach (TestClient client in clients) client.Poll();

                if (condition()) return true;
                Thread.Sleep(15); // entspricht NetManager.UpdateTime, damit LiteNetLib je Tick wirklich einmal sendet
            }
            return condition();
        }

        private static bool WaitConnected(GameServer server, params TestClient[] clients)
        {
            return Pump(() => clients.All(c => c.Peer != null && c.Peer.ConnectionState == ConnectionState.Connected), server, clients);
        }

        /// <summary>Für Negativ-Prüfungen ("in dieser Zeit ist nichts passiert") - pumpt IMMER die volle
        /// Dauer, anders als Pump() (das bei erfüllter Bedingung sofort zurückkehrt).</summary>
        private static void PumpForDuration(TimeSpan duration, GameServer server, params TestClient[] clients)
        {
            DateTime deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                server.Update();
                foreach (TestClient client in clients) client.Poll();
                Thread.Sleep(15);
            }
        }

        [Fact]
        public void TwoClients_JoinAndSeeEachOthersSpawn()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);

                Assert.True(WaitConnected(server, clientA, clientB));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                // B sieht den Spawn von A (als vorhandene Entity beim eigenen Join).
                Assert.True(Pump(() => clientB.Received.OfType<EntitySpawnPacket>().Any(p => p.Name == "Alice"), server, clientA, clientB));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Appearance_IsIdenticalBetweenOwnJoinAcceptedAndOthersEntitySpawn()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                Appearance aliceOwnAppearance = clientA.Received.OfType<JoinAcceptedPacket>().First().Appearance;

                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                Assert.True(Pump(() => clientB.Received.OfType<EntitySpawnPacket>().Any(p => p.Name == "Alice"), server, clientA, clientB));

                Appearance aliceAsSeenByBob = clientB.Received.OfType<EntitySpawnPacket>().First(p => p.Name == "Alice").Appearance;
                Assert.Equal(aliceOwnAppearance, aliceAsSeenByBob);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void JoinAccepted_AppearanceMatchesCharacterRow()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    Character character = db.Characters.Single(c => c.Id == characterId);
                    character.HairStyleId = 2;
                    character.HairColorId = 3;
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                // Ruestung/Helm/Waffe/Schild (M5a: jetzt ueber ItemCatalog aus EquippedXItemId aufgeloest,
                // siehe JoinAccepted_ResolvesEquippedItemsToLookIds) werden dort mit echten Items abgedeckt -
                // hier nur noch Koerper/Haare, die weiterhin direkt aus der Character-Zeile kommen.
                Appearance appearance = client.Received.OfType<JoinAcceptedPacket>().First().Appearance;
                Assert.Equal(1, appearance.BodyId);
                Assert.Equal(2, appearance.HairStyleId);
                Assert.Equal(3, appearance.HairColorId);
            }
            finally
            {
                server.Stop();
            }
        }

        /// <summary>M5a: EquippedWeaponItemId auf der Character-Zeile wird beim Join über ItemCatalog zu
        /// einer LookId aufgelöst (siehe GameServer.CompleteJoin/ToAppearance) - kein direktes Durchreichen
        /// einer DB-Spalte mehr wie vor M5a.</summary>
        [Fact]
        public void JoinAccepted_ResolvesEquippedItemsToLookIds()
        {
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(@"{
  ""items"": [
    { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 2, ""defenseBonus"": 0 }
  ]
}", TestLookCatalogs.CreateMinimal());

            var fixture = new ServerFixture(itemCatalog: itemCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    Character character = db.Characters.Single(c => c.Id == characterId);
                    character.EquippedWeaponItemId = 1;
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Appearance appearance = client.Received.OfType<JoinAcceptedPacket>().First().Appearance;
                Assert.Equal(1, appearance.WeaponId); // LookId aus dem Item, nicht die ItemId selbst.
                Assert.Equal(0, appearance.ArmorId);
                Assert.Equal(0, appearance.HelmetId);
                Assert.Equal(0, appearance.ShieldId);

                InventorySyncPacket sync = client.Received.OfType<InventorySyncPacket>().First();
                Assert.Equal(1, sync.EquippedWeaponItemId); // hier weiterhin die ItemId, nicht die LookId.
                Assert.Empty(sync.Slots);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void ExistingClient_SeesLaterJoinerViaBroadcast()
        {
            // Deckt den Fall ab, der bei TwoClients_JoinAndSeeEachOthersSpawn NICHT geprüft wird:
            // A ist schon drin, B joint SPÄTER - kommt bei A ein EntitySpawn für B per Broadcast an?
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                clientA.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA));

                using var clientB = new TestClient();
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                Assert.True(Pump(() => clientA.Received.OfType<EntitySpawnPacket>().Any(p => p.Name == "Bob"), server, clientA, clientB));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void ExistingClient_SeesBurstOfLaterJoiners()
        {
            // Näher an "Lumiis.Bot --count 3": mehrere neue Clients joinen kurz hintereinander,
            // ohne dass zwischendurch gewartet wird - sieht der bereits verbundene Client alle drei?
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                clientA.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA));

                using var bot1 = new TestClient();
                using var bot2 = new TestClient();
                using var bot3 = new TestClient();
                bot1.Connect(server.ListenPort);
                bot2.Connect(server.ListenPort);
                bot3.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, bot1, bot2, bot3));

                (int bot1Id, string bot1Token) = fixture.CreateCharacter("Bot1");
                (int bot2Id, string bot2Token) = fixture.CreateCharacter("Bot2");
                (int bot3Id, string bot3Token) = fixture.CreateCharacter("Bot3");
                bot1.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bot1Token, CharacterId = bot1Id });
                bot2.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bot2Token, CharacterId = bot2Id });
                bot3.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bot3Token, CharacterId = bot3Id });

                Assert.True(Pump(
                    () => clientA.Received.OfType<EntitySpawnPacket>().Select(p => p.Name).Distinct().Count() >= 3,
                    server, clientA, bot1, bot2, bot3));

                var seenNames = clientA.Received.OfType<EntitySpawnPacket>().Select(p => p.Name).Distinct().ToList();
                Assert.Contains("Bot1", seenNames);
                Assert.Contains("Bot2", seenNames);
                Assert.Contains("Bot3", seenNames);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void MoveTo_BroadcastsMoveThenStopAtExactTarget()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                int targetX = GameConstants.MapWidth / 2 + 1000; // 2s Laufzeit bei PlayerSpeed
                int targetY = GameConstants.MapHeight / 2;
                clientA.Send(new MoveToPacket { X = targetX, Y = targetY });

                Assert.True(Pump(
                    () => clientB.Received.OfType<EntityMovePacket>().Any(m => m.TargetX == targetX && m.TargetY == targetY),
                    server, clientA, clientB));

                Assert.True(Pump(
                    () => clientB.Received.OfType<EntityStopPacket>().Any(s => s.X == targetX && s.Y == targetY),
                    server, clientA, clientB));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Disconnect_SendsDespawnToOthers()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                int aliceEntityId = clientA.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                clientA.Peer!.Disconnect();
                Assert.True(Pump(
                    () => clientB.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == aliceEntityId),
                    server, clientA, clientB));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Disconnect_SavesPositionImmediatelyToDatabase()
        {
            // Position wird asynchron über einen Hintergrund-Task geschrieben (siehe GameServer.
            // RunPositionSaveConsumerAsync) - deshalb auf die (InMemory-)DB pollen statt fest zu warten.
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice"); // Startposition: Kartenmitte (PositionX/Y null in der DB)
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                int targetX = GameConstants.MapWidth / 2 + 500;
                int targetY = GameConstants.MapHeight / 2;
                client.Send(new MoveToPacket { X = targetX, Y = targetY });
                Assert.True(Pump(() => client.Received.OfType<EntityStopPacket>().Any(), server, client)); // exakt angekommen, bevor getrennt wird

                client.Peer!.Disconnect();
                Assert.True(Pump(() => client.Disconnected, server, client));

                bool saved = Pump(() =>
                {
                    using LumiisDbContext db = fixture.OpenDb();
                    Character? character = db.Characters.Find(characterId);
                    return character != null && character.PositionX == targetX && character.PositionY == targetY;
                }, server, client);

                Assert.True(saved);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void WrongProtocolVersion_IsRejected()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version + 1, Token = token, CharacterId = characterId });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.VersionMismatch),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void InvalidToken_IsRejected()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = "kauderwelsch.not-a-token", CharacterId = 1 });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.InvalidToken),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void ExpiredToken_IsRejectedAsExpiredNotInvalid()
        {
            long fakeTokenNow = 1_000_000;
            var fixture = new ServerFixture(tokenNowUnixSeconds: () => fakeTokenNow);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                fakeTokenNow += TokenService.ValiditySeconds + 1; // Token nach der Ausgabe ablaufen lassen.

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.TokenExpired),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void CharacterFromDifferentAccount_IsRejectedAsNotFound()
        {
            // Eigentümerschaft geprüft: eine gültige CharacterId, aber ein Token für einen ANDEREN Account.
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, _) = fixture.CreateCharacter("Alice");
                int otherAccountId = fixture.CreateAccount();
                string otherAccountToken = fixture.TokenService.Issue(otherAccountId);

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = otherAccountToken, CharacterId = characterId });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.CharacterNotFound),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void UnknownCharacterId_IsRejectedAsNotFound()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                int accountId = fixture.CreateAccount();
                string token = fixture.TokenService.Issue(accountId);

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = 999999 });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.CharacterNotFound),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void SecondJoinWithSameCharacterId_IsRejectedWhileFirstStillConnected_ThenSucceedsAfterDisconnect()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                clientA.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA));

                using var clientB = new TestClient();
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });

                Assert.True(Pump(
                    () => clientB.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.CharacterAlreadyOnline),
                    server, clientA, clientB));

                // Nach dem Disconnect des ersten Clients klappt ein erneuter Join mit derselben CharacterId wieder.
                clientA.Peer!.Disconnect();
                Assert.True(Pump(() => clientA.Disconnected, server, clientA, clientB));

                using var clientC = new TestClient();
                clientC.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientC));
                clientC.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => clientC.Received.OfType<JoinAcceptedPacket>().Any(), server, clientC));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void GarbagePacket_DisconnectsClient_ServerStaysAlive()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var badClient = new TestClient();
                badClient.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, badClient));

                badClient.SendRaw(new byte[] { 0xFF, 0xFF, 0x01, 0x02 }); // unbekannte PacketId
                Assert.True(Pump(() => badClient.Disconnected, server, badClient));

                // Server lebt weiter: ein neuer Client kann normal joinen.
                using var goodClient = new TestClient();
                goodClient.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, goodClient));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                goodClient.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => goodClient.Received.OfType<JoinAcceptedPacket>().Any(), server, goodClient));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void OversizedPacket_DisconnectsClient_ServerStaysAlive()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var badClient = new TestClient();
                badClient.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, badClient));

                badClient.SendRaw(new byte[Protocol.MaxPacketBytes + 1]); // größer als erlaubt
                Assert.True(Pump(() => badClient.Disconnected, server, badClient));

                // Server lebt weiter: ein neuer Client kann normal joinen.
                using var goodClient = new TestClient();
                goodClient.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, goodClient));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                goodClient.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => goodClient.Received.OfType<JoinAcceptedPacket>().Any(), server, goodClient));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void ExceptionInHandler_DisconnectsOnlyThatConnection_ServerAndOthersStayAlive()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.TestFaultHook = () => throw new InvalidOperationException("Simulierter Fehler im Handler");
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int aliceId, string aliceToken) = fixture.CreateCharacter("Alice");
                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = aliceToken, CharacterId = aliceId });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                (int bobId, string bobToken) = fixture.CreateCharacter("Bob");
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = bobToken, CharacterId = bobId });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                // A löst über den Test-Hook eine Exception im MoveTo-Handler aus.
                clientA.Send(new MoveToPacket { X = 1000, Y = 1000 });
                Assert.True(Pump(() => clientA.Disconnected, server, clientA, clientB));

                // Server lebt weiter, B ist unbeeinträchtigt und kann normal weiter spielen.
                server.TestFaultHook = null;
                Assert.False(clientB.Disconnected);

                clientB.Send(new MoveToPacket { X = 2000, Y = 2000 });
                Assert.True(Pump(
                    () => clientB.Received.OfType<EntityMovePacket>().Any(m => m.TargetX == 2000 && m.TargetY == 2000),
                    server, clientA, clientB));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void JoinTimeout_UsesFakeClock_DisconnectsOnlyAfterDeadlineReached()
        {
            long fakeNow = 0;
            const int joinTimeoutMs = 5000; // ServerConfig.JoinTimeoutMs-Default
            var fixture = new ServerFixture(nowMs: () => fakeNow);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                // Vor Ablauf der Frist: mehrfach ticken, ohne die Uhr vorzudrehen - keine Trennung.
                for (int i = 0; i < 5; i++) server.Update();
                client.Poll();
                Assert.False(client.Disconnected);

                // Uhr über die Frist hinausdrehen (kein echtes Warten nötig) - jetzt trennt der nächste Tick.
                fakeNow = joinTimeoutMs + 1;
                Assert.True(Pump(() => client.Disconnected, server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void RejectGrace_UsesFakeClock_KeepsConnectionUntilDeadlineReached()
        {
            long fakeNow = 0;
            var fixture = new ServerFixture(nowMs: () => fakeNow);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version + 1, Token = "egal", CharacterId = 1 }); // synchron ablehnbar (falsche Version), kein DB-Lookup nötig
                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.VersionMismatch),
                    server, client));

                // JoinRejected ist angekommen, Grace-Frist läuft noch (Uhr steht still) - noch nicht getrennt.
                for (int i = 0; i < 5; i++) server.Update();
                client.Poll();
                Assert.False(client.Disconnected);

                // Uhr über die Grace-Frist hinausdrehen - jetzt trennt der nächste Tick.
                fakeNow = 100 + 1; // RejectGraceMs aus ServerFixture()
                Assert.True(Pump(() => client.Disconnected, server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M3a: Kollision, mit der echten village-Karte (siehe docs/tasks/M3a-collision.md Abschnitt 3) ----

        /// <summary>Sucht data/maps/village/collision.bin ausgehend vom tatsächlichen Testassembly-Verzeichnis
        /// (AppContext.BaseDirectory, nicht abhängig davon, welches Arbeitsverzeichnis der Testrunner setzt) -
        /// läuft die Verzeichnisse bis zum Repo-Root hoch.</summary>
        private static string ResolveVillageCollisionMapPath()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data", "maps", "village", "collision.bin");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("data/maps/village/collision.bin nicht gefunden (ausgehend von " + AppContext.BaseDirectory + ").");
        }

        [Fact]
        public void MoveIntoPlaceholderPond_StopsBeforeBlockedArea()
        {
            CollisionMap collisionMap = CollisionMap.LoadFromFile(
                ResolveVillageCollisionMapPath(), GameConstants.MapWidth / 100, GameConstants.MapHeight / 100);

            var fixture = new ServerFixture(collisionMap: collisionMap);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                // Startposition dicht vor dem linken Teichrand (Zelle 595,700 - der Teich beginnt bei Zeile
                // 700 laut collision-source.png erst bei Zelle 600, siehe Ellipse 600,630-800,770). Bewusst
                // NAH am Teich statt an der Kartenmitte: Pump() wartet real-zeitlich (PlayerSpeed 5 Einh./s),
                // eine lange Strecke würde das feste 5s-Timeout von Pump() sprengen.
                const int startX = 59500;
                const int startY = 70000;
                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: startX, positionY: startY);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Assert.False(collisionMap.IsBlocked(startX / 100, startY / 100), "Testannahme verletzt: Startzelle (595,700) müsste laut Teich-Platzhalter begehbar sein.");

                // Ziel: mitten in den Teich (Zelle 700,700 - Ellipsen-Zentrum, dieselbe Zeile wie der Start) -
                // muss laut Karte blockiert sein, sonst würde dieser Test gar nichts über Kollision beweisen.
                const int targetX = 70050;
                const int targetY = 70000;
                Assert.True(collisionMap.IsBlocked(targetX / 100, targetY / 100), "Testannahme verletzt: Zelle (700,700) müsste laut Teich-Platzhalter blockiert sein.");

                client.Send(new MoveToPacket { X = targetX, Y = targetY });
                Assert.True(Pump(() => client.Received.OfType<EntityStopPacket>().Any(), server, client));

                EntityStopPacket stop = client.Received.OfType<EntityStopPacket>().Last();

                Assert.False(collisionMap.IsBlocked(stop.X / 100, stop.Y / 100), $"EntityStop bei ({stop.X},{stop.Y}) liegt auf einer blockierten Zelle - sollte davor stehen bleiben.");
                Assert.False(stop.X == targetX && stop.Y == targetY, "EntityStop kam exakt am Teich-Ziel an - Kollision hätte greifen müssen.");
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M3b: Sichtbarkeitsbereich (AOI, siehe docs/tasks/M3b-aoi.md Abschnitt 5) ----

        [Fact]
        public void TwoPlayersOutsideAoiRadius_NeitherSeesTheOther()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                (int idA, string tokenA) = fixture.CreateCharacter("Alice", positionX: 10000, positionY: 10000);
                (int idB, string tokenB) = fixture.CreateCharacter("Bob", positionX: 10000, positionY: 12000); // 2000 > AoiRadiusWire (1500)

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = tokenA, CharacterId = idA });
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = tokenB, CharacterId = idB });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any() && clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                // Mehrere Ticks abwarten (nicht nur "kein Fehler") - keiner darf je ein EntitySpawn für den anderen bekommen.
                PumpForDuration(TimeSpan.FromMilliseconds(400), server, clientA, clientB);

                Assert.Empty(clientA.Received.OfType<EntitySpawnPacket>());
                Assert.Empty(clientB.Received.OfType<EntitySpawnPacket>());
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void PlayerMovesIntoRange_ObserverGetsSpawnAndMove()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var observer = new TestClient();
                using var mover = new TestClient();
                observer.Connect(server.ListenPort);
                mover.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, observer, mover));

                (int observerCharacterId, string observerToken) = fixture.CreateCharacter("Observer", positionX: 50000, positionY: 50000);
                // 1600 > AoiRadiusWire (1500) - außerhalb, beim Join noch nicht sichtbar.
                (int moverCharacterId, string moverToken) = fixture.CreateCharacter("Mover", positionX: 50000, positionY: 51600);

                observer.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = observerToken, CharacterId = observerCharacterId });
                mover.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = moverToken, CharacterId = moverCharacterId });
                Assert.True(Pump(() => observer.Received.OfType<JoinAcceptedPacket>().Any() && mover.Received.OfType<JoinAcceptedPacket>().Any(), server, observer, mover));

                // EntityId (World-intern, per TryJoin vergeben) ist NICHT dasselbe wie CharacterId (DB) - beide
                // Zähler starten bei fixen ServerFixtures zufällig bei 1 und stimmen nur zufällig überein, je
                // nachdem in welcher Reihenfolge die asynchronen Joins tatsächlich abgeschlossen werden.
                int moverEntityId = mover.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                // 800 Wire-Einheiten näher (Ziel 800 von observer entfernt, innerhalb des Radius) - kurze Strecke, damit der Test schnell bleibt.
                mover.Send(new MoveToPacket { X = 50000, Y = 50800 });

                Assert.True(Pump(
                    () => observer.Received.OfType<EntitySpawnPacket>().Any(s => s.EntityId == moverEntityId),
                    server, observer, mover));
                Assert.Contains(observer.Received.OfType<EntityMovePacket>(), m => m.EntityId == moverEntityId);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void PlayerMovesOutOfRange_ObserverGetsDespawn()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var observer = new TestClient();
                using var mover = new TestClient();
                observer.Connect(server.ListenPort);
                mover.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, observer, mover));

                (int observerCharacterId, string observerToken) = fixture.CreateCharacter("Observer", positionX: 50000, positionY: 50000);
                (int moverCharacterId, string moverToken) = fixture.CreateCharacter("Mover", positionX: 50000, positionY: 50500); // 500 < AoiRadiusWire, von Anfang an sichtbar.

                observer.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = observerToken, CharacterId = observerCharacterId });
                mover.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = moverToken, CharacterId = moverCharacterId });
                Assert.True(Pump(() => observer.Received.OfType<JoinAcceptedPacket>().Any() && mover.Received.OfType<JoinAcceptedPacket>().Any(), server, observer, mover));

                int moverEntityId = mover.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                Assert.True(Pump(() => observer.Received.OfType<EntitySpawnPacket>().Any(s => s.EntityId == moverEntityId), server, observer, mover));

                mover.Send(new MoveToPacket { X = 50000, Y = 51600 }); // 1600 > AoiRadiusWire - verlässt den Radius.

                Assert.True(Pump(
                    () => observer.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == moverEntityId),
                    server, observer, mover));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void JoinWhileVisiblePlayerIsMoving_GetsSpawnWithCurrentPositionAndMove()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var mover = new TestClient();
                mover.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, mover));

                (int moverCharacterId, string moverToken) = fixture.CreateCharacter("Mover", positionX: 50000, positionY: 50000);
                mover.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = moverToken, CharacterId = moverCharacterId });
                Assert.True(Pump(() => mover.Received.OfType<JoinAcceptedPacket>().Any(), server, mover));

                int moverEntityId = mover.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                // Lange genug Strecke, damit "mover" beim späteren Join des Joiners nachweislich noch in
                // Bewegung ist (1000 Wire-Einheiten bei PlayerSpeed 500/s = 2s), aber kurz genug für den Test.
                mover.Send(new MoveToPacket { X = 51000, Y = 50000 });

                using var joiner = new TestClient();
                joiner.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, joiner, mover));

                // 300 von der URSPRÜNGLICHEN Position von "mover" entfernt - selbst nach etwas Bewegung
                // seit dem MoveTo oben klar innerhalb des AoiRadiusWire (1500).
                (int joinerCharacterId, string joinerToken) = fixture.CreateCharacter("Joiner", positionX: 50300, positionY: 50000);
                joiner.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = joinerToken, CharacterId = joinerCharacterId });
                Assert.True(Pump(() => joiner.Received.OfType<JoinAcceptedPacket>().Any(), server, joiner, mover));

                Assert.Contains(joiner.Received.OfType<EntitySpawnPacket>(), s => s.EntityId == moverEntityId);
                Assert.Contains(joiner.Received.OfType<EntityMovePacket>(), m => m.EntityId == moverEntityId && m.TargetX == 51000);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Disconnect_OnlyVisibleObserverGetsDespawn_OtherObserverGetsNothing()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var target = new TestClient();
                using var nearObserver = new TestClient();
                using var farObserver = new TestClient();
                target.Connect(server.ListenPort);
                nearObserver.Connect(server.ListenPort);
                farObserver.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, target, nearObserver, farObserver));

                (int targetCharacterId, string targetToken) = fixture.CreateCharacter("Target", positionX: 50000, positionY: 50000);
                (int nearCharacterId, string nearToken) = fixture.CreateCharacter("Near", positionX: 50000, positionY: 50500); // 500 < Radius
                (int farCharacterId, string farToken) = fixture.CreateCharacter("Far", positionX: 90000, positionY: 90000); // weit weg

                target.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = targetToken, CharacterId = targetCharacterId });
                nearObserver.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = nearToken, CharacterId = nearCharacterId });
                farObserver.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = farToken, CharacterId = farCharacterId });
                Assert.True(Pump(
                    () => target.Received.OfType<JoinAcceptedPacket>().Any()
                        && nearObserver.Received.OfType<JoinAcceptedPacket>().Any()
                        && farObserver.Received.OfType<JoinAcceptedPacket>().Any(),
                    server, target, nearObserver, farObserver));

                int targetEntityId = target.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                Assert.True(Pump(
                    () => nearObserver.Received.OfType<EntitySpawnPacket>().Any(s => s.EntityId == targetEntityId),
                    server, target, nearObserver, farObserver));

                target.Peer!.Disconnect();

                Assert.True(Pump(
                    () => nearObserver.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == targetEntityId),
                    server, nearObserver, farObserver));

                // Negativ-Prüfung mit echtem Timeout, nicht nur "kein Fehler aufgetreten".
                PumpForDuration(TimeSpan.FromMilliseconds(400), server, farObserver);
                Assert.DoesNotContain(farObserver.Received.OfType<EntityDespawnPacket>(), d => d.EntityId == targetEntityId);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M3c: Monster (siehe docs/tasks/M3c-monsters.md Abschnitt 7) ----

        /// <summary>Offene 1000x1000-Zellen-CollisionMap (deckt GameConstants.MapWidth/Height exakt ab) - für
        /// diesen Test reicht eine synthetische Karte, das echte collision.bin/village-Zusammenspiel prüft
        /// bereits der M3a-Integrationstest.</summary>
        private static CollisionMap BuildOpenCollisionMap()
        {
            const int width = 1000;
            const int height = 1000;
            byte[] header = new byte[12];
            System.Text.Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);
            byte[] packed = new byte[(width * height + 7) / 8];
            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, width, height);
        }

        /// <summary>Sendet wiederholt AttackRequestPacket gegen targetEntityId, bis mindestens
        /// minDamagePacketCount EntityDamagedPackets für dieses Ziel angekommen sind (Cooldown lehnt
        /// einzelne Versuche ab, das ist erwartet, siehe AttackerKillsMonster-Test oben) oder ein 5s-Timeout
        /// erreicht ist.</summary>
        private static void AttackUntilDamaged(GameServer server, TestClient client, int targetEntityId, int minDamagePacketCount)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline
                && client.Received.OfType<EntityDamagedPacket>().Count(d => d.TargetEntityId == targetEntityId) < minDamagePacketCount)
            {
                client.Send(new AttackRequestPacket { TargetEntityId = targetEntityId });
                server.Update();
                client.Poll();
                Thread.Sleep(15);
            }
            Assert.True(client.Received.OfType<EntityDamagedPacket>().Count(d => d.TargetEntityId == targetEntityId) >= minDamagePacketCount);
        }

        [Fact]
        public void BotNearSpawnPoint_SeesMonsterSpawnAndEventualMovement()
        {
            // Kurzes Wander-Intervall (synthetisch, nicht die echten mobs.json-Werte) - haelt den Test schnell.
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 100, ""wanderIntervalMaxMs"": 200, ""maxHp"": 30, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 30 } ]
            }", mobCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                // Dieselbe Position wie der Spawn-Punkt - garantiert innerhalb des AOI-Radius.
                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, client));
                EntitySpawnPacket monsterSpawn = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster);
                Assert.Equal(1, monsterSpawn.TypeId);
                Assert.Equal("Wildhund", monsterSpawn.Name);

                Assert.True(Pump(
                    () => client.Received.OfType<EntityMovePacket>().Any(m => m.EntityId == monsterSpawn.EntityId),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M4a: Kampf ----

        [Fact]
        public void AttackerKillsMonster_ObserverSeesDamageAndDespawn_YangOnlyToAttacker()
        {
            // Kurzer Cooldown und niedriges MobHp (synthetisch, nicht die echten Daten aus data/) - haelt den
            // Test schnell, erzwingt aber trotzdem mehrere Treffer (40 HP / 15 Schaden pro Treffer = 3 Treffer).
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 40, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 30 } ]
            }", mobCatalog, openMap);
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 50, ""baseHp"": 100, ""baseSp"": 0, ""baseAttack"": 15, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog, classCatalog: classCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var attacker = new TestClient();
                using var observer = new TestClient();
                attacker.Connect(server.ListenPort);
                observer.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, attacker, observer));

                // Angreifer direkt auf dem Spawn-Punkt (innerhalb Nahkampfreichweite), Beobachter 500 Einheiten
                // entfernt - außerhalb der Nahkampfreichweite (150), aber innerhalb des AOI-Radius (1500).
                (int attackerCharId, string attackerToken) = fixture.CreateCharacter("Attacker", positionX: 50000, positionY: 50000);
                (int observerCharId, string observerToken) = fixture.CreateCharacter("Observer", positionX: 50500, positionY: 50000);
                attacker.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = attackerToken, CharacterId = attackerCharId });
                observer.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = observerToken, CharacterId = observerCharId });
                Assert.True(Pump(() => attacker.Received.OfType<JoinAcceptedPacket>().Any() && observer.Received.OfType<JoinAcceptedPacket>().Any(),
                    server, attacker, observer));

                Assert.True(Pump(() => attacker.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, attacker, observer));
                int monsterId = attacker.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster).EntityId;

                // Wiederholt angreifen (Cooldown macht einzelne Versuche fehlschlagen, das ist erwartet und
                // wird hier nicht geprüft) bis das Monster stirbt.
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                bool monsterDied = false;
                while (DateTime.UtcNow < deadline && !monsterDied)
                {
                    attacker.Send(new AttackRequestPacket { TargetEntityId = monsterId });
                    server.Update();
                    attacker.Poll();
                    observer.Poll();
                    monsterDied = attacker.Received.OfType<EntityDiedPacket>().Any(d => d.EntityId == monsterId);
                    Thread.Sleep(15);
                }
                Assert.True(monsterDied);

                // Schaden kam bei beiden an (Angreifer UND Beobachter, siehe AOI), mehrfach (3 Treffer erwartet).
                Assert.True(attacker.Received.OfType<EntityDamagedPacket>().Count(d => d.TargetEntityId == monsterId) >= 3);
                Assert.Contains(observer.Received.OfType<EntityDamagedPacket>(), d => d.TargetEntityId == monsterId);

                // Tod dann Despawn kamen bei beiden an.
                Assert.Contains(observer.Received.OfType<EntityDiedPacket>(), d => d.EntityId == monsterId);
                Assert.True(Pump(() => attacker.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == monsterId)
                    && observer.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == monsterId), server, attacker, observer));

                // Yang nur beim Angreifer, im erwarteten Bereich - nur der Todesschlag zahlt aus (yangMin..yangMax).
                // Beide bekommen beim Join je ein YangChangedPacket mit dem Anfangsstand (Bugfix Yang-Anzeige) -
                // beim Beobachter bleibt das der EINZIGE (0, unveraendert), er wird nie zusaetzlich gutgeschrieben.
                Assert.True(attacker.Received.OfType<YangChangedPacket>().Count() >= 2); // Join (0) + Kill-Gutschrift.
                long lastYang = attacker.Received.OfType<YangChangedPacket>().Last().NewTotal;
                Assert.InRange(lastYang, 5, 15);
                Assert.Single(observer.Received.OfType<YangChangedPacket>());
                Assert.Equal(0, observer.Received.OfType<YangChangedPacket>().Single().NewTotal);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M5a: Items/Inventar (siehe docs/tasks/M5a-items-equipment.md) ----

        [Fact]
        public void KillWithGuaranteedDrop_ThenEquipDroppedWeapon_IncreasesDamageOnNextHit()
        {
            // Monster A: 1 Treffer toetet es sicher (maxHp 10 < 15 Basis-Angriff), garantierter Drop
            // (chancePercent 100, siehe World.TryAttack - deterministisch, kein Random-Seed noetig).
            // Monster B: hohes MaxHp (stirbt nicht), dient als Schaden-Dummy vor/nach dem Ausruesten.
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 10, ""attack"": 5, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30,
                  ""drops"": [ { ""itemId"": 1, ""chancePercent"": 100 } ] },
                { ""id"": 2, ""name"": ""Waldschrat"", ""speedWire"": 200, ""wanderRadiusWire"": 300, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 9999, ""attack"": 0, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 150, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [
                { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 999999 },
                { ""id"": 2, ""mobId"": 2, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 999999 }
              ]
            }", mobCatalog, openMap);
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 50, ""baseHp"": 100, ""baseSp"": 0, ""baseAttack"": 15, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(@"{
              ""items"": [ { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 50, ""defenseBonus"": 0 } ]
            }", TestLookCatalogs.CreateMinimal());

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog,
                classCatalog: classCatalog, itemCatalog: itemCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                // Startwaffe: keine (Testcharakter wird ohne EquippedWeaponItemId angelegt) - InventorySync
                // beim Join ist leer.
                InventorySyncPacket joinSync = client.Received.OfType<InventorySyncPacket>().First();
                Assert.Empty(joinSync.Slots);
                Assert.Equal(0, joinSync.EquippedWeaponItemId);

                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Count(s => s.Kind == EntityKind.Monster) >= 2, server, client));
                int wildhundId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster && s.TypeId == 1).EntityId;
                int waldschratId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster && s.TypeId == 2).EntityId;

                // Wildhund toeten (1 Treffer reicht, siehe MaxHp) - garantierter Drop.
                client.Send(new AttackRequestPacket { TargetEntityId = wildhundId });
                Assert.True(Pump(() => client.Received.OfType<EntityDiedPacket>().Any(d => d.EntityId == wildhundId), server, client));

                InventorySyncPacket afterDrop = client.Received.OfType<InventorySyncPacket>().Last();
                Assert.Contains(afterDrop.Slots, s => s.ItemId == 1);
                int droppedSlotIndex = afterDrop.Slots.First(s => s.ItemId == 1).SlotIndex;

                // Schaden VOR dem Ausruesten (Basis-Angriff 15, Verteidigung Waldschrat 0) - wiederholt
                // versuchen (Cooldown 50ms lehnt einzelne Versuche ab, das ist erwartet), bis ein Treffer ankommt.
                AttackUntilDamaged(server, client, waldschratId, minDamagePacketCount: 1);
                int damageBeforeEquip = client.Received.OfType<EntityDamagedPacket>().First(d => d.TargetEntityId == waldschratId).Damage;
                Assert.Equal(15, damageBeforeEquip);

                // Ausruesten: InventorySync (Waffe jetzt ausgeruestet) UND AppearanceChanged kommen an.
                client.Send(new EquipItemRequestPacket { InventorySlotIndex = droppedSlotIndex });
                Assert.True(Pump(() => client.Received.OfType<InventorySyncPacket>().Any(s => s.EquippedWeaponItemId == 1), server, client));
                Assert.Contains(client.Received.OfType<AppearanceChangedPacket>(), a => a.Appearance.WeaponId == 1); // LookId aus dem Item.

                // Schaden NACH dem Ausruesten - um genau den AttackBonus (50) hoeher.
                AttackUntilDamaged(server, client, waldschratId, minDamagePacketCount: 2);
                int damageAfterEquip = client.Received.OfType<EntityDamagedPacket>().Last(d => d.TargetEntityId == waldschratId).Damage;
                Assert.Equal(damageBeforeEquip + 50, damageAfterEquip);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- Bugfix nach M5a: EntityHpChangedPacket (HP-Änderung ohne Treffer) ----

        [Fact]
        public void UsingConsumable_SendsEntityHpChangedWithHealedValue()
        {
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 800, ""baseHp"": 100, ""baseSp"": 0, ""baseAttack"": 0, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 9999, ""attack"": 10, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 150, ""attackCooldownMs"": 999999, ""aggressive"": true, ""aggroRangeWire"": 1000, ""leashRangeWire"": 2000, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 999999 } ]
            }", mobCatalog, openMap);
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(@"{
              ""items"": [ { ""id"": 1, ""name"": ""Testtrank"", ""itemType"": ""Consumable"", ""healAmount"": 5 } ]
            }", TestLookCatalogs.CreateMinimal());

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog,
                classCatalog: classCatalog, itemCatalog: itemCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    db.CharacterInventoryItems.Add(new CharacterInventoryItem { CharacterId = characterId, SlotIndex = 0, ItemId = 1 });
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));
                int ownId = client.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                // Der aggressive Wildhund (attackCooldownMs praktisch unendlich - nur EIN Treffer erwartet)
                // schlägt automatisch zu: 100 -> 90 Leben.
                Assert.True(Pump(() => client.Received.OfType<EntityDamagedPacket>().Any(d => d.TargetEntityId == ownId), server, client));
                Assert.Equal(90, client.Received.OfType<EntityDamagedPacket>().First(d => d.TargetEntityId == ownId).TargetHpAfter);

                client.Send(new UseConsumableRequestPacket { InventorySlotIndex = 0 });

                Assert.True(Pump(
                    () => client.Received.OfType<EntityHpChangedPacket>().Any(p => p.EntityId == ownId && p.CurrentHp == 95),
                    server, client));
                EntityHpChangedPacket healed = client.Received.OfType<EntityHpChangedPacket>().First(p => p.EntityId == ownId && p.CurrentHp == 95);
                Assert.Equal(100, healed.MaxHp);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void PlayerRespawn_SendsEntityHpChangedWithMaxHp()
        {
            long fakeNow = 0;
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 800, ""baseHp"": 10, ""baseSp"": 0, ""baseAttack"": 0, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 9999, ""attack"": 999, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 150, ""attackCooldownMs"": 100, ""aggressive"": true, ""aggroRangeWire"": 1000, ""leashRangeWire"": 2000, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 999999 } ]
            }", mobCatalog, openMap);

            var fixture = new ServerFixture(nowMs: () => fakeNow, collisionMap: openMap, mobCatalog: mobCatalog,
                spawnCatalog: spawnCatalog, classCatalog: classCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));
                int ownId = client.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                // Der Wildhund (Angriff 999 gegen 10 Leben) toetet mit dem ersten Treffer - RespawnAtMs wird
                // dabei auf fakeNow (0) + 3000 (World.PlayerRespawnDelayMs) gesetzt.
                Assert.True(Pump(() => client.Received.OfType<EntityDiedPacket>().Any(d => d.EntityId == ownId), server, client));

                // Uhr über die (fixe, nicht konfigurierbare) 3s-Respawn-Frist hinausdrehen statt echt zu warten.
                fakeNow = 3000;
                Assert.True(Pump(() => client.Received.OfType<EntityStopPacket>().Any(s => s.EntityId == ownId), server, client));

                Assert.Contains(client.Received.OfType<EntityHpChangedPacket>(), p => p.EntityId == ownId && p.CurrentHp == 10 && p.MaxHp == 10);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void LaterJoiner_SeesAlreadyDamagedMonster_ReceivesEntityHpChangedAfterSpawn()
        {
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 800, ""baseHp"": 100, ""baseSp"": 0, ""baseAttack"": 15, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 30, ""attack"": 0, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 999999 } ]
            }", mobCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog, classCatalog: classCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var attacker = new TestClient();
                attacker.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, attacker));

                (int attackerCharId, string attackerToken) = fixture.CreateCharacter("Attacker", positionX: 50000, positionY: 50000);
                attacker.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = attackerToken, CharacterId = attackerCharId });
                Assert.True(Pump(() => attacker.Received.OfType<JoinAcceptedPacket>().Any(), server, attacker));

                Assert.True(Pump(() => attacker.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, attacker));
                int monsterId = attacker.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster).EntityId;

                attacker.Send(new AttackRequestPacket { TargetEntityId = monsterId });
                Assert.True(Pump(() => attacker.Received.OfType<EntityDamagedPacket>().Any(d => d.TargetEntityId == monsterId), server, attacker));

                // Erst JETZT joint ein zweiter Spieler - sieht das Monster zum ersten Mal, bereits verletzt
                // (30 -> 15 Leben, Angriff 15 - Verteidigung 0).
                using var joiner = new TestClient();
                joiner.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, joiner, attacker));

                (int joinerCharId, string joinerToken) = fixture.CreateCharacter("Joiner", positionX: 50000, positionY: 50000);
                joiner.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = joinerToken, CharacterId = joinerCharId });
                Assert.True(Pump(() => joiner.Received.OfType<JoinAcceptedPacket>().Any(), server, joiner, attacker));

                Assert.Contains(joiner.Received.OfType<EntitySpawnPacket>(), s => s.EntityId == monsterId);
                Assert.Contains(joiner.Received.OfType<EntityHpChangedPacket>(), p => p.EntityId == monsterId && p.CurrentHp == 15 && p.MaxHp == 30);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M5b: Shop und NPCs (siehe docs/tasks/M5b-shop.md Abschnitt 7) ----

        private const string ShopItemsJson = @"{
  ""items"": [ { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""buyPrice"": 200, ""sellPrice"": 80 } ]
}";

        private const string ShopNpcsJson = @"{
  ""npcs"": [ { ""id"": 1, ""name"": ""Dorfhaendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] } ]
}";

        [Fact]
        public void BotNearNpc_OpensShopBuysAndSells_ReceivesShopContentInventorySyncAndYangChanged()
        {
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(ShopItemsJson, TestLookCatalogs.CreateMinimal());
            NpcCatalog npcCatalog = NpcCatalog.LoadFromJson(ShopNpcsJson, itemCatalog, TestLookCatalogs.CreateMinimal());
            CollisionMap openMap = BuildOpenCollisionMap();
            NpcSpawnCatalog npcSpawnCatalog = NpcSpawnCatalog.LoadFromJson(
                @"{ ""placements"": [ { ""id"": 1, ""npcId"": 1, ""positionX"": 50000, ""positionY"": 50000 } ] }", npcCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, itemCatalog: itemCatalog, npcCatalog: npcCatalog, npcSpawnCatalog: npcSpawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    Character character = db.Characters.Single(c => c.Id == characterId);
                    character.Yang = 500; // Test-Seed - "ausreichend Yang".
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                // Bot sieht den NPC per AOI (EntitySpawn Kind=Npc).
                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Npc), server, client));
                int npcEntityId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Npc).EntityId;

                // OpenShop liefert das Sortiment.
                client.Send(new OpenShopRequestPacket { NpcEntityId = npcEntityId });
                Assert.True(Pump(() => client.Received.OfType<ShopContentPacket>().Any(), server, client));
                ShopContentPacket content = client.Received.OfType<ShopContentPacket>().First();
                Assert.Equal(npcEntityId, content.NpcEntityId);
                Assert.Contains(content.Items, i => i.ItemId == 1 && i.BuyPrice == 200);

                // Vor dem Kauf steht bereits der beim Join verschickte Anfangsstand (Bugfix Yang-Anzeige) fest.
                Assert.Equal(500, client.Received.OfType<YangChangedPacket>().Single().NewTotal);

                // Kauf mit ausreichend Yang -> InventorySync + YangChanged. Auf den KONKRETEN neuen Stand warten
                // (nicht nur "irgendein YangChangedPacket") - der Join-Stand allein würde die Pump-Bedingung
                // sonst schon vor dem eigentlichen Kauf-Paket erfüllen.
                client.Send(new BuyItemRequestPacket { NpcEntityId = npcEntityId, ItemId = 1 });
                Assert.True(Pump(() => client.Received.OfType<YangChangedPacket>().Any(p => p.NewTotal == 300), server, client)); // 500 - 200
                Assert.Contains(client.Received.OfType<InventorySyncPacket>().Last().Slots, s => s.ItemId == 1);
                int boughtSlotIndex = client.Received.OfType<InventorySyncPacket>().Last().Slots.First(s => s.ItemId == 1).SlotIndex;

                // Verkauf desselben Items zurueck -> erneut InventorySync + YangChanged.
                client.Send(new SellItemRequestPacket { NpcEntityId = npcEntityId, InventorySlotIndex = boughtSlotIndex });
                Assert.True(Pump(() => client.Received.OfType<YangChangedPacket>().Any(p => p.NewTotal == 380), server, client)); // 300 + 80
                Assert.DoesNotContain(client.Received.OfType<InventorySyncPacket>().Last().Slots, s => s.ItemId == 1);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void BuyItemRequest_OutOfRange_IsRejectedAndLeavesYangAndInventoryUnchanged()
        {
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(ShopItemsJson, TestLookCatalogs.CreateMinimal());
            NpcCatalog npcCatalog = NpcCatalog.LoadFromJson(ShopNpcsJson, itemCatalog, TestLookCatalogs.CreateMinimal());
            CollisionMap openMap = BuildOpenCollisionMap();
            NpcSpawnCatalog npcSpawnCatalog = NpcSpawnCatalog.LoadFromJson(
                @"{ ""placements"": [ { ""id"": 1, ""npcId"": 1, ""positionX"": 50000, ""positionY"": 50000 } ] }", npcCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, itemCatalog: itemCatalog, npcCatalog: npcCatalog, npcSpawnCatalog: npcSpawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                // Weit genug weg vom NPC (50000,50000), aber noch innerhalb des AOI-Radius (1500) - der Bot
                // sieht den NPC also, ist aber außerhalb NpcInteractRangeWire (300).
                (int characterId, string token) = fixture.CreateCharacter("Bob", positionX: 50000 + 1000, positionY: 50000);
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    Character character = db.Characters.Single(c => c.Id == characterId);
                    character.Yang = 500;
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Npc), server, client));
                int npcEntityId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Npc).EntityId;

                client.Send(new BuyItemRequestPacket { NpcEntityId = npcEntityId, ItemId = 1 });
                Assert.True(Pump(() => client.Received.OfType<ShopActionFailedPacket>().Any(), server, client));

                Assert.Equal(ShopActionFailedReason.OutOfRange, client.Received.OfType<ShopActionFailedPacket>().First().Reason);
                // Nur der Join-Anfangsstand (Bugfix Yang-Anzeige, 500 unveraendert) - keine zusaetzliche
                // Gutschrift/Abbuchung durch den abgelehnten Kauf.
                Assert.Equal(500, client.Received.OfType<YangChangedPacket>().Single().NewTotal);
                Assert.Empty(client.Received.OfType<InventorySyncPacket>().SelectMany(p => p.Slots));
            }
            finally
            {
                server.Stop();
            }
        }

        /// <summary>Regression: der Include(InventoryItems)-Fehler aus M5a (siehe Bugfix-Sektion oben, "Fund -
        /// Include(InventoryItems) fehlte beim Join") darf nach dem kombinierten CharacterSaveRequest-Channel
        /// (Abschnitt 0d/5) nicht zurückkehren - nach Kauf, Disconnect und erneutem Join müssen Yang UND
        /// Inventar exakt wie vor dem Disconnect sein.</summary>
        [Fact]
        public void AfterBuy_DisconnectAndRejoin_YangAndInventoryPersistExactly()
        {
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(ShopItemsJson, TestLookCatalogs.CreateMinimal());
            NpcCatalog npcCatalog = NpcCatalog.LoadFromJson(ShopNpcsJson, itemCatalog, TestLookCatalogs.CreateMinimal());
            CollisionMap openMap = BuildOpenCollisionMap();
            NpcSpawnCatalog npcSpawnCatalog = NpcSpawnCatalog.LoadFromJson(
                @"{ ""placements"": [ { ""id"": 1, ""npcId"": 1, ""positionX"": 50000, ""positionY"": 50000 } ] }", npcCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, itemCatalog: itemCatalog, npcCatalog: npcCatalog, npcSpawnCatalog: npcSpawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                int characterId;
                string token;

                using (var client = new TestClient())
                {
                    client.Connect(server.ListenPort);
                    Assert.True(WaitConnected(server, client));

                    (characterId, token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                    using (LumiisDbContext db = fixture.OpenDb())
                    {
                        Character character = db.Characters.Single(c => c.Id == characterId);
                        character.Yang = 500;
                        db.SaveChanges();
                    }

                    client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                    Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Npc), server, client));
                    int npcEntityId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Npc).EntityId;

                    client.Send(new BuyItemRequestPacket { NpcEntityId = npcEntityId, ItemId = 1 });
                    Assert.True(Pump(() => client.Received.OfType<InventorySyncPacket>().Any(s => s.Slots.Any(slot => slot.ItemId == 1)), server, client));

                    client.Peer.Disconnect();
                    Assert.True(Pump(() => client.Disconnected, server, client));
                }

                // Die kombinierte Speicherung (CharacterSaveRequest, Abschnitt 5) wurde bereits beim Kauf
                // selbst angestoßen (nicht erst bei Disconnect) - hier nur abwarten, bis der
                // Hintergrund-Consumer sie tatsächlich verarbeitet hat.
                Assert.True(Pump(() =>
                {
                    using LumiisDbContext db = fixture.OpenDb();
                    Character? character = db.Characters.Include(c => c.InventoryItems).SingleOrDefault(c => c.Id == characterId);
                    return character != null && character.Yang == 300 && character.InventoryItems.Any(i => i.ItemId == 1);
                }, server));

                // Erneuter Join mit einer neuen Verbindung - InventorySync muss das Item wieder zeigen.
                using var rejoinClient = new TestClient();
                rejoinClient.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, rejoinClient));
                rejoinClient.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => rejoinClient.Received.OfType<InventorySyncPacket>().Any(), server, rejoinClient));

                InventorySyncPacket rejoinSync = rejoinClient.Received.OfType<InventorySyncPacket>().First();
                Assert.Contains(rejoinSync.Slots, s => s.ItemId == 1);

                // Bugfix Yang-Anzeige: der Anfangsstand beim erneuten Join muss dem gespeicherten Stand
                // entsprechen (300, nicht 0 oder der alte 500er Stand von vor dem Kauf).
                Assert.Equal(300, rejoinClient.Received.OfType<YangChangedPacket>().Single().NewTotal);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- Bugfix: Yang-Anzeige beim Join (kein Anfangswert ohne YangChangedPacket direkt nach Join) ----

        [Fact]
        public void Join_WithStoredYang_SendsYangChangedWithStoredValueRightAfterJoin()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                using (LumiisDbContext db = fixture.OpenDb())
                {
                    Character character = db.Characters.Single(c => c.Id == characterId);
                    character.Yang = 1234;
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<YangChangedPacket>().Any(), server, client));

                Assert.True(client.Received.OfType<JoinAcceptedPacket>().Any());
                Assert.Equal(1234, client.Received.OfType<YangChangedPacket>().Single().NewTotal);
            }
            finally
            {
                server.Stop();
            }
        }

        /// <summary>Einheitliches Verhalten, kein Sonderfall für Yang=0 (siehe docs/tasks-Bugfix-Auftrag) - ein
        /// frisch erstellter Charakter (Yang startet bei 0, siehe Character.Yang-Default) bekommt genauso ein
        /// YangChangedPacket wie einer mit einem echten Stand.</summary>
        [Fact]
        public void Join_WithZeroYang_StillSendsYangChanged()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice"); // Yang bleibt beim DB-Default 0.
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Assert.Equal(0, client.Received.OfType<YangChangedPacket>().Single().NewTotal);
            }
            finally
            {
                server.Stop();
            }
        }

        /// <summary>Gleiche Fehlerklasse, zweite betroffene Stelle aus dem Audit: JoinAcceptedPacket trägt kein
        /// Leben, die eigene Lebensanzeige blieb bis zum ersten Treffer auf "-" stehen (Entity.CurrentHp wird
        /// nie aus der DB geladen, eine frische Entity startet immer bei MaxHp - siehe World.TryJoin) - jetzt
        /// schickt CompleteJoin unbedingt ein EntityHpChangedPacket für die eigene Entity mit.</summary>
        [Fact]
        public void Join_SendsEntityHpChangedForOwnEntity()
        {
            var fixture = new ServerFixture();
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice");
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));
                int ownEntityId = client.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                Assert.True(Pump(() => client.Received.OfType<EntityHpChangedPacket>().Any(p => p.EntityId == ownEntityId), server, client));
                EntityHpChangedPacket hp = client.Received.OfType<EntityHpChangedPacket>().Single(p => p.EntityId == ownEntityId);
                Assert.Equal(hp.MaxHp, hp.CurrentHp); // frische Entity, immer volles Leben.
                Assert.True(hp.MaxHp > 0);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- Dev-Tools: Debug-Spawn (siehe docs/tasks/dev-tools-debug-spawn.md) ----

        private static MobCatalog BuildSingleWildhundMobCatalog()
        {
            return MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 30, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
        }

        [Fact]
        public void DebugSpawnMonsterRequest_WithoutAllowDebugCommands_HasNoEffect()
        {
            MobCatalog mobCatalog = BuildSingleWildhundMobCatalog();
            CollisionMap openMap = BuildOpenCollisionMap();

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, allowDebugCommands: false);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                client.Send(new DebugSpawnMonsterRequestPacket { MobId = 1 });
                PumpForDuration(TimeSpan.FromMilliseconds(500), server, client);

                Assert.DoesNotContain(client.Received, p => p is EntitySpawnPacket spawn && spawn.Kind == EntityKind.Monster);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void DebugSpawnMonsterRequest_WithAllowDebugCommands_SpawnsAttackableMonsterNearSender()
        {
            MobCatalog mobCatalog = BuildSingleWildhundMobCatalog();
            CollisionMap openMap = BuildOpenCollisionMap();

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, allowDebugCommands: true);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                client.Send(new DebugSpawnMonsterRequestPacket { MobId = 1 });
                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, client));

                EntitySpawnPacket monsterSpawn = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster);
                long dx = monsterSpawn.X - 50000;
                long dy = monsterSpawn.Y - 50000;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                Assert.InRange(distance, 0, 350); // 100-300 Wire-Einheiten Versatz, etwas Toleranz fuer Rundung

                // Normal angreifbar - bestehende TryAttack-Logik unveraendert, kein Sonderfall fuer
                // Debug-Monster. EntityDamaged (falls zufaellig in Reichweite) oder AttackFailed (sonst,
                // z. B. OutOfRange) belegen beide, dass der normale Kampf-Pfad greift statt stillzuschweigen.
                client.Send(new AttackRequestPacket { TargetEntityId = monsterSpawn.EntityId });
                Assert.True(Pump(() =>
                    client.Received.OfType<EntityDamagedPacket>().Any(d => d.TargetEntityId == monsterSpawn.EntityId) ||
                    client.Received.OfType<AttackFailedPacket>().Any(),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M4b: Monster-Respawn (siehe docs/tasks/M4b-monster-respawn.md) ----

        [Fact]
        public void KillingSpawnPointMonster_RespawnsAtSameLocationAfterConfiguredDelay()
        {
            MobCatalog mobCatalog = BuildSingleWildhundMobCatalog();
            CollisionMap openMap = BuildOpenCollisionMap();
            // Kurze RespawnSeconds (synthetisch, nicht die echten Daten aus data/) - haelt den Test schnell,
            // echte Uhr (siehe ServerFixture ohne nowMs-Override).
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 2 } ]
            }", mobCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, client));
                int firstMonsterId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster).EntityId;

                // Wiederholt angreifen (Cooldown, siehe M4a-Testmuster) bis das Monster stirbt.
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                bool died = false;
                while (DateTime.UtcNow < deadline && !died)
                {
                    client.Send(new AttackRequestPacket { TargetEntityId = firstMonsterId });
                    server.Update();
                    client.Poll();
                    died = client.Received.OfType<EntityDiedPacket>().Any(d => d.EntityId == firstMonsterId);
                    Thread.Sleep(15);
                }
                Assert.True(died);
                Assert.True(Pump(() => client.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == firstMonsterId), server, client));

                // Jetzt ueber die (kurze, synthetische) Respawn-Zeit hinweg warten.
                Assert.True(Pump(() =>
                    client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster && s.EntityId != firstMonsterId),
                    server, client));

                EntitySpawnPacket respawn = client.Received.OfType<EntitySpawnPacket>()
                    .First(s => s.Kind == EntityKind.Monster && s.EntityId != firstMonsterId);
                Assert.Equal(1, respawn.TypeId);
                Assert.Equal("Wildhund", respawn.Name);
                Assert.Equal(50000, respawn.X);
                Assert.Equal(50000, respawn.Y);
            }
            finally
            {
                server.Stop();
            }
        }

        // ---- M4c: Monster-Aggro (siehe docs/tasks/M4c-monster-aggro.md) ----

        [Fact]
        public void PlayerNearAggressiveWildhund_GetsAttackedWithoutAttacking()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 30, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 300, ""aggressive"": true, ""aggroRangeWire"": 400, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 1, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 30 } ]
            }", mobCatalog, openMap);

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));
                int myEntityId = client.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, client));
                int wildhundId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster).EntityId;

                // Client sendet bewusst NIE ein AttackRequestPacket - der Wildhund muss von sich aus angreifen.
                Assert.True(Pump(() =>
                    client.Received.OfType<EntityDamagedPacket>().Any(d => d.AttackerEntityId == wildhundId && d.TargetEntityId == myEntityId),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void PlayerAttacksPassiveWaldschratOnce_ThenGetsAttackedBackWithoutAttackingAgain()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(@"{
              ""mobs"": [
                { ""id"": 2, ""name"": ""Waldschrat"", ""speedWire"": 200, ""wanderRadiusWire"": 300, ""wanderIntervalMinMs"": 999999, ""wanderIntervalMaxMs"": 999999, ""maxHp"": 60, ""attack"": 8, ""defense"": 3, ""yangMin"": 10, ""yangMax"": 25, ""meleeRangeWire"": 150, ""attackCooldownMs"": 300, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 600, ""level"": 3, ""baseXp"": 30 }
              ]
            }");
            CollisionMap openMap = BuildOpenCollisionMap();
            SpawnCatalog spawnCatalog = SpawnCatalog.LoadFromJson(@"{
              ""spawns"": [ { ""id"": 1, ""mobId"": 2, ""positionX"": 50000, ""positionY"": 50000, ""respawnSeconds"": 60 } ]
            }", mobCatalog, openMap);
            // Langer eigener Cooldown stellt sicher, dass ein spaeterer Treffer beim Client wirklich vom
            // Waldschrat kommt - der Client selbst koennte in diesem Zeitraum gar nicht erneut treffen.
            ClassCatalog classCatalog = ClassCatalog.LoadFromJson(@"{
              ""classes"": [ { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 5000, ""baseHp"": 100, ""baseSp"": 0, ""baseAttack"": 15, ""baseDefense"": 0, ""baseSt"": 1, ""baseHt"": 1, ""baseDx"": 1, ""baseIq"": 1, ""hpPerHt"": 0, ""hpPerLevel"": 0, ""spPerIq"": 0, ""spPerLevel"": 0, ""attackPerSt"": 0, ""htPerDefense"": 1000, ""dodgeBpPerDx"": 0, ""dodgeCapBp"": 0, ""statPointsPerLevel"": 0, ""skillPointsPerLevel"": 0, ""regenDelayMs"": 999999, ""regenIntervalMs"": 999999, ""hpRegenPercent"": 0, ""spRegenPercent"": 0 } ]
            }");

            var fixture = new ServerFixture(collisionMap: openMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog, classCatalog: classCatalog);
            GameServer server = fixture.Server;
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                (int characterId, string token) = fixture.CreateCharacter("Alice", positionX: 50000, positionY: 50000);
                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));
                int myEntityId = client.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                Assert.True(Pump(() => client.Received.OfType<EntitySpawnPacket>().Any(s => s.Kind == EntityKind.Monster), server, client));
                int waldschratId = client.Received.OfType<EntitySpawnPacket>().First(s => s.Kind == EntityKind.Monster).EntityId;

                // Genau EIN Angriff - danach nie wieder.
                client.Send(new AttackRequestPacket { TargetEntityId = waldschratId });
                Assert.True(Pump(() => client.Received.OfType<EntityDamagedPacket>().Any(d => d.AttackerEntityId == myEntityId), server, client));

                // Client greift bewusst nicht erneut an - der Waldschrat muss jetzt von sich aus zurueckschlagen.
                Assert.True(Pump(() =>
                    client.Received.OfType<EntityDamagedPacket>().Any(d => d.AttackerEntityId == waldschratId && d.TargetEntityId == myEntityId),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
