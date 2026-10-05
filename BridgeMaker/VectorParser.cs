using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RedroBridgeMaker;

static class VectorParser
{
    const double PointToMillimetre = 25.4 / 72.0;
    static readonly Regex SvgTokenRegex = new(@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    static readonly Regex NumberOrWord = new(@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z]+", RegexOptions.Compiled);

    public static DocumentModel Load(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".svg" => Svg(file),
            ".eps" or ".ps" => Eps(file),
            ".pdf" => Pdf(file),
            ".cdr" => Cdr(file),
            _ => throw new NotSupportedException("Supported formats are CorelDRAW (.cdr), PDF, EPS, PS and SVG.")
        };
    }

    static DocumentModel Svg(string file)
    {
        var document = new DocumentModel();
        var xml = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        foreach (var element in xml.Descendants())
        {
            var name = element.Name.LocalName.ToLowerInvariant();
            if (name == "path") ParseSvgPath((string?)element.Attribute("d") ?? "", document);
            else if (name is "line" or "rect" or "polyline" or "polygon") AddBasicSvgElement(element, name, document);
        }
        if (!document.Paths.Any()) throw new InvalidDataException("No vector paths were found in the SVG file.");
        return document;
    }

    static void ParseSvgPath(string data, DocumentModel document)
    {
        var tokens = SvgTokenRegex.Matches(data).Select(m => m.Value).ToList();
        var i = 0;
        char command = '\0';
        var current = new Pt(0, 0);
        var start = current;
        var previousControl = current;
        PathModel? path = null;

        bool IsCommand() => i < tokens.Count && char.IsLetter(tokens[i][0]);
        bool TryNumber(out double value)
        {
            value = 0;
            return i < tokens.Count && !IsCommand() && double.TryParse(tokens[i++], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        void EnsurePath()
        {
            path ??= new PathModel();
            if (!document.Paths.Contains(path)) document.Paths.Add(path);
        }
        void AddLine(Pt next)
        {
            EnsurePath();
            if (Distance(current, next) > 1e-9) path!.Segments.Add(new Seg(current, next));
            if (path!.Points.Count == 0) path.Points.Add(current);
            path.Points.Add(next);
            current = next;
        }

        while (i < tokens.Count)
        {
            if (IsCommand()) command = tokens[i++][0];
            var relative = char.IsLower(command);
            var upper = char.ToUpperInvariant(command);
            if (upper == 'Z')
            {
                if (path != null) { AddLine(start); path.Closed = true; }
                current = start; command = '\0'; continue;
            }
            if (upper == 'M')
            {
                if (!TryNumber(out var x) || !TryNumber(out var y)) break;
                var next = relative ? new Pt(current.X + x, current.Y + y) : new Pt(x, y);
                path = new PathModel(); document.Paths.Add(path); path.Points.Add(next);
                current = next; start = next; previousControl = next; command = relative ? 'l' : 'L'; continue;
            }
            if (path == null) { command = '\0'; continue; }
            switch (upper)
            {
                case 'L':
                    if (!TryNumber(out var lx) || !TryNumber(out var ly)) { command = '\0'; continue; }
                    AddLine(relative ? new Pt(current.X + lx, current.Y + ly) : new Pt(lx, ly)); break;
                case 'H':
                    if (!TryNumber(out var hx)) { command = '\0'; continue; }
                    AddLine(new Pt(relative ? current.X + hx : hx, current.Y)); break;
                case 'V':
                    if (!TryNumber(out var vy)) { command = '\0'; continue; }
                    AddLine(new Pt(current.X, relative ? current.Y + vy : vy)); break;
                case 'C':
                    if (!TryNumber(out var c1x) || !TryNumber(out var c1y) || !TryNumber(out var c2x) || !TryNumber(out var c2y) || !TryNumber(out var cx) || !TryNumber(out var cy)) { command = '\0'; continue; }
                    var c1 = relative ? new Pt(current.X + c1x, current.Y + c1y) : new Pt(c1x, c1y);
                    var c2 = relative ? new Pt(current.X + c2x, current.Y + c2y) : new Pt(c2x, c2y);
                    var ce = relative ? new Pt(current.X + cx, current.Y + cy) : new Pt(cx, cy);
                    AddCubic(path, current, c1, c2, ce); current = ce; previousControl = c2; break;
                case 'S':
                    if (!TryNumber(out var sx2) || !TryNumber(out var sy2) || !TryNumber(out var sex) || !TryNumber(out var sey)) { command = '\0'; continue; }
                    var sc1 = new Pt(2 * current.X - previousControl.X, 2 * current.Y - previousControl.Y);
                    var sc2 = relative ? new Pt(current.X + sx2, current.Y + sy2) : new Pt(sx2, sy2);
                    var se = relative ? new Pt(current.X + sex, current.Y + sey) : new Pt(sex, sey);
                    AddCubic(path, current, sc1, sc2, se); current = se; previousControl = sc2; break;
                case 'Q':
                    if (!TryNumber(out var qx) || !TryNumber(out var qy) || !TryNumber(out var qex) || !TryNumber(out var qey)) { command = '\0'; continue; }
                    var qc = relative ? new Pt(current.X + qx, current.Y + qy) : new Pt(qx, qy);
                    var qe = relative ? new Pt(current.X + qex, current.Y + qey) : new Pt(qex, qey);
                    AddQuadratic(path, current, qc, qe); current = qe; previousControl = qc; break;
                case 'T':
                    if (!TryNumber(out var tx) || !TryNumber(out var ty)) { command = '\0'; continue; }
                    var tc = new Pt(2 * current.X - previousControl.X, 2 * current.Y - previousControl.Y);
                    var te = relative ? new Pt(current.X + tx, current.Y + ty) : new Pt(tx, ty);
                    AddQuadratic(path, current, tc, te); current = te; previousControl = tc; break;
                case 'A':
                    // Elliptical arcs are safely approximated by their endpoint.
                    if (!TryNumber(out _) || !TryNumber(out _) || !TryNumber(out _) || !TryNumber(out _) || !TryNumber(out _) || !TryNumber(out var ax) || !TryNumber(out var ay)) { command = '\0'; continue; }
                    AddLine(relative ? new Pt(current.X + ax, current.Y + ay) : new Pt(ax, ay)); break;
                default: command = '\0'; break;
            }
        }
    }

    static void AddBasicSvgElement(XElement element, string name, DocumentModel document)
    {
        double N(string key) => double.TryParse((string?)element.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var path = new PathModel();
        if (name == "line") AddSegment(path, new Pt(N("x1"), N("y1")), new Pt(N("x2"), N("y2")));
        else if (name == "rect")
        {
            var a = new Pt(N("x"), N("y")); var b = new Pt(a.X + N("width"), a.Y); var c = new Pt(b.X, b.Y + N("height")); var d = new Pt(a.X, c.Y);
            AddSegment(path, a, b); AddSegment(path, b, c); AddSegment(path, c, d); AddSegment(path, d, a); path.Closed = true;
        }
        else
        {
            var values = Regex.Matches((string?)element.Attribute("points") ?? "", @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            for (var i = 0; i + 3 < values.Length; i += 2) AddSegment(path, new Pt(values[i], values[i + 1]), new Pt(values[i + 2], values[i + 3]));
            if (name == "polygon" && values.Length >= 4) { AddSegment(path, new Pt(values[^2], values[^1]), new Pt(values[0], values[1])); path.Closed = true; }
        }
        if (path.Segments.Count > 0) document.Paths.Add(path);
    }

    static void AddSegment(PathModel path, Pt a, Pt b) { if (path.Points.Count == 0) path.Points.Add(a); path.Segments.Add(new Seg(a, b)); path.Points.Add(b); }

    static DocumentModel Eps(string file)
    {
        var document = new DocumentModel();
        var text = File.ReadAllText(file, Encoding.ASCII);
        ParsePostScript(text, document);
        Scale(document, PointToMillimetre);
        if (!document.Paths.Any()) throw new InvalidDataException("No stroked vector paths were found in the EPS file.");
        return document;
    }

    static void ParsePostScript(string text, DocumentModel document)
    {
        var tokens = Regex.Matches(text, @"(?m)(?<!%)\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|/[A-Za-z]+|[A-Za-z]+)")
            .Select(m => m.Groups[1].Value).ToList();
        var stack = new Stack<double>(); PathModel? path = null; var cur = new Pt(0, 0); var start = cur;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { stack.Push(n); continue; }
            var op = tokens[i].TrimStart('/');
            double Pop() => stack.Count == 0 ? 0 : stack.Pop();
            void Move(Pt p) { path = new PathModel(); document.Paths.Add(path); path.Points.Add(p); cur = p; start = p; }
            void Line(Pt p) { if (path == null) Move(cur); AddSegment(path!, cur, p); cur = p; }
            switch (op)
            {
                case "newpath": case "n": path = null; break;
                case "moveto": case "m": { var y = Pop(); var x = Pop(); Move(new Pt(x, y)); break; }
                case "rmoveto": case "rm": { var y = Pop(); var x = Pop(); Move(new Pt(cur.X + x, cur.Y + y)); break; }
                case "lineto": case "l": { var y = Pop(); var x = Pop(); Line(new Pt(x, y)); break; }
                case "rlineto": case "rl": { var y = Pop(); var x = Pop(); Line(new Pt(cur.X + x, cur.Y + y)); break; }
                case "closepath": case "h": if (path != null) { Line(start); path.Closed = true; } break;
                case "curveto": case "c": { var y3 = Pop(); var x3 = Pop(); var y2 = Pop(); var x2 = Pop(); var y1 = Pop(); var x1 = Pop(); if (path != null) { AddCubic(path, cur, new Pt(x1, y1), new Pt(x2, y2), new Pt(x3, y3)); cur = new Pt(x3, y3); } break; }
                case "rcurveto": case "rc": { var y3 = Pop(); var x3 = Pop(); var y2 = Pop(); var x2 = Pop(); var y1 = Pop(); var x1 = Pop(); if (path != null) { var p = cur; var e = new Pt(p.X + x3, p.Y + y3); AddCubic(path, p, new Pt(p.X + x1, p.Y + y1), new Pt(p.X + x2, p.Y + y2), e); cur = e; } break; }
            }
        }
    }

    static DocumentModel Pdf(string file)
    {
        var bytes = File.ReadAllBytes(file); var document = new DocumentModel(); var marker = Encoding.ASCII.GetBytes("stream"); var endMarker = Encoding.ASCII.GetBytes("endstream");
        for (var offset = 0; (offset = IndexOf(bytes, marker, offset)) >= 0; offset += marker.Length)
        {
            var start = offset + marker.Length; while (start < bytes.Length && (bytes[start] == 10 || bytes[start] == 13 || bytes[start] == 32)) start++;
            var end = IndexOf(bytes, endMarker, start); if (end < 0) break;
            var payload = bytes[start..end]; var headerStart = Math.Max(0, offset - 1200); var header = Encoding.ASCII.GetString(bytes, headerStart, offset - headerStart);
            try
            {
                if (header.Contains("FlateDecode", StringComparison.Ordinal)) using (var input = new MemoryStream(payload)) using (var z = new ZLibStream(input, CompressionMode.Decompress)) using (var output = new MemoryStream()) { z.CopyTo(output); payload = output.ToArray(); }
                ParsePdfStream(Encoding.ASCII.GetString(payload), document);
            }
            catch (InvalidDataException) { }
        }
        Scale(document, PointToMillimetre);
        if (!document.Paths.Any()) throw new InvalidDataException("No vector paths could be read. The PDF may contain only images, or use an unsupported encryption/filter.");
        return document;
    }

    static void ParsePdfStream(string text, DocumentModel document)
    {
        var tokens = NumberOrWord.Matches(text).Select(m => m.Value).ToList(); var stack = new Stack<double>(); var states = new Stack<double[]>(); var matrix = new double[] { 1, 0, 0, 1, 0, 0 }; PathModel? path = null; var cur = new Pt(0, 0); var start = cur;
        double Pop() => stack.Count == 0 ? 0 : stack.Pop();
        Pt Transform(double x, double y) => new(matrix[0] * x + matrix[2] * y + matrix[4], matrix[1] * x + matrix[3] * y + matrix[5]);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { stack.Push(n); continue; }
            switch (tokens[i])
            {
                case "q": states.Push((double[])matrix.Clone()); break;
                case "Q": if (states.Count > 0) matrix = states.Pop(); break;
                case "cm": { var f = Pop(); var e = Pop(); var d = Pop(); var c = Pop(); var b = Pop(); var a = Pop(); matrix = new[] { matrix[0] * a + matrix[2] * b, matrix[1] * a + matrix[3] * b, matrix[0] * c + matrix[2] * d, matrix[1] * c + matrix[3] * d, matrix[0] * e + matrix[2] * f + matrix[4], matrix[1] * e + matrix[3] * f + matrix[5] }; break; }
                case "m": { var y = Pop(); var x = Pop(); cur = Transform(x, y); path = new PathModel(); path.Points.Add(cur); document.Paths.Add(path); start = cur; break; }
                case "l": { var y = Pop(); var x = Pop(); var p = Transform(x, y); if (path == null) { path = new PathModel(); path.Points.Add(cur); document.Paths.Add(path); } AddSegment(path, cur, p); cur = p; break; }
                case "c": { var y3 = Pop(); var x3 = Pop(); var y2 = Pop(); var x2 = Pop(); var y1 = Pop(); var x1 = Pop(); if (path != null) { var p1 = Transform(x1, y1); var p2 = Transform(x2, y2); var p3 = Transform(x3, y3); AddCubic(path, cur, p1, p2, p3); cur = p3; } break; }
                case "v": { var y3 = Pop(); var x3 = Pop(); if (path != null) { var p = Transform(x3, y3); AddCubic(path, cur, cur, cur, p); cur = p; } break; }
                case "y": { var y3 = Pop(); var x3 = Pop(); var x2 = Pop(); var y2 = Pop(); if (path != null) { var p2 = Transform(x2, y2); var p3 = Transform(x3, y3); AddCubic(path, cur, p2, p3, p3); cur = p3; } break; }
                case "re": { var h = Pop(); var w = Pop(); var y = Pop(); var x = Pop(); var a = Transform(x, y); var b = Transform(x + w, y); var c = Transform(x + w, y + h); var d = Transform(x, y + h); path = new PathModel(); document.Paths.Add(path); AddSegment(path, a, b); AddSegment(path, b, c); AddSegment(path, c, d); AddSegment(path, d, a); path.Closed = true; cur = a; start = a; break; }
                case "h": if (path != null) { AddSegment(path, cur, start); path.Closed = true; cur = start; } break;
                default: stack.Clear(); break;
            }
        }
    }

    static DocumentModel Cdr(string file)
    {
        var inkscape = FindOnPath("inkscape");
        if (inkscape == null) throw new NotSupportedException("CorelDRAW files require Inkscape to be installed and available in PATH. Export the CDR as PDF/EPS/SVG, or install Inkscape and try again.");
        var temp = Path.Combine(Path.GetTempPath(), "bridge-maker-" + Guid.NewGuid().ToString("N") + ".svg");
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(inkscape) { UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add(file); process.StartInfo.ArgumentList.Add("--export-plain-svg"); process.StartInfo.ArgumentList.Add("--export-filename=" + temp);
            process.Start(); process.WaitForExit(30000);
            if (process.ExitCode != 0 || !File.Exists(temp)) throw new InvalidDataException("Inkscape could not import this CDR file.");
            return Svg(temp);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    static string? FindOnPath(string executable)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            paths = paths.Concat(new[] { Path.Combine(programFiles, "Inkscape", "bin"), Path.Combine(programFilesX86, "Inkscape", "bin") }).ToArray();
        }
        foreach (var path in paths) { var candidate = Path.Combine(path, executable); if (File.Exists(candidate)) return candidate; if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe"; }
        return null;
    }

    static int IndexOf(byte[] source, byte[] value, int start)
    {
        for (var i = start; i <= source.Length - value.Length; i++) { var ok = true; for (var j = 0; j < value.Length; j++) if (source[i + j] != value[j]) { ok = false; break; } if (ok) return i; }
        return -1;
    }

    static void Scale(DocumentModel document, double scale)
    {
        foreach (var path in document.Paths)
        {
            for (var i = 0; i < path.Points.Count; i++) path.Points[i] = new Pt(path.Points[i].X * scale, path.Points[i].Y * scale);
            for (var i = 0; i < path.Segments.Count; i++) { var s = path.Segments[i]; path.Segments[i] = new Seg(new Pt(s.A.X * scale, s.A.Y * scale), new Pt(s.B.X * scale, s.B.Y * scale)); }
        }
    }

    static double Distance(Pt a, Pt b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    static void AddQuadratic(PathModel path, Pt p0, Pt p1, Pt p2) { AddCubic(path, p0, new Pt(p0.X + 2.0 / 3 * (p1.X - p0.X), p0.Y + 2.0 / 3 * (p1.Y - p0.Y)), new Pt(p2.X + 2.0 / 3 * (p1.X - p2.X), p2.Y + 2.0 / 3 * (p1.Y - p2.Y)), p2); }
    static void AddCubic(PathModel path, Pt p0, Pt p1, Pt p2, Pt p3)
    {
        const int steps = 16;
        var previous = p0;
        for (var i = 1; i <= steps; i++) { var t = i / (double)steps; var u = 1 - t; var next = new Pt(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X, u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y); AddSegment(path, previous, next); previous = next; }
    }
}
