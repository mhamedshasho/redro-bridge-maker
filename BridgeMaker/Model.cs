namespace RedroBridgeMaker;

public record Pt(double X, double Y);
public record Seg(Pt A, Pt B);

public class PathModel
{
    public List<Pt> Points { get; } = new();
    public List<Seg> Segments { get; } = new();
    public bool Closed { get; set; }
}

// One cut on one straight segment. A bridge crossing a corner is represented
// by adjacent Gap objects so SVG export remains exact.
public class Gap
{
    public Pt A { get; init; } = new(0, 0);
    public Pt B { get; init; } = new(0, 0);
}

public class DocumentModel
{
    public List<PathModel> Paths { get; } = new();
    public List<Gap> Gaps { get; } = new();
}
