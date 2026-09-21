using Lumiis.Server.Game;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class EntityMovementTests
    {
        [Fact]
        public void Tick_ArrivesExactlyAtTarget_NoOvershoot()
        {
            var entity = new Entity(1, "Bot", 0, 0);
            entity.MoveTo(1000, 0, 500);

            bool arrived = entity.Tick(3.0); // mehr Zeit als nötig (2s bei Speed 500)

            Assert.True(arrived);
            Assert.False(entity.IsMoving);
            Assert.Equal(1000, entity.IntX);
            Assert.Equal(0, entity.IntY);
        }

        [Fact]
        public void Tick_StepByStepAt25Hz_ArrivesExactlyWithoutOvershoot()
        {
            var entity = new Entity(1, "Bot", 0, 0);
            entity.MoveTo(1000, 500, 500);

            const double deltaSeconds = 1.0 / 25.0;
            bool arrived = false;
            int ticks = 0;
            while (!arrived && ticks < 1000)
            {
                arrived = entity.Tick(deltaSeconds);
                ticks++;
                Assert.InRange(entity.IntX, 0, 1000);
                Assert.InRange(entity.IntY, 0, 500);
            }

            Assert.True(arrived);
            Assert.Equal(1000, entity.IntX);
            Assert.Equal(500, entity.IntY);
        }

        [Fact]
        public void Tick_Diagonal_MovesProportionallyOnBothAxes()
        {
            var entity = new Entity(1, "Bot", 0, 0);
            entity.MoveTo(1000, 1000, 500);

            bool arrived = entity.Tick(1.0);

            Assert.False(arrived);
            Assert.True(entity.IsMoving);
            Assert.Equal(entity.X, entity.Y, 3);
            double distanceMoved = System.Math.Sqrt(entity.X * entity.X + entity.Y * entity.Y);
            Assert.Equal(500.0, distanceMoved, 3);
        }

        [Fact]
        public void Tick_RespectsSpeed()
        {
            var slow = new Entity(1, "Slow", 0, 0);
            slow.MoveTo(1000, 0, 100);
            slow.Tick(1.0);
            Assert.Equal(100, slow.IntX);

            var fast = new Entity(2, "Fast", 0, 0);
            fast.MoveTo(1000, 0, 400);
            fast.Tick(1.0);
            Assert.Equal(400, fast.IntX);
        }

        [Fact]
        public void Tick_WhileNotMoving_DoesNothing()
        {
            var entity = new Entity(1, "Bot", 10, 20);

            bool arrived = entity.Tick(1.0);

            Assert.False(arrived);
            Assert.Equal(10, entity.IntX);
            Assert.Equal(20, entity.IntY);
        }
    }
}
