using System.IO.Ports;

namespace ScopeTrace.Serial;

public enum ListenerState
{
    Stopped,
    Listening,
    Unavailable,
}

/// <summary>
/// Keeps a serial port open and streams whatever arrives. Runs on a dedicated background
/// thread; if the port disappears (USB adapter unplugged) or is busy, it keeps retrying until
/// the port comes back or <see cref="Stop"/> is called. Events are raised on that thread.
/// </summary>
public sealed class ScopeListener : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan XonInterval = TimeSpan.FromMilliseconds(500);
    private static readonly byte[] Xon = [0x11];

    private readonly object _gate = new();
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public event Action<byte[]>? DataReceived;
    public event Action<ListenerState, string>? StateChanged;

    public SerialSettings? Settings { get; private set; }
    public bool IsRunning => _thread is not null;

    public void Start(SerialSettings settings)
    {
        lock (_gate)
        {
            Stop();
            Settings = settings;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _thread = new Thread(() => Run(settings, token))
            {
                IsBackground = true,
                Name = "ScopeTrace serial reader",
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_thread is null)
                return;
            _cts!.Cancel();
            _thread.Join(TimeSpan.FromSeconds(3));
            _thread = null;
            _cts.Dispose();
            _cts = null;
        }
        StateChanged?.Invoke(ListenerState.Stopped, "Stopped");
    }

    public void Dispose() => Stop();

    private void Run(SerialSettings settings, CancellationToken token)
    {
        var buffer = new byte[8192];
        bool reportedUnavailable = false;

        while (!token.IsCancellationRequested)
        {
            SerialPort? port = null;
            try
            {
                port = Open(settings);
                reportedUnavailable = false;
                StateChanged?.Invoke(ListenerState.Listening, $"Listening on {settings}");
                var nextXon = DateTime.MinValue;

                while (!token.IsCancellationRequested)
                {
                    // The scope will not start a plot until it has received XON from the "plotter"
                    // ("printer not responding" otherwise), so keep announcing that we are ready.
                    if (DateTime.UtcNow >= nextXon)
                    {
                        SendXon(port);
                        nextXon = DateTime.UtcNow + XonInterval;
                    }

                    int n;
                    try
                    {
                        n = port.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue; // lets us notice cancellation and unplugged adapters
                    }
                    if (n > 0)
                        DataReceived?.Invoke(buffer.AsSpan(0, n).ToArray());
                    if (!port.IsOpen)
                        throw new IOException("The port was closed.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or ObjectDisposedException)
            {
                if (token.IsCancellationRequested)
                    break;
                string reason = ex switch
                {
                    UnauthorizedAccessException => $"{settings.PortName} is in use by another program",
                    _ when !SerialPort.GetPortNames().Contains(settings.PortName, StringComparer.OrdinalIgnoreCase)
                        => $"{settings.PortName} is not connected",
                    _ => $"{settings.PortName}: {ex.Message.TrimEnd('.')}",
                };
                if (!reportedUnavailable || ex is not UnauthorizedAccessException)
                    StateChanged?.Invoke(ListenerState.Unavailable, $"{reason}, retrying…");
                reportedUnavailable = true;
            }
            finally
            {
                try { port?.Dispose(); } catch (IOException) { }
            }

            token.WaitHandle.WaitOne(RetryDelay);
        }
    }

    private static void SendXon(SerialPort port)
    {
        try
        {
            port.Write(Xon, 0, 1);
        }
        catch (TimeoutException)
        {
            // Output blocked by hardware flow control; not fatal for receiving.
        }
    }

    private static SerialPort Open(SerialSettings s)
    {
        var port = new SerialPort(s.PortName, s.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadBufferSize = 1 << 16,
            ReadTimeout = 250,
            WriteTimeout = 500,
            // We read far faster than 19200 baud, so the PC never needs to pause the scope.
            // Leave flow control to us: no driver XON/XOFF (it made the real scope stall
            // mid-plot), DTR and RTS permanently "ready", plus the periodic XON sent above.
            // This works for both of the scope's handshake modes.
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
        };

        try
        {
            port.Open();
            port.DiscardInBuffer();
        }
        catch
        {
            port.Dispose();
            throw;
        }
        return port;
    }
}
