using System;
using System.Linq;
using System.Threading;
using LiteNetLib;
using Lumiis.Data;
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

            public ServerFixture(Func<long>? nowMs = null, Func<long>? tokenNowUnixSeconds = null)
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
                Server = new GameServer(config, new NullLogger(), TokenService, _dbFactory, nowMs);
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
    }
}
