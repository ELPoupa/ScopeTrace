using System.Text;
using ScopeTrace.Hpgl;

namespace ScopeTrace.Tests;

public class HpglInterpreterTests
{
    private static PlotDocument Run(string hpgl)
    {
        var interp = new HpglInterpreter();
        interp.Feed(Encoding.Latin1.GetBytes(hpgl));
        interp.Flush();
        return interp.Document;
    }

    [Fact]
    public void AbsolutePolylineDrawsSegmentsOnlyWithPenDown()
    {
        var doc = Run("IN;SP1;PU100,100;PD200,100,200,200;PU300,300;PD;PA400,300;");

        Assert.Equal(
        [
            new PlotSegment(100, 100, 200, 100, 1),
            new PlotSegment(200, 100, 200, 200, 1),
            new PlotSegment(300, 300, 400, 300, 1),
        ], doc.Segments);
    }

    [Fact]
    public void RelativeModePersistsForPdUntilPa()
    {
        var doc = Run("IN;PA100,100;PR;PD10,0,0,10;PA0,0;");

        Assert.Equal(
        [
            new PlotSegment(100, 100, 110, 100, 1),
            new PlotSegment(110, 100, 110, 110, 1),
            new PlotSegment(110, 110, 0, 0, 1),
        ], doc.Segments);
    }

    [Fact]
    public void LowercaseSpaceSeparatorsSignsAndMissingSemicolons()
    {
        var doc = Run("in sp2 pu 0 0 pd 10 -20+30 5PU");

        Assert.Equal(
        [
            new PlotSegment(0, 0, 10, -20, 2),
            new PlotSegment(10, -20, 30, 5, 2),
        ], doc.Segments);
    }

    [Fact]
    public void SpZeroHidesOutputAndDefaultPenIsOne()
    {
        var doc = Run("IN;PD;PA10,10;SP0;PA20,20;SP2;PA30,30;");

        Assert.Equal(
        [
            new PlotSegment(0, 0, 10, 10, 1),
            new PlotSegment(20, 20, 30, 30, 2),
        ], doc.Segments);
    }

    [Fact]
    public void LabelIsTerminatedByEtxAndMayContainSemicolonsAndLetters()
    {
        var doc = Run("IN;SP1;PA100,200;LBV=1.0;PA 5\x03PA0,0;PD10,0;");

        var label = Assert.Single(doc.Labels);
        Assert.Equal("V=1.0;PA 5", label.Text);
        Assert.Equal(100, label.X);
        Assert.Equal(200, label.Y);
        Assert.Single(doc.Segments); // the PA/PD after the label is parsed normally
    }

    [Fact]
    public void CustomTerminatorViaDt()
    {
        var doc = Run("IN;DT@;LBhello@PU;DT;LBworld\x03");

        Assert.Equal(["hello", "world"], doc.Labels.Select(l => l.Text));
    }

    [Fact]
    public void LabelCrLfStartsNewLineBelowLabelStart()
    {
        var doc = Run("IN;SI0.1,0.2;PA1000,1000;LBAB\r\nC\x03");

        Assert.Equal(2, doc.Labels.Count);
        Assert.Equal((1000, 1000), (doc.Labels[0].X, doc.Labels[0].Y));
        // SI is in cm: 0.2 cm = 80 pu high, line spacing is two character heights.
        Assert.Equal(1000, doc.Labels[1].X, 6);
        Assert.Equal(1000 - 160, doc.Labels[1].Y, 6);
        Assert.Equal(40, doc.Labels[0].CharWidth, 6);
        Assert.Equal(80, doc.Labels[0].CharHeight, 6);
    }

    [Fact]
    public void LabelAdvancesPenPositionAlongDirection()
    {
        var doc = Run("IN;SI0.1,0.2;DI0,1;PA0,0;LBABC\x03PD;PR0,0;LBX\x03");

        Assert.Equal(Math.PI / 2, doc.Labels[0].Direction, 6);
        // 3 chars * 1.5 * 40 pu = 180 pu up the Y axis.
        Assert.Equal(0, doc.Labels[1].X, 6);
        Assert.Equal(180, doc.Labels[1].Y, 6);
        Assert.Equal(90, doc.DominantLabelQuadrant());
    }

    [Fact]
    public void DefaultCharSizeIsRelativeToP1P2()
    {
        var doc = Run("IN;LBA\x03");
        Assert.Equal(75, doc.Labels[0].CharWidth, 6);   // 0.75% of 10000
        Assert.Equal(108, doc.Labels[0].CharHeight, 6); // 1.5% of 7200
    }

    [Fact]
    public void EscapeSequencesAreSkipped()
    {
        var doc = Run("\x1B.(\x1B.I81;;17:\x1B.N;19:\x1B.@1234;0:\x1B.)IN;PD;PA5,5;");

        Assert.Equal([new PlotSegment(0, 0, 5, 5, 1)], doc.Segments);
    }

