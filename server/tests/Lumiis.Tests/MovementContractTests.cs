using Lumiis.Server.Game;
using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests
{
    /// <summary>Vertragstest: Serverbewegung (Entity.Tick, inkrementell in Ticks) und MovementMath (absolut in
    /// elapsedSeconds, für den Unity-Client) müssen nach N Ticks auf 0,01 Einheiten übereinstimmen.</summary>
    public class MovementContractTests
    {
        [Theory]
        [InlineData(0, 0, 1000, 500, 500)]
        [InlineData(0, 0, 1000, 1000, 500)]
        [InlineData(100, 200, 50000, 3000, 500)]
        public void ServerTickAndMovementMath_AgreeAfterEveryTick(int startX, int startY, int targetX, int targetY, int speed)
        {
            var entity = new Entity(1, "Bot", startX, startY);
            entity.MoveTo(targetX, targetY, speed);

            double deltaSeconds = 1.0 / Protocol.TickRate; // "N Ticks à 1000/Protocol.TickRate ms"
            bool serverArrived = false;
            int ticks = 0;

            while (!serverArrived && ticks < 5000)
            {
                serverArrived = entity.Tick(deltaSeconds);
                ticks++;

                double elapsedSeconds = ticks * deltaSeconds;
                MovementSample sample = MovementMath.Evaluate(startX, startY, targetX, targetY, speed, elapsedSeconds);

                Assert.Equal(entity.X, sample.X, 2); // 2 Nachkommastellen = 0,01 Einheiten = 1 in 1/100
                Assert.Equal(entity.Y, sample.Y, 2);
                Assert.Equal(serverArrived, sample.Arrived);
            }

            Assert.True(serverArrived);
        }
    }
}
