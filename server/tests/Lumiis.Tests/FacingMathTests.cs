using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests
{
    public class FacingMathTests
    {
        private static readonly Facing DownFacing = new Facing(FacingDirection.Down, false);
        private static readonly Facing SideRightFacing = new Facing(FacingDirection.Side, false);
        private static readonly Facing SideLeftFacing = new Facing(FacingDirection.Side, true);

        [Fact]
        public void ZeroVector_KeepsPrevious()
        {
            Facing result = FacingMath.FromDelta(0.0, 0.0, SideLeftFacing);
            Assert.Equal(SideLeftFacing.Direction, result.Direction);
            Assert.Equal(SideLeftFacing.FlipX, result.FlipX);
        }

        // Alle vier Quadranten, jeweils mit klar dominanter Achse (kein Hysterese-Grenzfall).
        [Theory]
        [InlineData(10, 1, FacingDirection.Side, false)]   // rechts-oben, dx dominiert
        [InlineData(10, -1, FacingDirection.Side, false)]  // rechts-unten, dx dominiert
        [InlineData(-10, 1, FacingDirection.Side, true)]   // links-oben, dx dominiert
        [InlineData(-10, -1, FacingDirection.Side, true)]  // links-unten, dx dominiert
        [InlineData(1, 10, FacingDirection.Up, false)]     // rechts-oben, dy dominiert
        [InlineData(-1, 10, FacingDirection.Up, false)]    // links-oben, dy dominiert
        [InlineData(1, -10, FacingDirection.Down, false)]  // rechts-unten, dy dominiert
        [InlineData(-1, -10, FacingDirection.Down, false)] // links-unten, dy dominiert
        public void AllQuadrants_PickDominantAxis(double dx, double dy, FacingDirection expectedDirection, bool expectedFlipX)
        {
            Facing result = FacingMath.FromDelta(dx, dy, DownFacing);
            Assert.Equal(expectedDirection, result.Direction);
            Assert.Equal(expectedFlipX, result.FlipX);
        }

        [Fact]
        public void PureHorizontal_IsSide()
        {
            Facing result = FacingMath.FromDelta(5.0, 0.0, DownFacing);
            Assert.Equal(FacingDirection.Side, result.Direction);
            Assert.False(result.FlipX);
        }

        [Fact]
        public void PureVertical_IsUpOrDown()
        {
            Assert.Equal(FacingDirection.Up, FacingMath.FromDelta(0.0, 5.0, SideRightFacing).Direction);
            Assert.Equal(FacingDirection.Down, FacingMath.FromDelta(0.0, -5.0, SideRightFacing).Direction);
        }

        [Fact]
        public void Hysteresis_StaysSide_WhenNotClearlyVertical()
        {
            // dy(11) < dx(10) * 1.2 = 12 -> bleibt Side, obwohl |dy| > |dx|.
            Facing result = FacingMath.FromDelta(10.0, 11.0, SideRightFacing);
            Assert.Equal(FacingDirection.Side, result.Direction);
        }

        [Fact]
        public void Hysteresis_SwitchesToVertical_WhenClearlyDominant()
        {
            // dy(13) > dx(10) * 1.2 = 12 -> wechselt zu vertikal.
            Facing result = FacingMath.FromDelta(10.0, 13.0, SideRightFacing);
            Assert.Equal(FacingDirection.Up, result.Direction);
        }

        [Fact]
        public void Hysteresis_StaysVertical_WhenNotClearlyHorizontal()
        {
            // dx(10) < dy(9) * 1.2 = 10.8 -> bleibt vertikal.
            Facing result = FacingMath.FromDelta(10.0, 9.0, DownFacing);
            Assert.NotEqual(FacingDirection.Side, result.Direction);
        }

        [Fact]
        public void Hysteresis_SwitchesToSide_WhenClearlyDominant()
        {
            // dx(13) > dy(10) * 1.2 = 12 -> wechselt zu Side.
            Facing result = FacingMath.FromDelta(13.0, 10.0, DownFacing);
            Assert.Equal(FacingDirection.Side, result.Direction);
            Assert.False(result.FlipX);
        }
    }
}
