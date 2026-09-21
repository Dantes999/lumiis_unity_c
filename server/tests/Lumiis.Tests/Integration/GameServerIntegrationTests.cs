using System;
using System.Linq;
using System.Threading;
using LiteNetLib;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Integration
{
    /// <summary>Echtes LiteNetLib auf Loopback (freier Port). Server und Clients werden manuell gepumpt, mit Timeout statt festen Sleeps.</summary>
    public class GameServerIntegrationTests
    {
        private sealed class NullLogger : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
        }

        private static GameServer CreateServer(Func<long>? nowMs = null)
        {
            var config = new ServerConfig
            {
                Port = 0, // freier Port, vom OS vergeben
                MaxPlayers = 10,
                RejectGraceMs = 100 // echte Uhr, kleiner Wert - siehe M1-fixes.md Punkt 2
            };
            return new GameServer(config, new NullLogger(), nowMs);
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
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);

                Assert.True(WaitConnected(server, clientA, clientB));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bob" });
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
        public void ExistingClient_SeesLaterJoinerViaBroadcast()
        {
            // Deckt den Fall ab, der bei TwoClients_JoinAndSeeEachOthersSpawn NICHT geprüft wird:
            // A ist schon drin, B joint SPÄTER - kommt bei A ein EntitySpawn für B per Broadcast an?
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var clientA = new TestClient();
                clientA.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA));

                using var clientB = new TestClient();
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bob" });
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
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var clientA = new TestClient();
                clientA.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA));

                using var bot1 = new TestClient();
                using var bot2 = new TestClient();
                using var bot3 = new TestClient();
                bot1.Connect(server.ListenPort);
                bot2.Connect(server.ListenPort);
                bot3.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, bot1, bot2, bot3));

                bot1.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bot1" });
                bot2.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bot2" });
                bot3.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bot3" });

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
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bob" });
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
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                int aliceId = clientA.Received.OfType<JoinAcceptedPacket>().First().EntityId;

                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bob" });
                Assert.True(Pump(() => clientB.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));

                clientA.Peer!.Disconnect();
                Assert.True(Pump(
                    () => clientB.Received.OfType<EntityDespawnPacket>().Any(d => d.EntityId == aliceId),
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
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version + 1, Name = "Alice" });

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
        public void InvalidName_IsRejected()
        {
            GameServer server = CreateServer();
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "ab" });

                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.InvalidName),
                    server, client));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void GarbagePacket_DisconnectsClient_ServerStaysAlive()
        {
            GameServer server = CreateServer();
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

                goodClient.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
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
            GameServer server = CreateServer();
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

                goodClient.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
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
            GameServer server = CreateServer();
            server.TestFaultHook = () => throw new InvalidOperationException("Simulierter Fehler im Handler");
            server.Start();
            try
            {
                using var clientA = new TestClient();
                using var clientB = new TestClient();
                clientA.Connect(server.ListenPort);
                clientB.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, clientA, clientB));

                clientA.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Alice" });
                Assert.True(Pump(() => clientA.Received.OfType<JoinAcceptedPacket>().Any(), server, clientA, clientB));
                clientB.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "Bob" });
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
            const int joinTimeoutMs = 5000; // ServerConfig.JoinTimeoutMs-Default aus CreateServer()
            GameServer server = CreateServer(nowMs: () => fakeNow);
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
            GameServer server = CreateServer(nowMs: () => fakeNow);
            server.Start();
            try
            {
                using var client = new TestClient();
                client.Connect(server.ListenPort);
                Assert.True(WaitConnected(server, client));

                client.Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "ab" }); // ungültiger Name
                Assert.True(Pump(
                    () => client.Received.OfType<JoinRejectedPacket>().Any(r => r.Reason == JoinRejectReason.InvalidName),
                    server, client));

                // JoinRejected ist angekommen, Grace-Frist läuft noch (Uhr steht still) - noch nicht getrennt.
                for (int i = 0; i < 5; i++) server.Update();
                client.Poll();
                Assert.False(client.Disconnected);

                // Uhr über die Grace-Frist hinausdrehen - jetzt trennt der nächste Tick.
                fakeNow = 100 + 1; // RejectGraceMs aus CreateServer()
                Assert.True(Pump(() => client.Disconnected, server, client));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
