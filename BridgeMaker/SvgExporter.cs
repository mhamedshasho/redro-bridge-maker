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
        var minX = points.Min(p => p.X); var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X); var maxY = points.Max(p => p.Y);
        var w = Math.Max(0.001, maxX - minX); var h = Math.Max(0.001, maxY - minY);
        static string F(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{F(w)}mm"" height=""{F(h)}mm"" viewBox=""{F(minX)} {F(minY)} {F(w)} {F(h)}"">");
        foreach (var path in d.Paths)
            foreach (var segment in path.Segments)
                foreach (var visible in BridgeEngine.VisibleParts(segment, d.Gaps))
                    WriteLine(sb, visible.A, visible.B, F);
        sb.AppendLine("</svg>");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
    }

    static void WriteLine(StringBuilder sb, Pt a, Pt b, Func<double, string> format) =>
        sb.AppendLine($@"<line x1=""{format(a.X)}"" y1=""{format(a.Y)}"" x2=""{format(b.X)}"" y2=""{format(b.Y)}"" stroke=""black"" fill=""none"" />");
}
