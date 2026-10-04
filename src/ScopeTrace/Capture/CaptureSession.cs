using ScopeTrace.Hpgl;

namespace ScopeTrace.Capture;

/// <summary>One plot transfer: the exact bytes received and the drawing interpreted from them.</summary>
public sealed class CaptureStream
{
    private readonly MemoryStream _raw = new();

    public CaptureStream(DateTime startedAt) => StartedAt = startedAt;

    public DateTime StartedAt { get; }
    public DateTime LastByteAt { get; internal set; }
    public HpglInterpreter Interpreter { get; } = new();
    public PlotDocument Document => Interpreter.Document;
    public long ByteCount => _raw.Length;

    /// <summary>Copy of the received bytes, byte for byte.</summary>
    public byte[] RawBytes => _raw.ToArray();

    internal void Append(ReadOnlySpan<byte> data) => _raw.Write(data);

    internal byte[] TakeTailFrom(long offset)
    {
        var all = _raw.GetBuffer();
        var tail = all.AsSpan((int)offset, (int)(_raw.Length - offset)).ToArray();
        _raw.SetLength(offset);
        return tail;
    }
}

/// <summary>
/// Splits the incoming byte stream into separate plots. A plot starts with the first meaningful
/// byte after a quiet period and ends when the line has been idle for <see cref="IdleTimeout"/>,
/// or earlier when a new IN (initialize) command follows something already drawn.
/// Time is passed in explicitly so the logic is deterministic and testable.
/// </summary>
public sealed class CaptureSession
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// After a pause, data that does not start a new plot (anything but IN or DF) within this
    /// window is appended to the previous capture: the 54600 sometimes stops for many seconds
    /// in the middle of a plot.
    /// </summary>
    public TimeSpan ContinuationWindow { get; set; } = TimeSpan.FromMinutes(3);

    public CaptureStream? Current { get; private set; }

    private CaptureStream? _lastFinished;
    private readonly List<byte> _pending = [];
    private DateTime _pendingSince;

    public event Action<CaptureStream>? Started;
    public event Action<CaptureStream>? Updated;
    public event Action<CaptureStream>? Completed;
    public event Action<CaptureStream>? Discarded;

    /// <summary>A completed capture received more data after a pause and is receiving again.</summary>
    public event Action<CaptureStream>? Resumed;

    public void Feed(ReadOnlySpan<byte> data, DateTime now)
    {
        // A transfer that went quiet long enough is over even if Tick was not called in between.
        Tick(now);

        bool changed = false;
        while (data.Length > 0)
        {
            if (Current is null)
            {
                if (_pending.Count == 0)
                {
                    int first = IndexOfMeaningful(data);
                    if (first < 0)
                        break; // only flow-control bytes / whitespace / NULs: not a plot
                    data = data[first..];
                    _pendingSince = now;
                }

                // Hold the first bytes until we can tell whether a new plot starts or a paused one continues.
                int take = Math.Min(data.Length, Math.Max(0, 2 - _pending.Count));
                _pending.AddRange(data[..take].ToArray());
                data = data[take..];
                if (_pending.Count < 2)
                    break;
                data = StartFromPending(now, data);
                changed = true;
                continue;
            }

            var cap = Current;
            int used = cap.Interpreter.Feed(data);
            cap.Append(data[..used]);
            cap.LastByteAt = now;
            data = data[used..];
            changed = true;

            if (cap.Interpreter.NewPlotStartOffset is long split)
            {
                // Everything from the IN command on belongs to the next plot.
                byte[] tail = cap.TakeTailFrom(split);
                Updated?.Invoke(cap);
                Finish(cap);

                Current = new CaptureStream(now);
                Started?.Invoke(Current);
                Current.Interpreter.Feed(tail); // re-reads "IN;" on a fresh, empty document
                Current.Append(tail);
                Current.LastByteAt = now;
            }
        }

        if (changed && Current is not null)
            Updated?.Invoke(Current);
    }

    /// <summary>Call periodically; completes the current plot once the line is idle.</summary>
    public void Tick(DateTime now)
    {
        if (Current is null && _pending.Count > 0 && now - _pendingSince >= TimeSpan.FromMilliseconds(300))
            StartFromPending(now, []);
        if (Current is { } cap && now - cap.LastByteAt >= IdleTimeout)
            Finish(cap);
    }

    /// <summary>Ends the current plot immediately (e.g. when listening stops).</summary>
    public void EndCurrent()
    {
        if (Current is { } cap)
            Finish(cap);
    }

    private void Finish(CaptureStream cap)
    {
        Current = null;
        cap.Interpreter.Flush();
        if (cap.Document.IsEmpty)
            Discarded?.Invoke(cap);
        else
        {
            _lastFinished = cap;
            Completed?.Invoke(cap);
        }
    }

    /// <summary>The previous capture will not be continued (e.g. it was deleted).</summary>
    public void ForgetPrevious(CaptureStream stream)
    {
        if (_lastFinished == stream)
            _lastFinished = null;
    }

    private ReadOnlySpan<byte> StartFromPending(DateTime now, ReadOnlySpan<byte> rest)
    {
        byte[] head = [.. _pending];
        _pending.Clear();

        if (_lastFinished is { } previous && !StartsNewPlot(head) && now - previous.LastByteAt <= ContinuationWindow)
        {
            Current = previous;
            Resumed?.Invoke(previous);
        }
        else
        {
            _lastFinished = null;
            Current = new CaptureStream(now);
            Started?.Invoke(Current);
        }

        var cap = Current;
        int used = cap.Interpreter.Feed(head);
        cap.Append(head.AsSpan(0, used));
        cap.LastByteAt = now;
        return rest;
    }

    private static bool StartsNewPlot(byte[] head)
    {
        if (head[0] == 0x1B)
            return true; // device-control escape: start of a fresh job
        string m = new(new[] { char.ToUpperInvariant((char)head[0]), char.ToUpperInvariant((char)head[1]) });
        return m is "IN" or "DF";
    }

    private static int IndexOfMeaningful(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b is 0x00 or 0x11 or 0x13 or 0x0D or 0x0A or (byte)' ' or (byte)'\t' or 0xFF)
                continue;
            return i;
        }
        return -1;
    }
}
