namespace RedroBridgeMaker;
public record Pt(double X,double Y);
public record Seg(Pt A,Pt B);
public class PathModel { public List<Pt> Points=new(); public List<Seg> Segments=new(); public bool Closed; }
public class Gap { public Pt A=new(0,0); public Pt B=new(0,0); }
public class DocumentModel { public List<PathModel> Paths=new(); public List<Gap> Gaps=new(); }