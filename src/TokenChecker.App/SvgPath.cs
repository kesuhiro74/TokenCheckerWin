using System.Drawing.Drawing2D;
using System.Globalization;

namespace TokenChecker.App;

// Minimal SVG path-data -> GraphicsPath converter, scoped to the commands the
// Octicons copilot glyph uses (M/m L/l H/h V/v C/c A/a Z/z). Arcs are converted
// via the SVG endpoint->center parameterization (rotation assumed 0, which holds
// for Octicons) and added with GraphicsPath.AddArc.
internal static class SvgPath
{
    public static void AddToPath(GraphicsPath path, string d)
    {
        var t = new Tokenizer(d);
        char cmd = '\0';
        PointF cur = default, start = default;
        var open = false;

        while (!t.AtEnd)
        {
            if (t.NextIsCommand)
            {
                cmd = t.ReadCommand();
            }

            switch (cmd)
            {
                case 'M':
                    cur = new PointF(t.ReadNumber(), t.ReadNumber());
                    if (open) { path.CloseFigure(); }
                    path.StartFigure();
                    open = true;
                    start = cur;
                    cmd = 'L';
                    break;
                case 'm':
                    cur = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                    if (open) { path.CloseFigure(); }
                    path.StartFigure();
                    open = true;
                    start = cur;
                    cmd = 'l';
                    break;
                case 'L':
                    cur = AddLine(path, cur, new PointF(t.ReadNumber(), t.ReadNumber()));
                    break;
                case 'l':
                    cur = AddLine(path, cur, new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber()));
                    break;
                case 'H':
                    cur = AddLine(path, cur, new PointF(t.ReadNumber(), cur.Y));
                    break;
                case 'h':
                    cur = AddLine(path, cur, new PointF(cur.X + t.ReadNumber(), cur.Y));
                    break;
                case 'V':
                    cur = AddLine(path, cur, new PointF(cur.X, t.ReadNumber()));
                    break;
                case 'v':
                    cur = AddLine(path, cur, new PointF(cur.X, cur.Y + t.ReadNumber()));
                    break;
                case 'C':
                {
                    var c1 = new PointF(t.ReadNumber(), t.ReadNumber());
                    var c2 = new PointF(t.ReadNumber(), t.ReadNumber());
                    var e = new PointF(t.ReadNumber(), t.ReadNumber());
                    path.AddBezier(cur, c1, c2, e);
                    cur = e;
                    break;
                }
                case 'c':
                {
                    var c1 = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                    var c2 = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                    var e = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                    path.AddBezier(cur, c1, c2, e);
                    cur = e;
                    break;
                }
                case 'A':
                {
                    var rx = t.ReadNumber();
                    var ry = t.ReadNumber();
                    var rot = t.ReadNumber();
                    var laf = t.ReadFlag();
                    var sf = t.ReadFlag();
                    var e = new PointF(t.ReadNumber(), t.ReadNumber());
                    cur = AddArc(path, cur, rx, ry, rot, laf, sf, e);
                    break;
                }
                case 'a':
                {
                    var rx = t.ReadNumber();
                    var ry = t.ReadNumber();
                    var rot = t.ReadNumber();
                    var laf = t.ReadFlag();
                    var sf = t.ReadFlag();
                    var e = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                    cur = AddArc(path, cur, rx, ry, rot, laf, sf, e);
                    break;
                }
                case 'Z':
                case 'z':
                    path.CloseFigure();
                    open = false;
                    cur = start;
                    cmd = '\0';
                    break;
                default:
                    return; // Unsupported command — stop safely.
            }
        }
    }

    private static PointF AddLine(GraphicsPath path, PointF from, PointF to)
    {
        if (from != to)
        {
            path.AddLine(from, to);
        }

        return to;
    }

    private static PointF AddArc(GraphicsPath path, PointF p0, float rx, float ry, float rotDeg, bool largeArc, bool sweep, PointF p1)
    {
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-4f || ry < 1e-4f || p0 == p1)
        {
            return AddLine(path, p0, p1);
        }

        // Octicons arcs have no x-axis rotation; treat phi = 0.
        double x1p = (p0.X - p1.X) / 2.0;
        double y1p = (p0.Y - p1.Y) / 2.0;

        // Scale up radii if they are too small to span the endpoints.
        double lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lambda > 1.0)
        {
            var s = Math.Sqrt(lambda);
            rx = (float)(rx * s);
            ry = (float)(ry * s);
        }

        double rx2 = (double)rx * rx;
        double ry2 = (double)ry * ry;
        double num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
        double den = rx2 * y1p * y1p + ry2 * x1p * x1p;
        double co = den <= 0 ? 0 : Math.Sqrt(Math.Max(0.0, num / den));
        if (largeArc == sweep)
        {
            co = -co;
        }

        double cxp = co * (rx * y1p) / ry;
        double cyp = co * -(ry * x1p) / rx;
        double cx = cxp + (p0.X + p1.X) / 2.0;
        double cy = cyp + (p0.Y + p1.Y) / 2.0;

        double startAngle = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        path.AddArc(
            (float)(cx - rx), (float)(cy - ry), (float)(2 * rx), (float)(2 * ry),
            (float)(startAngle * 180.0 / Math.PI), (float)(delta * 180.0 / Math.PI));
        return p1;
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        double a = len <= 0 ? 0 : Math.Acos(Math.Clamp(dot / len, -1.0, 1.0));
        return ux * vy - uy * vx < 0 ? -a : a;
    }

    private sealed class Tokenizer
    {
        private readonly string _s;
        private int _i;

        public Tokenizer(string s) => _s = s;

        private void SkipSep()
        {
            while (_i < _s.Length)
            {
                var c = _s[_i];
                if (c is ' ' or ',' or '\t' or '\n' or '\r')
                {
                    _i++;
                }
                else
                {
                    break;
                }
            }
        }

        public bool AtEnd
        {
            get
            {
                SkipSep();
                return _i >= _s.Length;
            }
        }

        public bool NextIsCommand
        {
            get
            {
                SkipSep();
                return _i < _s.Length && char.IsLetter(_s[_i]);
            }
        }

        public char ReadCommand()
        {
            SkipSep();
            return _i < _s.Length ? _s[_i++] : '\0';
        }

        public float ReadNumber()
        {
            SkipSep();
            var startIndex = _i;
            if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
            {
                _i++;
            }

            while (_i < _s.Length && char.IsDigit(_s[_i]))
            {
                _i++;
            }

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    _i++;
                }
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                {
                    _i++;
                }

                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    _i++;
                }
            }

            // Total parse: a malformed/empty span yields 0 rather than throwing,
            // so the glyph builder can never crash the paint pipeline.
            return float.TryParse(_s.AsSpan(startIndex, _i - startIndex), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }

        // Arc flags are a single '0' or '1', which may be packed with no separator.
        public bool ReadFlag()
        {
            SkipSep();
            return _i < _s.Length && _s[_i++] == '1';
        }
    }
}
