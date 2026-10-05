namespace RedroBridgeMaker;

static class BridgeEngine
{
    static double Distance(Pt a, Pt b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    public static double PathLength(PathModel path) => path.Segments.Sum(s => Distance(s.A, s.B));

    // spacing is the target distance between bridge starts. Any leftover is
    // placed in the middle gap; if it reaches 10 cm, a bridge is added.
    public const double ExtraBridgeThreshold = 100.0; // internal unit: mm
    public static int Automatic(DocumentModel document, double length, double start, double end, double spacing, int requestedCount = 0)
    {
        Validate(length, start, end, spacing, requestedCount);
        var placed = 0;
        foreach (var path in document.Paths)
        {
            var count = PlaceSeries(path, document.Gaps, length, start, end, spacing, requestedCount);
            placed += count;
        }
        document.BridgeCount += placed;
        return placed;
    }

    public static int PlaceSeries(PathModel path, List<Gap> gaps, double length, double start, double end, double spacing, int requestedCount)
    {
        Validate(length, start, end, spacing, requestedCount);
        var total = PathLength(path);
        var usable = total - start - end - length;
        if (usable < 0) return 0;
        if (requestedCount == 0) return PlaceBalanced(path, gaps, length, start, usable, spacing);
        var available = (int)Math.Floor(usable / spacing) + 1;
        var count = Math.Min(requestedCount, available);
        for (var i = 0; i < count; i++) AddAt(path, gaps, start + i * spacing, length);
        return count;
    }

    static int PlaceBalanced(PathModel path, List<Gap> gaps, double length, double start, double usable, double spacing)
    {
        var intervals = (int)Math.Floor(usable / spacing);
        var remainder = usable - intervals * spacing;
        if (intervals <= 0) return AddAt(path, gaps, start, length);

        // Keep the requested spacing, but make the centre gap equal to
        // spacing + remainder. Example: 7.5 cm target plus 1.5 cm leftover
        // becomes 7.5 cm, 9 cm, 7.5 cm, matching the CorelDRAW example.
        var middleGap = intervals / 2;
        var centralGap = spacing + remainder;
        var position = start;
        var bridgeCount = intervals + 1;
        for (var i = 0; i < bridgeCount; i++)
        {
            AddAt(path, gaps, position, length);
            if (i < intervals) position += i == middleGap ? centralGap : spacing;
        }

        // If the resulting centre gap reaches 10 cm, place one bridge in its
        // middle. This is the requested "extra gap >= 10 cm" rule.
        if (centralGap >= ExtraBridgeThreshold)
        {
            var centreStart = start + middleGap * spacing;
            AddAt(path, gaps, centreStart + centralGap / 2, length);
            return bridgeCount + 1;
        }
        return bridgeCount;
    }

    public static int AddAt(PathModel path, List<Gap> gaps, double at, double length)
    {
        if (at < 0 || length <= 0) return 0;
        var total = PathLength(path);
        if (at >= total || at + length > total + 1e-9) return 0;
        var end = Math.Min(total, at + length);
        var run = 0.0;
        var added = 0;
        foreach (var segment in path.Segments)
        {
            var segmentLength = Distance(segment.A, segment.B);
            if (segmentLength <= 1e-9) continue;
            var localStart = Math.Max(0, at - run);
            var localEnd = Math.Min(segmentLength, end - run);
            if (localEnd > localStart + 1e-9)
            {
                gaps.Add(new Gap { A = PointAt(segment, localStart), B = PointAt(segment, localEnd) });
                added++;
            }
            run += segmentLength;
            if (run >= end - 1e-9) break;
        }
        return added;
    }

    public static IEnumerable<Seg> VisibleParts(Seg segment, IEnumerable<Gap> gaps)
    {
        var cuts = gaps.Where(g => DistanceToSegment(g.A, segment) <= 0.01 && DistanceToSegment(g.B, segment) <= 0.01)
            .Select(g => (Start: Math.Clamp(Math.Min(Projection(g.A, segment), Projection(g.B, segment)), 0, 1), End: Math.Clamp(Math.Max(Projection(g.A, segment), Projection(g.B, segment)), 0, 1)))
            .Where(c => c.End > c.Start + 1e-9).OrderBy(c => c.Start).ToList();
        var cursor = 0.0;
        foreach (var cut in cuts)
        {
            if (cut.Start > cursor + 1e-9) yield return new Seg(Lerp(segment.A, segment.B, cursor), Lerp(segment.A, segment.B, cut.Start));
            cursor = Math.Max(cursor, cut.End);
        }
        if (cursor < 1 - 1e-9) yield return new Seg(Lerp(segment.A, segment.B, cursor), segment.B);
    }

    public static bool TryGetNearest(PathModel path, Pt point, out double distanceAlong, out double distance)
    {
        distanceAlong = 0; distance = double.MaxValue; var run = 0.0;
        foreach (var segment in path.Segments)
        {
            var dx = segment.B.X - segment.A.X; var dy = segment.B.Y - segment.A.Y; var den = dx * dx + dy * dy;
            if (den <= 1e-12) continue;
            var t = Math.Clamp(((point.X - segment.A.X) * dx + (point.Y - segment.A.Y) * dy) / den, 0, 1);
            var nearest = new Pt(segment.A.X + dx * t, segment.A.Y + dy * t);
            var candidateDistance = Distance(point, nearest);
            if (candidateDistance < distance) { distance = candidateDistance; distanceAlong = run + Math.Sqrt(den) * t; }
            run += Math.Sqrt(den);
        }
        return distance < double.MaxValue;
    }

    static void Validate(double length, double start, double end, double spacing, int count)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start < 0 || end < 0) throw new ArgumentOutOfRangeException("Offsets cannot be negative.");
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
    }

    static Pt PointAt(Seg segment, double distance)
    {
        var length = Distance(segment.A, segment.B); var t = length <= 0 ? 0 : Math.Clamp(distance / length, 0, 1);
        return new Pt(segment.A.X + (segment.B.X - segment.A.X) * t, segment.A.Y + (segment.B.Y - segment.A.Y) * t);
    }

    static double DistanceToSegment(Pt p, Seg s)
    {
        var t = Projection(p, s); var q = Lerp(s.A, s.B, t); return Distance(p, q);
    }
    static double Projection(Pt p, Seg s)
    {
        var dx = s.B.X - s.A.X; var dy = s.B.Y - s.A.Y; var den = dx * dx + dy * dy;
        return den <= 1e-12 ? 0 : Math.Clamp(((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / den, 0, 1);
    }
    static Pt Lerp(Pt a, Pt b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
