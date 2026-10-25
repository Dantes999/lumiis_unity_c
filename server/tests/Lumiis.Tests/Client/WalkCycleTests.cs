using Lumiis.Client;
using Xunit;

namespace Lumiis.Tests.Client;

public class WalkCycleTests
{
    [Fact]
    public void Frames_ChangeAtEvenIntervals()
    {
        // Jedes Bild steht gleich lange: bei 9 Bildern/s genau 1/9 s, unabhängig von der Stelle im Zyklus.
        float phase = 0f;
        int last = WalkCycle.Frame(phase);
        int framesHeld = 0;
        var holds = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 900; i++)
        {
            phase = WalkCycle.Advance(phase, 0.001f, 500);
            int frame = WalkCycle.Frame(phase);
            framesHeld++;
            if (frame != last)
            {
                holds.Add(framesHeld);
                framesHeld = 0;
                Assert.Equal((last + 1) % WalkCycle.FrameCount, frame);
                last = frame;
            }
        }
        Assert.True(holds.Count >= 7);
        foreach (int hold in holds.GetRange(1, holds.Count - 1))
            Assert.InRange(hold, 110, 112);
    }

    [Fact]
    public void SheetColumns_AlternateStepsWithNeutralPose()
    {
        Assert.Equal(1, WalkCycle.SheetColumn(0));
        Assert.Equal(0, WalkCycle.SheetColumn(1));
        Assert.Equal(2, WalkCycle.SheetColumn(2));
        Assert.Equal(0, WalkCycle.SheetColumn(3));
    }

    [Fact]
    public void Bob_LowOnStepsHighBetween()
    {
        Assert.Equal(0f, WalkCycle.Bob(0.5f), 3);
        Assert.Equal(1f, WalkCycle.Bob(1.5f), 3);
        Assert.Equal(0f, WalkCycle.Bob(2.5f), 3);
        Assert.Equal(1f, WalkCycle.Bob(3.5f), 3);
    }

    [Fact]
    public void Advance_ScalesWithSpeedAndWraps()
    {
        Assert.Equal(WalkCycle.Advance(0f, 0.1f, 500) * 2f, WalkCycle.Advance(0f, 0.1f, 1000), 3);
        Assert.InRange(WalkCycle.Advance(3.9f, 0.1f, 500), 0f, 4f);
        Assert.Equal(0.5f, WalkCycle.Advance(0.5f, -1f, 500), 3);
    }
}
