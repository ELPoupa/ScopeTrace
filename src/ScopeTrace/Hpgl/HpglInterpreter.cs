using System.Text;

namespace ScopeTrace.Hpgl;

/// <summary>
/// Streaming HP-GL interpreter. Bytes can be fed in chunks of any size (down to one byte at a
/// time) and drawing primitives are produced as soon as each coordinate pair is complete, which
/// is what makes the live "plotter" view possible.
///
/// Covers the HP-GL subset that oscilloscopes and other instruments emit: IN DF SP PU PD PA PR
/// LB DT SI SR DI DR CP CI AA AR EA ER RA RR IP SC, plus HP RS-232 device-control escape
/// sequences (ESC . x ... :), which are skipped. Anything unknown is parsed and ignored.
/// </summary>
public sealed class HpglInterpreter
{
    private const byte Etx = 0x03;
    private const byte Esc = 0x1B;
    private const int MaxParamChars = 64;

    // Default hard-clip corners of an A4/letter plotter.
    private const double DefaultP1X = 250, DefaultP1Y = 596, DefaultP2X = 10250, DefaultP2Y = 7796;

    private enum State { Ground, Mnemonic, Params, Label, DtChar, SmChar, EscStart, EscCommand, EscParams }

    private State _state = State.Ground;
    private char _firstLetter;
    private string _mnemonic = "";
    private long _offset;            // absolute index of the byte being processed
    private long _mnemonicStart;     // absolute index of the first letter of the current command

    // Numeric argument tokenizer.
    private readonly List<double> _args = [];
    private readonly StringBuilder _token = new();

    // Label accumulation.
    private readonly StringBuilder _labelRun = new();
    private double _runX, _runY;

    // Plotter state.
    private int _pen;
    private bool _penDown;
    private bool _strokeDrawn;       // something was drawn since the pen last went down
    private bool _relative;
    private double _x, _y;            // pen position, plotter units
    private double _crX, _crY;        // carriage-return point for labels
    private double _p1X, _p1Y, _p2X, _p2Y;
    private bool _scaling;
    private double _scXMin, _scXMax, _scYMin, _scYMax;
    private bool _relativeCharSize;
    private double _charW, _charH;    // absolute size in plotter units (when not relative)
    private double _relW, _relH;      // percent of P1-P2 (when relative)
    private double _direction;        // label direction in radians
    private bool _directionRelative;
    private double _drRun, _drRise;
    private byte _terminator;

    public HpglInterpreter()
    {
        Initialize();
        _penDown = false;
    }

    public PlotDocument Document { get; } = new();

    /// <summary>Total bytes consumed so far.</summary>
    public long BytesConsumed => _offset;

    /// <summary>
    /// Set when an IN (initialize) command arrives after something has already been drawn: a new
    /// plot has started. Holds the absolute byte offset where that IN command begins. Feeding
    /// stops right after the IN command so the caller can split the stream into two captures.
    /// </summary>
    public long? NewPlotStartOffset { get; private set; }

