namespace RedroBridgeMaker;
static class BridgeEngine
{
 static double Distance(Pt a,Pt b)=>Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));
 public static void Automatic(DocumentModel d,double len,double start,double end,double spacing){foreach(var p in d.Paths){var total=p.Segments.Sum(s=>Distance(s.A,s.B));if(total<start+len+end||spacing<=0)continue;for(var at=start;at+len<=total-end;at+=spacing)AddAt(p,d.Gaps,at,len);}}
 public static void AddAt(PathModel p,List<Gap> gaps,double at,double len){if(at<0||len<=0)return;var run=0.0;foreach(var s in p.Segments){var l=Distance(s.A,s.B);if(l<=0)continue;if(at>=run&&at<run+l){var localEnd=Math.Min(l,at-run+len);gaps.Add(new Gap{A=PointAt(s,at-run),B=PointAt(s,localEnd)});return;}run+=l;}}
 static Pt PointAt(Seg s,double d){var l=Distance(s.A,s.B);var t=l<=0?0:Math.Clamp(d/l,0,1);return new Pt(s.A.X+(s.B.X-s.A.X)*t,s.A.Y+(s.B.Y-s.A.Y)*t);}
}