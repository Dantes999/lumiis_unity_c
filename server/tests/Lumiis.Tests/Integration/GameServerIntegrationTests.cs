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

        private static GameServer CreateServer()
        {
            var config = new ServerConfig
            {
                Port = 0, // freier Port, vom OS vergeben
                MaxPlayers = 10,
                RejectGraceTicks = 3
            };
            return new GameServer(config, new NullLogger());
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
    }
}