    [Fact]
    public void UnknownCommandsAndGarbageAreIgnored()
    {
        var bytes = new byte[] { 0xFF, 0x00, 0x11, 0x13, 0x07 }
            .Concat(Encoding.ASCII.GetBytes("ZZ12,34;VS10;LT2,4;PT0.3;IN;PD;PA1,1;XY;PA2,2;"))
            .ToArray();
        var interp = new HpglInterpreter();
        interp.Feed(bytes);

        Assert.Equal(
        [
            new PlotSegment(0, 0, 1, 1, 1),
            new PlotSegment(1, 1, 2, 2, 1),
        ], interp.Document.Segments);
    }

    [Fact]
    public void ScalingMapsUserUnitsOntoP1P2()
    {
        var doc = Run("IN;IP0,0,1000,2000;SC0,10,0,10;PU0,0;PD10,10;PR-5,0;");

        Assert.Equal(
        [
            new PlotSegment(0, 0, 1000, 2000, 1),
            new PlotSegment(1000, 2000, 500, 2000, 1),
        ], doc.Segments);
    }

    [Fact]
    public void CircleAndRectangleProduceClosedOutlines()
    {
        var doc = Run("IN;PA500,500;CI100,90;EA600,700;");

        // CI with a 90 degree chord gives 4 segments, EA gives 4 edges.
        Assert.Equal(8, doc.Segments.Count);
        Assert.Equal(new PlotSegment(500, 500, 600, 500, 1), doc.Segments[4]);
    }

    [Fact]
    public void NewPlotSplitIsReportedOnlyAfterSomethingWasDrawn()
    {
        var interp = new HpglInterpreter();
        var bytes = Encoding.ASCII.GetBytes("IN;PD;PA9,9;IN;PA1,1;");
        int used = interp.Feed(bytes);

        Assert.Equal(15, used); // stops right after the second "IN;"
        Assert.Equal(12, interp.NewPlotStartOffset);
    }

    [Fact]
    public void ChunkBoundariesDoNotChangeTheResult()
    {
        byte[] sample = SampleData.Load();
        var whole = new HpglInterpreter();
        whole.Feed(sample);
        whole.Flush();

        var oneByOne = new HpglInterpreter();
        foreach (byte b in sample)
            oneByOne.Feed([b]);
        oneByOne.Flush();

        var random = new HpglInterpreter();
        var rng = new Random(1234);
        for (int i = 0; i < sample.Length;)
        {
            int n = Math.Min(rng.Next(1, 97), sample.Length - i);
            random.Feed(sample.AsSpan(i, n));
            i += n;
        }
        random.Flush();

        Assert.True(whole.Document.Segments.Count > 2000);
        Assert.Equal(5, whole.Document.Labels.Count);
        Assert.Equal(whole.Document.Segments, oneByOne.Document.Segments);
        Assert.Equal(whole.Document.Segments, random.Document.Segments);
        Assert.Equal(whole.Document.Labels, oneByOne.Document.Labels);
        Assert.Equal(whole.Document.Labels, random.Document.Labels);
    }

    [Fact]
    public void SampleUsesBothPensAndVerticalLabels()
    {
        var interp = new HpglInterpreter();
        interp.Feed(SampleData.Load());
        interp.Flush();
        var doc = interp.Document;

        Assert.Equal([1, 2], doc.PensUsed.Order());
        Assert.Equal(90, doc.DominantLabelQuadrant());
        Assert.Equal("1  1.00V", doc.Labels[0].Text);
        Assert.Equal("Freq(2)=1.500kHz", doc.Labels[4].Text);
        Assert.Null(interp.NewPlotStartOffset);
    }
}

public class ScopeDialectTests
{
    [Fact]
    public void PenDownThenUpWithoutMovingDrawsADot()
    {
        var interp = new HpglInterpreter();
        interp.Feed(Encoding.ASCII.GetBytes("IN;PA10,20;PD;PU;PA30,40;PD;"));
        interp.Flush();

        Assert.Equal(
        [
            new PlotSegment(10, 20, 10, 20, 1),
            new PlotSegment(30, 40, 30, 40, 1),
        ], interp.Document.Segments);
    }

    [Fact]
    public void Hp54600StyleHeaderLabelAndDots()
    {
        // Same shape as a 54600B plotter dump: lowercase, screen-pixel scaling, a timestamp label
        // ending in CR LF ETX, graticule dots drawn as pd;pu; and no closing command.
        const string plot =
            "df;sc -50,562,-50,330;pu;sp1;dr1,0;sr.6,1.2;pa0,-20;lb12:00:00  Mon Jan 1, 2001\r\n\x03" +
            "sp1;pu;pa4,279;pd;pa4,270;pa1,270;pu;pa 100,100;pd;pu;pa 102,100;pd;";
        var interp = new HpglInterpreter();
        interp.Feed(Encoding.Latin1.GetBytes(plot));
        interp.Flush();
        var doc = interp.Document;

        Assert.Equal("12:00:00  Mon Jan 1, 2001", Assert.Single(doc.Labels).Text);
        Assert.Equal(4, doc.Segments.Count); // two lines and two dots
        Assert.Equal(2, doc.Segments.Count(s => s.X1 == s.X2 && s.Y1 == s.Y2));
        // SC maps user x -50..562 onto P1..P2 (250..10250 plotter units).
        Assert.Equal(250 + 54 * 10000 / 612.0, doc.Segments[0].X1, 6);
        Assert.Equal(0, doc.DominantLabelQuadrant());
    }
}
