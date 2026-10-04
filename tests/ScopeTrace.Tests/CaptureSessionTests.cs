using System.Text;
using ScopeTrace.Capture;

namespace ScopeTrace.Tests;

public class CaptureSessionTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Recorder
    {
        public readonly List<CaptureStream> Started = [];
        public readonly List<CaptureStream> Completed = [];
        public readonly List<CaptureStream> Discarded = [];

        public Recorder(CaptureSession s)
        {
            s.Started += Started.Add;
            s.Completed += Completed.Add;
            s.Discarded += Discarded.Add;
        }
    }

    private static byte[] B(string s) => Encoding.Latin1.GetBytes(s);

    [Fact]
    public void TwoTransfersSeparatedByIdleGapBecomeTwoCaptures()
    {
        var s = new CaptureSession { IdleTimeout = TimeSpan.FromSeconds(1.5) };
        var r = new Recorder(s);

        s.Feed(B("IN;SP1;PD;PA10,10;"), T0);
        s.Feed(B("PA20,20;"), T0.AddSeconds(1));
        s.Tick(T0.AddSeconds(2));          // only 1 s since the last byte: still the same plot
        Assert.Empty(r.Completed);
        s.Tick(T0.AddSeconds(2.6));        // 1.6 s idle: done
        s.Feed(B("IN;PD;PA5,5;"), T0.AddSeconds(10));
        s.Tick(T0.AddSeconds(12));

        Assert.Equal(2, r.Started.Count);
        Assert.Equal(2, r.Completed.Count);
        Assert.Equal(B("IN;SP1;PD;PA10,10;PA20,20;"), r.Completed[0].RawBytes);
        Assert.Equal(2, r.Completed[0].Document.Segments.Count);
        Assert.Equal(B("IN;PD;PA5,5;"), r.Completed[1].RawBytes);
        Assert.Null(s.Current);
    }

    [Fact]
    public void BytesWithinTimeoutStayInOneCapture()
    {
        var s = new CaptureSession { IdleTimeout = TimeSpan.FromSeconds(1.5) };
        var r = new Recorder(s);
        var data = SampleData.Load();

        for (int i = 0; i < data.Length; i += 100)
            s.Feed(data.AsSpan(i, Math.Min(100, data.Length - i)), T0.AddSeconds(i / 1000.0));
        s.Tick(T0.AddHours(1));

        var cap = Assert.Single(r.Completed);
        Assert.Equal(data, cap.RawBytes); // byte-for-byte identical
    }

    [Fact]
    public void IdleGapIsDetectedOnNextFeedEvenWithoutTick()
    {
        var s = new CaptureSession { IdleTimeout = TimeSpan.FromSeconds(1) };
        var r = new Recorder(s);

        s.Feed(B("PD;PA1,1;"), T0);
        s.Feed(B("IN;PD;PA2,2;"), T0.AddSeconds(5));
        s.EndCurrent();

        Assert.Equal(2, r.Completed.Count);
    }

    [Fact]
    public void InAfterDrawingSplitsImmediately()
    {
        var s = new CaptureSession();
        var r = new Recorder(s);

        s.Feed(B("IN;PD;PA9,9;IN;PD;PA1,1;"), T0);

        var first = Assert.Single(r.Completed);
        Assert.Equal(B("IN;PD;PA9,9;"), first.RawBytes);
        Assert.NotNull(s.Current);
        Assert.Equal(B("IN;PD;PA1,1;"), s.Current!.RawBytes);
        Assert.Equal(new Hpgl.PlotSegment(0, 0, 1, 1, 1), Assert.Single(s.Current.Document.Segments));
    }

    [Fact]
    public void InSplitAlsoWorksWhenFedByteByByte()
    {
        var s = new CaptureSession();
        var r = new Recorder(s);

        foreach (byte b in B("IN;PD;PA9,9;INPD;PA1,1;"))
            s.Feed([b], T0);
        s.EndCurrent();

        Assert.Equal(2, r.Completed.Count);
        Assert.Equal(B("IN;PD;PA9,9;"), r.Completed[0].RawBytes);
        Assert.Equal(B("INPD;PA1,1;"), r.Completed[1].RawBytes);
        Assert.Single(r.Completed[1].Document.Segments);
    }

    [Fact]
    public void FlowControlNoiseDoesNotCreateACapture()
    {
        var s = new CaptureSession();
        var r = new Recorder(s);

        s.Feed([0x11, 0x13, 0x00, 0x0D, 0x0A, 0x11], T0);
        s.Tick(T0.AddMinutes(1));

        Assert.Empty(r.Started);
        Assert.Null(s.Current);
    }

    [Fact]
    public void TransferThatDrawsNothingIsDiscarded()
    {
        var s = new CaptureSession();
        var r = new Recorder(s);

        s.Feed(B("IN;SP0;"), T0);
        s.Tick(T0.AddMinutes(1));

        Assert.Single(r.Started);
        Assert.Empty(r.Completed);
        Assert.Single(r.Discarded);
    }
}


