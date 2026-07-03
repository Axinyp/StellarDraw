using System.Globalization;

namespace WhiteBoard.Core.Algorithms;

/// <summary>
/// 计算 SVG path 数据的精确几何包围盒（纯数学，不依赖 WPF）。
/// <para>
/// 支持绝对/相对命令 M/L/H/V/C/S/Q/T/A/Z：贝塞尔曲线用导数极值精确求界，
/// 弧用中心参数化采样近似。目的是让 <see cref="Domain.Objects.PathObject"/> 的 Bounds
/// 与渲染层 <c>Geometry.Parse</c> 的实际几何一致，避免选中框相对图形偏移。
/// </para>
/// </summary>
public static class SvgPathBounds
{
    private readonly struct Tok
    {
        public readonly bool IsNum;
        public readonly double Num;
        public readonly char Cmd;
        private Tok(bool isNum, double num, char cmd) { IsNum = isNum; Num = num; Cmd = cmd; }
        public static Tok Number(double v) => new(true, v, ' ');
        public static Tok Command(char c) => new(false, 0, c);
    }

    public static (float x, float y, float w, float h) Compute(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData)) return (0f, 0f, 0f, 0f);

        var toks = Tokenize(pathData);
        if (toks.Count == 0) return (0f, 0f, 0f, 0f);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        void Include(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            any = true;
        }

        int i = 0;
        double cx = 0, cy = 0;      // 当前点
        double sx = 0, sy = 0;      // 子路径起点
        double lastC2x = 0, lastC2y = 0; // 上段三次贝塞尔第二控制点（S 反射用）
        double lastQx = 0, lastQy = 0;   // 上段二次贝塞尔控制点（T 反射用）
        char cmd = ' ';
        char prevCmd = ' ';

        bool Has(int need) => i + need <= toks.Count && AllNum(toks, i, need);
        double N() => toks[i++].Num;

        while (i < toks.Count)
        {
            if (!toks[i].IsNum)
            {
                cmd = toks[i].Cmd;
                i++;
            }
            else
            {
                // 隐式重复：M 之后的坐标对按 L 处理
                if (cmd == 'M') cmd = 'L';
                else if (cmd == 'm') cmd = 'l';
            }

            bool rel = char.IsLower(cmd);
            char c = char.ToUpperInvariant(cmd);

            switch (c)
            {
                case 'M':
                    if (!Has(2)) { i = toks.Count; break; }
                    { double x = N(), y = N(); if (rel) { x += cx; y += cy; } cx = x; cy = y; sx = x; sy = y; Include(cx, cy); }
                    break;
                case 'L':
                    if (!Has(2)) { i = toks.Count; break; }
                    { double x = N(), y = N(); if (rel) { x += cx; y += cy; } cx = x; cy = y; Include(cx, cy); }
                    break;
                case 'H':
                    if (!Has(1)) { i = toks.Count; break; }
                    { double x = N(); if (rel) x += cx; cx = x; Include(cx, cy); }
                    break;
                case 'V':
                    if (!Has(1)) { i = toks.Count; break; }
                    { double y = N(); if (rel) y += cy; cy = y; Include(cx, cy); }
                    break;
                case 'C':
                    if (!Has(6)) { i = toks.Count; break; }
                    {
                        double x1 = N(), y1 = N(), x2 = N(), y2 = N(), x = N(), y = N();
                        if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                        CubicBounds(cx, cy, x1, y1, x2, y2, x, y, Include);
                        lastC2x = x2; lastC2y = y2; cx = x; cy = y;
                    }
                    break;
                case 'S':
                    if (!Has(4)) { i = toks.Count; break; }
                    {
                        double x2 = N(), y2 = N(), x = N(), y = N();
                        if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; }
                        double x1, y1;
                        if (prevCmd is 'C' or 'c' or 'S' or 's') { x1 = 2 * cx - lastC2x; y1 = 2 * cy - lastC2y; }
                        else { x1 = cx; y1 = cy; }
                        CubicBounds(cx, cy, x1, y1, x2, y2, x, y, Include);
                        lastC2x = x2; lastC2y = y2; cx = x; cy = y;
                    }
                    break;
                case 'Q':
                    if (!Has(4)) { i = toks.Count; break; }
                    {
                        double x1 = N(), y1 = N(), x = N(), y = N();
                        if (rel) { x1 += cx; y1 += cy; x += cx; y += cy; }
                        QuadBounds(cx, cy, x1, y1, x, y, Include);
                        lastQx = x1; lastQy = y1; cx = x; cy = y;
                    }
                    break;
                case 'T':
                    if (!Has(2)) { i = toks.Count; break; }
                    {
                        double x = N(), y = N();
                        if (rel) { x += cx; y += cy; }
                        double x1, y1;
                        if (prevCmd is 'Q' or 'q' or 'T' or 't') { x1 = 2 * cx - lastQx; y1 = 2 * cy - lastQy; }
                        else { x1 = cx; y1 = cy; }
                        QuadBounds(cx, cy, x1, y1, x, y, Include);
                        lastQx = x1; lastQy = y1; cx = x; cy = y;
                    }
                    break;
                case 'A':
                    if (!Has(7)) { i = toks.Count; break; }
                    {
                        double rx = N(), ry = N(), rot = N(), laf = N(), sf = N(), x = N(), y = N();
                        if (rel) { x += cx; y += cy; }
                        ArcBounds(cx, cy, rx, ry, rot, laf != 0, sf != 0, x, y, Include);
                        cx = x; cy = y;
                    }
                    break;
                case 'Z':
                    cx = sx; cy = sy; Include(cx, cy);
                    break;
                default:
                    // 未知命令：跳过一个 token 防死循环
                    if (i < toks.Count && toks[i].IsNum) i++;
                    break;
            }

            prevCmd = cmd;
        }

        if (!any) return (0f, 0f, 0f, 0f);
        return ((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
    }

    private static bool AllNum(List<Tok> t, int start, int count)
    {
        for (int k = 0; k < count; k++)
            if (!t[start + k].IsNum) return false;
        return true;
    }

    // ── 贝塞尔精确包围盒（含端点 + 导数为零的极值点）─────────────────────────

    private static void CubicBounds(double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, Action<double, double> inc)
    {
        inc(x0, y0);
        inc(x3, y3);
        foreach (var t in CubicExtrema(x0, x1, x2, x3))
            inc(Cubic(x0, x1, x2, x3, t), Cubic(y0, y1, y2, y3, t));
        foreach (var t in CubicExtrema(y0, y1, y2, y3))
            inc(Cubic(x0, x1, x2, x3, t), Cubic(y0, y1, y2, y3, t));
    }

    private static double Cubic(double a, double b, double c, double d, double t)
    {
        double mt = 1 - t;
        return mt * mt * mt * a + 3 * mt * mt * t * b + 3 * mt * t * t * c + t * t * t * d;
    }

    private static IEnumerable<double> CubicExtrema(double a, double b, double c, double d)
    {
        // B'(t)=0 → A t^2 + B t + C = 0，A=u-2v+w, B=2(v-u), C=u，其中 u=b-a,v=c-b,w=d-c
        double u = b - a, v = c - b, w = d - c;
        double A = u - 2 * v + w, B = 2 * (v - u), Cc = u;
        foreach (var t in SolveQuadraticInUnit(A, B, Cc)) yield return t;
    }

    private static void QuadBounds(double x0, double y0, double x1, double y1,
        double x2, double y2, Action<double, double> inc)
    {
        inc(x0, y0);
        inc(x2, y2);
        foreach (var t in QuadExtrema(x0, x1, x2))
            inc(Quad(x0, x1, x2, t), Quad(y0, y1, y2, t));
        foreach (var t in QuadExtrema(y0, y1, y2))
            inc(Quad(x0, x1, x2, t), Quad(y0, y1, y2, t));
    }

    private static double Quad(double a, double b, double c, double t)
    {
        double mt = 1 - t;
        return mt * mt * a + 2 * mt * t * b + t * t * c;
    }

    private static IEnumerable<double> QuadExtrema(double a, double b, double c)
    {
        double denom = a - 2 * b + c;
        if (Math.Abs(denom) < 1e-12) yield break;
        double t = (a - b) / denom;
        if (t > 0 && t < 1) yield return t;
    }

    private static IEnumerable<double> SolveQuadraticInUnit(double a, double b, double c)
    {
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12)
            {
                double t = -c / b;
                if (t > 0 && t < 1) yield return t;
            }
            yield break;
        }
        double disc = b * b - 4 * a * c;
        if (disc < 0) yield break;
        double sq = Math.Sqrt(disc);
        double t1 = (-b + sq) / (2 * a);
        double t2 = (-b - sq) / (2 * a);
        if (t1 > 0 && t1 < 1) yield return t1;
        if (t2 > 0 && t2 < 1) yield return t2;
    }

    // ── 弧：endpoint→center 参数化后采样 ───────────────────────────────────────

    private static void ArcBounds(double x1, double y1, double rx, double ry, double rotDeg,
        bool largeArc, bool sweep, double x2, double y2, Action<double, double> inc)
    {
        inc(x1, y1);
        inc(x2, y2);
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-9 || ry < 1e-9) return; // 退化为直线，端点已计入

        double phi = rotDeg * Math.PI / 180.0;
        double cosP = Math.Cos(phi), sinP = Math.Sin(phi);

        double dx = (x1 - x2) / 2.0, dy = (y1 - y2) / 2.0;
        double x1p = cosP * dx + sinP * dy;
        double y1p = -sinP * dx + cosP * dy;

        double lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lam > 1) { double s = Math.Sqrt(lam); rx *= s; ry *= s; }

        double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double co = Math.Sqrt(Math.Max(0, num / den));
        if (largeArc == sweep) co = -co;

        double cxp = co * rx * y1p / ry;
        double cyp = co * -ry * x1p / rx;
        double cx = cosP * cxp - sinP * cyp + (x1 + x2) / 2.0;
        double cy = sinP * cxp + cosP * cyp + (y1 + y2) / 2.0;

        double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double a = Math.Acos(Math.Clamp(dot / len, -1, 1));
            if (ux * vy - uy * vx < 0) a = -a;
            return a;
        }

        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dTheta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dTheta > 0) dTheta -= 2 * Math.PI;
        else if (sweep && dTheta < 0) dTheta += 2 * Math.PI;

        const int Samples = 32;
        for (int k = 1; k < Samples; k++)
        {
            double theta = theta1 + dTheta * k / Samples;
            double px = cosP * rx * Math.Cos(theta) - sinP * ry * Math.Sin(theta) + cx;
            double py = sinP * rx * Math.Cos(theta) + cosP * ry * Math.Sin(theta) + cy;
            inc(px, py);
        }
    }

    // ── Tokenizer ──────────────────────────────────────────────────────────────

    private static List<Tok> Tokenize(string s)
    {
        var list = new List<Tok>();
        int n = s.Length, i = 0;
        while (i < n)
        {
            char ch = s[i];
            if (char.IsLetter(ch)) { list.Add(Tok.Command(ch)); i++; continue; }
            if (ch is ' ' or ',' or '\t' or '\n' or '\r') { i++; continue; }

            int start = i;
            if (ch is '+' or '-') i++;
            bool dot = false, dig = false;
            while (i < n)
            {
                char cc = s[i];
                if (cc >= '0' && cc <= '9') { dig = true; i++; }
                else if (cc == '.' && !dot) { dot = true; i++; }
                else if (cc is 'e' or 'E')
                {
                    i++;
                    if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                }
                else break;
            }
            if (i > start && dig &&
                double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var v))
                list.Add(Tok.Number(v));
            else
                i = start + 1; // 跳过无法识别的字符
        }
        return list;
    }
}
