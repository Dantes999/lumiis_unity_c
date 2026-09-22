using System;
using System.IO;
using System.Linq;
using System.Threading;
using LiteNetLib;
using Lumiis.Data;
using Lumiis.Data.Maps;
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

            public ServerFixture(Func<long>? nowMs = null, Func<long>? tokenNowUnixSeconds = null, CollisionMap? collisionMap = null)
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
                    RejectGraceMs = 100 // echte Uhr, kleiner Wert - siehe M1-fixes.md Punkt 2
                };
                Server = new GameServer(config, new NullLogger(), TokenService, _dbFactory, nowMs, collisionMap);
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
                    character.ArmorId = 4;
                    character.HelmetId = 1;
                    character.WeaponId = 2;
                    character.ShieldId = 1;
                    db.SaveChanges();
                }

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = token, CharacterId = characterId });
                Assert.True(Pump(() => client.Received.OfType<JoinAcceptedPacket>().Any(), server, client));

                Appearance appearance = client.Received.OfType<JoinAcceptedPacket>().First().Appearance;
                Assert.Equal(1, appearance.BodyId);
                Assert.Equal(2, appearance.HairStyleId);
                Assert.Equal(3, appearance.HairColorId);
                Assert.Equal(4, appearance.ArmorId);
                Assert.Equal(1, appearance.HelmetId);
                Assert.Equal(2, appearance.WeaponId);
                Assert.Equal(1, appearance.ShieldId);
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
    }
}
