using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests
{
    public class MovementMathTests
    {
        [Fact]
        public void AtStart_ReturnsStartPosition()
        {
            MovementSample sample = MovementMath.Evaluate(0, 0, 1000, 0, 500, 0.0);
            Assert.Equal(0, sample.X, 3);
            Assert.Equal(0, sample.Y, 3);
            Assert.False(sample.Arrived);
        }

        [Fact]
        public void AtMidpoint_ReturnsHalfwayPosition()
        {
            // Distanz 1000, Speed 500 -> travelSeconds = 2, Mitte bei t=1.
            MovementSample sample = MovementMath.Evaluate(0, 0, 1000, 0, 500, 1.0);
            Assert.Equal(500, sample.X, 3);
            Assert.Equal(0, sample.Y, 3);
            Assert.False(sample.Arrived);
        }

        [Fact]
        public void AtExactArrival_ReturnsTargetAndArrived()
        {
            MovementSample sample = MovementMath.Evaluate(0, 0, 1000, 0, 500, 2.0);
            Assert.Equal(1000, sample.X, 3);
            Assert.Equal(0, sample.Y, 3);
            Assert.True(sample.Arrived);
        }

        [Fact]
        public void LongAfterArrival_StaysAtTarget_NoOvershoot()
        {
            MovementSample sample = MovementMath.Evaluate(0, 0, 1000, 0, 500, 100.0);
            Assert.Equal(1000, sample.X, 3);
            Assert.Equal(0, sample.Y, 3);
            Assert.True(sample.Arrived);
        }

        [Fact]
        public void ZeroDistance_IsImmediatelyArrived()
        {
            MovementSample sample = MovementMath.Evaluate(500, 500, 500, 500, 500, 0.0);
            Assert.Equal(500, sample.X, 3);
            Assert.Equal(500, sample.Y, 3);
            Assert.True(sample.Arrived);
        }

        [Fact]
        public void ZeroOrNegativeSpeed_IsImmediatelyArrived()
        {
            MovementSample zeroSpeed = MovementMath.Evaluate(0, 0, 1000, 1000, 0, 1.0);
            Assert.Equal(1000, zeroSpeed.X, 3);
            Assert.Equal(1000, zeroSpeed.Y, 3);
            Assert.True(zeroSpeed.Arrived);

            MovementSample negativeSpeed = MovementMath.Evaluate(0, 0, 1000, 1000, -100, 1.0);
            Assert.True(negativeSpeed.Arrived);
        }

        [Fact]
        public void Diagonal_MovesProportionallyOnBothAxes()
        {
            MovementSample sample = MovementMath.Evaluate(0, 0, 1000, 1000, 500, 1.0);
            Assert.Equal(sample.X, sample.Y, 3);
            double distanceMoved = System.Math.Sqrt(sample.X * sample.X + sample.Y * sample.Y);
            Assert.Equal(500.0, distanceMoved, 3);
            Assert.False(sample.Arrived);
        }
    }
}
