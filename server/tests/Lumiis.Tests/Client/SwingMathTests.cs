using Lumiis.Client;
using Xunit;

namespace Lumiis.Tests.Client
{
    public class SwingMathTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Strike_SweepsFromStartToEndOfArc(int combo)
        {
            float atStrikeEnd = SwingMath.BladeOffsetDegrees(SwingMath.WindupSeconds + SwingMath.StrikeSeconds - 0.0001f, combo);
            Assert.InRange(atStrikeEnd, SwingMath.EndDegrees(combo) - 1f, SwingMath.EndDegrees(combo) + 1f);
            Assert.Equal(-SwingMath.StartDegrees(combo), SwingMath.EndDegrees(combo));
        }

        [Fact]
        public void Combo1_SwingsOppositeDirectionOfCombo0()
        {
            Assert.True(SwingMath.StartDegrees(0) > 0);
            Assert.True(SwingMath.StartDegrees(1) < 0);
        }

        [Theory]
        [InlineData(0, 60f)]
        [InlineData(0, 0f)]
        [InlineData(0, -60f)]
        [InlineData(1, -60f)]
        [InlineData(1, 45f)]
        public void ContactSeconds_IsWhenBladeReachesTargetAngle(int combo, float targetOffset)
        {
            float contact = SwingMath.ContactSeconds(targetOffset, combo);

            Assert.True(SwingMath.IsStriking(contact) || contact == SwingMath.WindupSeconds + SwingMath.StrikeSeconds);
            Assert.InRange(SwingMath.BladeOffsetDegrees(contact, combo), targetOffset - 1.5f, targetOffset + 1.5f);
        }

        [Fact]
        public void ContactSeconds_TargetEarlierInSweep_IsHitEarlier()
        {
            // Kombo 0 schlägt von +82° nach -82°: ein Ziel links der Zielrichtung wird zuerst getroffen.
            Assert.True(SwingMath.ContactSeconds(50f, 0) < SwingMath.ContactSeconds(-50f, 0));
            Assert.True(SwingMath.ContactSeconds(-50f, 1) < SwingMath.ContactSeconds(50f, 1));
        }

        [Fact]
        public void ContactSeconds_OutsideArc_ClampsToArcEdge()
        {
            Assert.Equal(SwingMath.ContactSeconds(-SwingMath.HalfArcDegrees, 0), SwingMath.ContactSeconds(-170f, 0), 4);
            Assert.InRange(SwingMath.ContactSeconds(400f, 0), SwingMath.WindupSeconds, SwingMath.WindupSeconds + SwingMath.StrikeSeconds);
        }

        [Fact]
        public void Blade_MovesFastestAtStartOfStrike()
        {
            float early = SwingMath.StrikeProgress(0.2f) - SwingMath.StrikeProgress(0.1f);
            float late = SwingMath.StrikeProgress(0.9f) - SwingMath.StrikeProgress(0.8f);
            Assert.True(early > late * 3f);
        }

        [Theory]
        [InlineData(190f, -170f)]
        [InlineData(-190f, 170f)]
        [InlineData(360f, 0f)]
        public void NormalizeDegrees_WrapsToHalfOpenRange(float input, float expected)
        {
            Assert.Equal(expected, SwingMath.NormalizeDegrees(input), 3);
        }
    }
}
