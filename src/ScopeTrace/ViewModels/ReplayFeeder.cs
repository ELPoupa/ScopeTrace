using ScopeTrace.Capture;

namespace ScopeTrace.ViewModels;

/// <summary>
/// Plays an HP-GL file into the capture pipeline at the speed a serial line would deliver it
/// (8N1 = 10 bits per byte), to preview live drawing without a scope attached.
/// </summary>
public sealed class ReplayFeeder
{
    private readonly byte[] _data;
    private readonly double _bytesPerSecond;
    private DateTime? _start;
    private int _sent;

    public ReplayFeeder(string path, byte[] data, int baud)
    {
        Path = path;
        _data = data;
        _bytesPerSecond = baud / 10.0;
    }

    public string Path { get; }
    public bool IsFinished => _sent >= _data.Length;

    public void Pump(DateTime now, CaptureSession session)
    {
        _start ??= now;
        int due = (int)Math.Min(_data.Length, (now - _start.Value).TotalSeconds * _bytesPerSecond + 1);
        if (due > _sent)
        {
            session.Feed(_data.AsSpan(_sent, due - _sent), now);
            _sent = due;
        }
    }
}
