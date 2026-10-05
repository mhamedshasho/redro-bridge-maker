namespace RedroBridgeMaker;

static class BridgeEngine
{
    static double Distance(Pt a, Pt b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    public static double PathLength(PathModel path) =>
        path.Segments.Sum(s => Distance(s.A, s.B));

    public static void Automatic(DocumentModel document, double length, double start, double end, double spacing)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start < 0 || end < 0) throw new ArgumentOutOfRangeException("Offsets cannot be negative.");
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        foreach (var path in document.Paths)
        {
            var total = PathLength(path);
            if (total < start + length + end) continue;
            for (var at = start; at + length <= total - end + 1e-9; at += spacing)
                AddAt(path, document.Gaps, at, length);
        }
    }

    // Adds the requested interval along the complete path, including across
    // corners. Each affected straight segment gets one exact cut.
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

    public static bool TryGetNearest(PathModel path, Pt point, out double distanceAlong, out double distance)
    {
        distanceAlong = 0;
        distance = double.MaxValue;
        var run = 0.0;
        foreach (var segment in path.Segments)
        {
            var dx = segment.B.X - segment.A.X;
            var dy = segment.B.Y - segment.A.Y;
            var den = dx * dx + dy * dy;
            if (den <= 1e-12) continue;
            var t = Math.Clamp(((point.X - segment.A.X) * dx + (point.Y - segment.A.Y) * dy) / den, 0, 1);
            var nearest = new Pt(segment.A.X + dx * t, segment.A.Y + dy * t);
            var candidateDistance = Distance(point, nearest);
            if (candidateDistance < distance)
            {
                distance = candidateDistance;
                distanceAlong = run + Math.Sqrt(den) * t;
            }
            run += Math.Sqrt(den);
        }
        return distance < double.MaxValue;
    }

    static Pt PointAt(Seg segment, double distance)
    {
        var length = Distance(segment.A, segment.B);
        var t = length <= 0 ? 0 : Math.Clamp(distance / length, 0, 1);
        return new Pt(segment.A.X + (segment.B.X - segment.A.X) * t,
                      segment.A.Y + (segment.B.Y - segment.A.Y) * t);
    }
}