    /// <summary>Feeds bytes. Returns how many were consumed (less than data.Length only on a new-plot split).</summary>
    public int Feed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            Process(data[i]);
            _offset++;
            if (NewPlotStartOffset is not null)
                return i + 1;
        }
        return data.Length;
    }

    /// <summary>Executes any command still waiting for its terminator (call when the stream ends).</summary>
    public void Flush()
    {
        if (_penDown && !_strokeDrawn)
            Line(_x, _y, _x, _y); // a final "PD;" with no movement still marks a dot
        if (_state == State.Params)
        {
            EndCommand();
            _state = State.Ground;
        }
        else if (_state == State.Label)
        {
            FlushLabelRun();
            _state = State.Ground;
        }
    }

    private void Process(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                if (IsLetter(b))
                    BeginMnemonic(b);
                else if (b == Esc)
                    _state = State.EscStart;
                break;

            case State.Mnemonic:
                if (IsLetter(b))
                    StartCommand(char.ToUpperInvariant(_firstLetter) + char.ToUpperInvariant((char)b).ToString());
                else if (b == Esc)
                    _state = State.EscStart;
                else if (b is (byte)' ' or 0x0D or 0x0A or 0x00 or 0x11 or 0x13)
                {
                    // Whitespace / flow control between the two letters: keep waiting.
                }
                else
                    _state = State.Ground; // stray letter
                break;

            case State.Params:
                if (b == (byte)';')
                {
                    EndCommand();
                    _state = State.Ground;
                }
                else if (IsLetter(b))
                {
                    // HP-GL allows omitting ';' - the next mnemonic terminates the command.
                    EndCommand();
                    if (NewPlotStartOffset is null)
                        BeginMnemonic(b);
                    else
                        _state = State.Ground;
                }
                else if (b == Esc)
                {
                    EndCommand();
                    _state = State.EscStart;
                }
                else
                    ParamChar((char)b);
                break;

            case State.Label:
                LabelByte(b);
                break;

            case State.DtChar:
                // DT; (no parameter) restores ETX. Otherwise the next byte is the new terminator.
                _terminator = b == (byte)';' ? Etx : b;
                _mnemonic = "DT_";
                _state = b == (byte)';' ? State.Ground : State.Params;
                break;

            case State.SmChar:
                _mnemonic = "SM_";
                _state = b == (byte)';' ? State.Ground : State.Params;
                break;

            case State.EscStart:
                _state = b == (byte)'.' ? State.EscCommand : State.Ground;
                break;

            case State.EscCommand:
                // Device-control commands that take parameters end with ':'; the others are a single letter.
                _state = b is (byte)'@' or (byte)'H' or (byte)'I' or (byte)'M' or (byte)'N' or (byte)'T'
                    or (byte)'P' or (byte)'S' or (byte)'h' or (byte)'i' or (byte)'m' or (byte)'n' or (byte)'t'
                    ? State.EscParams
                    : State.Ground;
                _token.Clear();
                break;

            case State.EscParams:
                if (b == (byte)':')
                    _state = State.Ground;
                else if (IsLetter(b) || _token.Length > MaxParamChars)
                {
                    // Malformed sequence without ':' - resynchronise on the next mnemonic.
                    _token.Clear();
                    if (IsLetter(b))
                        BeginMnemonic(b);
                    else
                        _state = State.Ground;
                }
                else
                    _token.Append((char)b);
                break;
        }
    }

    private static bool IsLetter(byte b) => (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z');

    private void BeginMnemonic(byte b)
    {
        _firstLetter = (char)b;
        _mnemonicStart = _offset;
        _state = State.Mnemonic;
    }

    private void StartCommand(string mnemonic)
    {
        _mnemonic = mnemonic;
        _args.Clear();
        _token.Clear();

        switch (mnemonic)
        {
            case "LB":
                _runX = _x;
                _runY = _y;
                _labelRun.Clear();
                _state = State.Label;
                return;
            case "DT":
                _state = State.DtChar;
                return;
            case "SM":
                _state = State.SmChar;
                return;
            case "PU":
                PenUp();
                break;
            case "PD":
                PenDown();
                break;
            case "PA":
                _relative = false;
                break;
            case "PR":
                _relative = true;
                break;
        }
        _state = State.Params;
    }

    // ---------------------------------------------------------------- numeric parameters

    private void ParamChar(char c)
    {
        if (c is >= '0' and <= '9' or '.')
        {
            if (_token.Length < MaxParamChars)
                _token.Append(c);
        }
        else if (c is '+' or '-')
        {
            // A sign starts a new number ("PA10-20" is two numbers).
            FinishToken();
            _token.Append(c);
        }
        else
            FinishToken(); // comma, space, CR/LF, anything else separates numbers
    }

    private void FinishToken()
    {
        if (_token.Length == 0)
            return;
        if (double.TryParse(_token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            _args.Add(v);
            OnArgument();
        }
        _token.Clear();
    }

    /// <summary>Coordinate commands act on every completed pair immediately (live drawing).</summary>
    private void OnArgument()
    {
        if (_args.Count != 2)
            return;
        switch (_mnemonic)
        {
            case "PA":
            case "PR":
            case "PD":
            case "PU":
                MoveTo(_args[0], _args[1], _relative);
                _args.Clear();
                break;
        }
    }

    private void EndCommand()
    {
        FinishToken();
        var a = _args;
        switch (_mnemonic)
        {
            case "IN":
                if (!Document.IsEmpty)
                    NewPlotStartOffset = _mnemonicStart;
                Initialize();
                break;
            case "DF":
                Defaults();
                break;
            case "SP":
                _pen = a.Count > 0 ? (int)a[0] : 0;
                break;
            case "IP":
                if (a.Count >= 4)
                    (_p1X, _p1Y, _p2X, _p2Y) = (a[0], a[1], a[2], a[3]);
                else if (a.Count >= 2)
                {
                    // Only P1 given: P2 keeps its offset from P1.
                    double dx = _p2X - _p1X, dy = _p2Y - _p1Y;
                    (_p1X, _p1Y) = (a[0], a[1]);
                    (_p2X, _p2Y) = (_p1X + dx, _p1Y + dy);
                }
                else
                    (_p1X, _p1Y, _p2X, _p2Y) = (DefaultP1X, DefaultP1Y, DefaultP2X, DefaultP2Y);
                break;
            case "SC":
                if (a.Count >= 4 && a[1] != a[0] && a[3] != a[2])
                {
                    _scaling = true;
                    (_scXMin, _scXMax, _scYMin, _scYMax) = (a[0], a[1], a[2], a[3]);
                }
                else
                    _scaling = false;
                break;
            case "SI":
                if (a.Count >= 2)
                {
                    _relativeCharSize = false;
                    _charW = a[0] * 400; // cm -> plotter units
                    _charH = a[1] * 400;
                }
                else
                    SetDefaultCharSize();
                break;
            case "SR":
                _relativeCharSize = true;
                if (a.Count >= 2)
                    (_relW, _relH) = (a[0], a[1]);
                else
                    (_relW, _relH) = (0.75, 1.5);
                break;
            case "DI":
                _directionRelative = false;
                _direction = a.Count >= 2 && (a[0] != 0 || a[1] != 0) ? Math.Atan2(a[1], a[0]) : 0;
                break;
            case "DR":
                if (a.Count >= 2 && (a[0] != 0 || a[1] != 0))
                {
                    _directionRelative = true;
                    (_drRun, _drRise) = (a[0], a[1]);
                }
                else
                {
                    _directionRelative = false;
                    _direction = 0;
                }
                break;
            case "CP":
                if (a.Count >= 2)
                    CharacterPlot(a[0], a[1]);
                else
                {
                    _x = _crX; _y = _crY;
                    CharacterPlot(0, -1);
                }
                break;
            case "CI":
                if (a.Count >= 1)
                    Circle(a[0], a.Count >= 2 ? a[1] : 5);
                break;
            case "AA":
            case "AR":
                if (a.Count >= 3)
                    Arc(a[0], a[1], a[2], a.Count >= 4 ? a[3] : 5, _mnemonic == "AR");
                break;
            case "EA":
            case "RA":
                if (a.Count >= 2)
                    Rectangle(a[0], a[1], relative: false);
                break;
            case "ER":
            case "RR":
                if (a.Count >= 2)
                    Rectangle(a[0], a[1], relative: true);
                break;
            // PA/PR/PD/PU were handled pair by pair; everything else (LT, PT, VS, IW, RO, PG, CS,
            // CA, SS, SA, LO, ...) does not affect what we draw and is ignored.
        }
        _args.Clear();
    }

    // ---------------------------------------------------------------- state

    private void Initialize()
    {
        (_p1X, _p1Y, _p2X, _p2Y) = (DefaultP1X, DefaultP1Y, DefaultP2X, DefaultP2Y);
        _x = _y = 0;
        _crX = _crY = 0;
        _penDown = false;
        // Instruments often never send SP; treat "no pen selected yet" as pen 1. SP0 still hides output.
        _pen = 1;
        Defaults();
    }

    private void Defaults()
    {
        _relative = false;
        _scaling = false;
        _terminator = Etx;
        _direction = 0;
        _directionRelative = false;
        SetDefaultCharSize();
    }

    private void SetDefaultCharSize()
    {
        _relativeCharSize = true;
        (_relW, _relH) = (0.75, 1.5);
    }

    private double CharWidth => _relativeCharSize ? Math.Abs(_p2X - _p1X) * _relW / 100 : _charW;
    private double CharHeight => _relativeCharSize ? Math.Abs(_p2Y - _p1Y) * _relH / 100 : _charH;

    private double Direction => _directionRelative
        ? Math.Atan2(_drRise * (_p2Y - _p1Y), _drRun * (_p2X - _p1X))
        : _direction;

    private (double X, double Y) ToPlotter(double ux, double uy)
    {
        if (!_scaling)
            return (ux, uy);
        return (_p1X + (ux - _scXMin) * (_p2X - _p1X) / (_scXMax - _scXMin),
                _p1Y + (uy - _scYMin) * (_p2Y - _p1Y) / (_scYMax - _scYMin));
    }

    private (double X, double Y) ToPlotterDelta(double dx, double dy)
    {
        if (!_scaling)
            return (dx, dy);
        return (dx * (_p2X - _p1X) / (_scXMax - _scXMin), dy * (_p2Y - _p1Y) / (_scYMax - _scYMin));
    }

    // ---------------------------------------------------------------- drawing

    private void PenDown()
    {
        if (!_penDown)
            _strokeDrawn = false;
        _penDown = true;
    }

    /// <summary>"PD;PU;" without moving leaves a dot on paper; instruments use this for graticule dots.</summary>
    private void PenUp()
    {
        if (_penDown && !_strokeDrawn)
            Line(_x, _y, _x, _y);
        _penDown = false;
    }

    private void MoveTo(double ax, double ay, bool relative)
    {
        double nx, ny;
        if (relative)
        {
            var (dx, dy) = ToPlotterDelta(ax, ay);
            (nx, ny) = (_x + dx, _y + dy);
        }
        else
            (nx, ny) = ToPlotter(ax, ay);

        if (_penDown)
            Line(_x, _y, nx, ny);
        _x = _crX = nx;
        _y = _crY = ny;
    }

    private void Line(double x1, double y1, double x2, double y2)
    {
        _strokeDrawn = true;
        if (_pen > 0)
            Document.AddSegment(new PlotSegment(x1, y1, x2, y2, _pen));
    }

    private void Circle(double radius, double chordDeg)
    {
        double r = Math.Abs(ToPlotterDelta(radius, 0).X);
        int n = Steps(360, chordDeg);
        double px = _x + r, py = _y;
        for (int i = 1; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            double qx = _x + r * Math.Cos(t), qy = _y + r * Math.Sin(t);
            Line(px, py, qx, qy);
            (px, py) = (qx, qy);
        }
    }

    private void Arc(double cxArg, double cyArg, double sweepDeg, double chordDeg, bool relative)
    {
        var (cx, cy) = relative
            ? (_x + ToPlotterDelta(cxArg, cyArg).X, _y + ToPlotterDelta(cxArg, cyArg).Y)
            : ToPlotter(cxArg, cyArg);
        double r = Math.Sqrt((_x - cx) * (_x - cx) + (_y - cy) * (_y - cy));
        double start = Math.Atan2(_y - cy, _x - cx);
        double sweep = sweepDeg * Math.PI / 180;
        int n = Steps(Math.Abs(sweepDeg), chordDeg);
        double px = _x, py = _y;
        for (int i = 1; i <= n; i++)
        {
            double t = start + sweep * i / n;
            double qx = cx + r * Math.Cos(t), qy = cy + r * Math.Sin(t);
            if (_penDown)
                Line(px, py, qx, qy);
            (px, py) = (qx, qy);
        }
        _x = _crX = px;
        _y = _crY = py;
    }

    private static int Steps(double sweepDeg, double chordDeg)
    {
        double chord = Math.Clamp(Math.Abs(chordDeg), 0.5, 180);
        return Math.Clamp((int)Math.Ceiling(sweepDeg / chord), 1, 720);
    }

    private void Rectangle(double ax, double ay, bool relative)
    {
        var (x2, y2) = relative
            ? (_x + ToPlotterDelta(ax, ay).X, _y + ToPlotterDelta(ax, ay).Y)
            : ToPlotter(ax, ay);
        Line(_x, _y, x2, _y);
        Line(x2, _y, x2, y2);
        Line(x2, y2, _x, y2);
        Line(_x, y2, _x, _y);
    }

    // ---------------------------------------------------------------- labels

    private void LabelByte(byte b)
    {
        if (b == _terminator)
        {
            FlushLabelRun();
            _state = State.Ground;
            return;
        }

        double dir = Direction;
        double cos = Math.Cos(dir), sin = Math.Sin(dir);
        switch (b)
        {
            case 0x0D: // carriage return: back to the start of the line
                FlushLabelRun();
                _x = _crX; _y = _crY;
                _runX = _x; _runY = _y;
                break;
            case 0x0A: // line feed: one line down (cell height = 2 x character height)
                FlushLabelRun();
                double down = 2 * CharHeight;
                _x += down * sin; _y -= down * cos;
                _crX += down * sin; _crY -= down * cos;
                _runX = _x; _runY = _y;
                break;
            case 0x08: // backspace
                FlushLabelRun();
                _x -= CharWidth * 1.5 * cos; _y -= CharWidth * 1.5 * sin;
                _runX = _x; _runY = _y;
                break;
            default:
                if (b < 0x20 || b == 0x7F)
                    return; // other control characters are not printed
                _labelRun.Append(b < 0x80 ? (char)b : '?');
                double adv = CharWidth * 1.5;
                _x += adv * cos; _y += adv * sin;
                break;
        }
    }

    private void FlushLabelRun()
    {
        if (_labelRun.Length == 0)
            return;
        string text = _labelRun.ToString();
        _labelRun.Clear();
        if (_pen > 0 && text.Trim().Length > 0)
            Document.AddLabel(new PlotLabel(_runX, _runY, text, _pen, CharWidth, CharHeight, Direction));
    }

    private void CharacterPlot(double spaces, double lines)
    {
        double dir = Direction;
        double cos = Math.Cos(dir), sin = Math.Sin(dir);
        double dx = spaces * CharWidth * 1.5;
        double dy = lines * CharHeight * 2;
        _x += dx * cos - dy * sin;
        _y += dx * sin + dy * cos;
        _crX = _x; _crY = _y;
    }
}