public class CaptureContinuationTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static byte[] B(string s) => Encoding.Latin1.GetBytes(s);

    [Fact]
    public void ScopePausingMidPlotKeepsOneCapture()
    {
        // Real 54600B behaviour: grid, then a ~60 s pause, then the trace, then another pause.
        var s = new CaptureSession { IdleTimeout = TimeSpan.FromSeconds(1.5) };
        var started = new List<CaptureStream>();
        var resumed = new List<CaptureStream>();
        s.Started += started.Add;
        s.Resumed += resumed.Add;

        s.Feed(B("df;sc -50,562,-50,330;pu;pa0,0;pd;pa10,0;pu;sp0;df;sp2;"), T0);
        s.Tick(T0.AddSeconds(2));
        foreach (byte b in B("pu;pa60,222;pd;pa60,221;"))   // arrives one byte at a time
            s.Feed([b], T0.AddSeconds(60));
        s.Tick(T0.AddSeconds(62));
        s.Feed(B("pu;pa 449,59;pd;"), T0.AddSeconds(75));
        s.Tick(T0.AddSeconds(80));

        var cap = Assert.Single(started);
        Assert.Equal(2, resumed.Count);
        Assert.All(resumed, r => Assert.Same(cap, r));
        Assert.Equal(B("df;sc -50,562,-50,330;pu;pa0,0;pd;pa10,0;pu;sp0;df;sp2;pu;pa60,222;pd;pa60,221;pu;pa 449,59;pd;"), cap.RawBytes);
        Assert.Equal(3, cap.Document.Segments.Count); // grid line, trace line, final dot
    }

    [Fact]
    public void NewPlotHeaderAfterPauseStartsANewCapture()
    {
        var s = new CaptureSession();
        var started = new List<CaptureStream>();
        s.Started += started.Add;

        s.Feed(B("df;pd;pa1,1;"), T0);
        s.Tick(T0.AddSeconds(5));
        s.Feed(B("df;pd;pa2,2;"), T0.AddSeconds(10));
        s.Tick(T0.AddSeconds(15));
        s.Feed(B("IN;PD;PA3,3;"), T0.AddSeconds(20));
        s.EndCurrent();

        Assert.Equal(3, started.Count);
    }

    [Fact]
    public void DataLongAfterThePreviousPlotIsNotMerged()
    {
        var s = new CaptureSession { ContinuationWindow = TimeSpan.FromMinutes(3) };
        var started = new List<CaptureStream>();
        s.Started += started.Add;

        s.Feed(B("pd;pa1,1;"), T0);
        s.Tick(T0.AddSeconds(5));
        s.Feed(B("pu;pa2,2;pd;pa3,3;"), T0.AddMinutes(10));
        s.EndCurrent();

        Assert.Equal(2, started.Count);
    }

    [Fact]
    public void ForgottenCaptureIsNotResumed()
    {
        var s = new CaptureSession();
        var started = new List<CaptureStream>();
        s.Started += started.Add;
        s.Completed += c => s.ForgetPrevious(c);

        s.Feed(B("pd;pa1,1;"), T0);
        s.Tick(T0.AddSeconds(5));
        s.Feed(B("pu;pa2,2;pd;pa3,3;"), T0.AddSeconds(10));
        s.EndCurrent();

        Assert.Equal(2, started.Count);
    }
}
