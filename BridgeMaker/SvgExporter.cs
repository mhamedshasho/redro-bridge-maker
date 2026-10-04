using System.Globalization;
using System.IO;
using System.Text;

namespace RedroBridgeMaker;

static class SvgExporter
{
    public static void Save(DocumentModel d, string file)
    {
        if (!d.Paths.Any()) throw new InvalidOperationException("No vector paths loaded.");
        var points = d.Paths.SelectMany(p => p.Points).ToList();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var w = Math.Max(0.001, maxX - minX);
        var h = Math.Max(0.001, maxY - minY);
        static string F(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{F(w)}mm"" height=""{F(h)}mm"" viewBox=""{F(minX)} {F(minY)} {F(w)} {F(h)}"">");

        foreach (var path in d.Paths)
        foreach (var segment in path.Segments)
        {
            var cuts = new List<(double Start, double End)>();
            foreach (var gap in d.Gaps)
            {
                if (DistanceToSegment(gap.A, segment) > 0.01 || DistanceToSegment(gap.B, segment) > 0.01) continue;
                var t1 = Projection(gap.A, segment);
                var t2 = Projection(gap.B, segment);
                var start = Math.Clamp(Math.Min(t1, t2), 0, 1);
                var end = Math.Clamp(Math.Max(t1, t2), 0, 1);
                if (end > start) cuts.Add((start, end));
            }

            if (cuts.Count == 0)
            {
                WriteLine(sb, segment.A, segment.B, F);
                continue;
            }

            var cursor = 0.0;
            foreach (var cut in cuts.OrderBy(x => x.Start))
            {
                if (cut.Start > cursor)
                    WriteLine(sb, Lerp(segment.A, segment.B, cursor), Lerp(segment.A, segment.B, cut.Start), F);
                cursor = Math.Max(cursor, cut.End);
            }
            if (cursor < 1) WriteLine(sb, Lerp(segment.A, segment.B, cursor), segment.B, F);
        }

        sb.AppendLine("</svg>");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
    }

    static double DistanceToSegment(Pt p, Seg s)
    {
        var dx = s.B.X - s.A.X;
        var dy = s.B.Y - s.A.Y;
        var den = dx * dx + dy * dy;
        if (den <= 0) return Math.Sqrt(Math.Pow(p.X - s.A.X, 2) + Math.Pow(p.Y - s.A.Y, 2));
        var t = Math.Clamp(((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / den, 0, 1);
        var qx = s.A.X + dx * t;
        var qy = s.A.Y + dy * t;
        return Math.Sqrt(Math.Pow(p.X - qx, 2) + Math.Pow(p.Y - qy, 2));
    }

    static double Projection(Pt p, Seg s)
    {
        var dx = s.B.X - s.A.X;
        var dy = s.B.Y - s.A.Y;
        var den = dx * dx + dy * dy;
        if (den <= 0) return 0;
        return Math.Clamp(((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / den, 0, 1);
    }

    static Pt Lerp(Pt a, Pt b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    static void WriteLine(StringBuilder sb, Pt a, Pt b, Func<double, string> format)
    {
        sb.AppendLine($@"<line x1=""{format(a.X)}"" y1=""{format(a.Y)}"" x2=""{format(b.X)}"" y2=""{format(b.Y)}"" stroke=""black"" fill=""none"" />");
    }
}