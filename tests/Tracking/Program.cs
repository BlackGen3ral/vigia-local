using Vigia;

var line = new EntryLine(.2,.5,.8,.5);
int checks = 0;
void Check(string name, int actual, int expected)
{ if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}"); checks++; Console.WriteLine("PASS " + name); }
PersonBox Person(double x, double foot) => new(x-.04, foot-.188, .08, .2);
int Walk(double[] ys, EntryLine? boundary = null, double x = .5)
{
    var tracker = new EntryTracker(); int count = 0;
    for (int i=0;i<ys.Length;i++) count += tracker.Update([Person(x,ys[i])],boundary??line,i*.22);
    return count;
}
double[] entry = [.40,.42,.45,.48,.51,.54,.57,.60];
Check("one entry",Walk(entry),1);
Check("exit excluded",Walk(entry.Reverse().ToArray()),0);
Check("reverse direction",Walk(entry.Reverse().ToArray(),line with { Reverse=true }),1);
Check("start inside",Walk([.56,.57,.56,.58,.59]),0);
Check("jitter on line",Walk([.49,.50,.51,.49,.51,.50,.49,.51]),0);
Check("outside segment",Walk(entry,x:.95),0);
Check("outside stays outside",Walk([.3,.32,.34,.35,.36]),0);
var group = new EntryTracker(); int groups = 0;
for(int i=0;i<entry.Length;i++) groups += group.Update([Person(.35,entry[i]),Person(.65,entry[i])],line,i*.22);
Check("two simultaneous entries",groups,2);
var missing = new EntryTracker(); int brief=0;
for(int i=0;i<entry.Length;i++) brief+=missing.Update(i==3 ? [] : [Person(.5,entry[i])],line,i*.22);
Check("brief occlusion",brief,1);
var gap = new EntryTracker(); gap.Update([Person(.5,.4)],line,0);gap.Update([Person(.5,.42)],line,.22);
Check("no inferred entry across long gap",gap.Update([Person(.5,.56)],line,2)+gap.Update([Person(.5,.58)],line,2.22),0);
Check("reconnect starts inside",Walk([.56,.57,.58,.59]),0);
var changed = new EntryTracker(); changed.Update([Person(.5,.4)],line,0);changed.Update([Person(.5,.42)],line,.22);
Check("changing line resets baseline",changed.Update([Person(.5,.56)],line with { Y1=.45,Y2=.45 },.44)+changed.Update([Person(.5,.58)],line with { Y1=.45,Y2=.45 },.66),0);
var returner=new EntryTracker();int repeated=0;double time=0;
foreach(double y in entry.Concat(entry.Reverse()).Concat(Enumerable.Repeat(.40,8)).Concat(entry))
{ repeated+=returner.Update([Person(.5,y)],line,time); time+=.22; }
Check("exit and return counts another entry",repeated,2);
Check("line geometry rejects invalid settings",new EntryLine(0,0,0,0).Valid?1:0,0);
var vertical = new EntryTracker(); int verticalCount=0;
for(int i=0;i<entry.Length;i++) verticalCount+=vertical.Update([Person(1-entry[i],.5)],new EntryLine(.5,.2,.5,.8),i*.22);
Check("vertical line entry",verticalCount,1);
var mixed = new EntryTracker();int mixedCount=0;
for(int i=0;i<entry.Length;i++)
{
    PersonBox[] boxes=[Person(.35,entry[i]),Person(.65,entry[^(i+1)])];
    if(i%2==0) Array.Reverse(boxes);
    mixedCount+=mixed.Update(boxes,line,i*.22);
}
Check("entry plus exit and shuffled detections",mixedCount,1);
var jitterAfter = new EntryTracker();int jitterCount=0;double jt=0;
foreach(double y in entry.Concat(new double[]{.51,.49,.51,.49,.52,.53,.51,.50,.52,.53}))
{jitterCount+=jitterAfter.Update([Person(.5,y)],line,jt);jt+=.22;}
Check("jitter after entry never recounts",jitterCount,1);
Check("fast passage with one clear sample per side",Walk([.46,.54]),1);
Check("fast exit excluded",Walk([.54,.46]),0);
Check("brief flicker just beyond the dead band",Walk([.48,.52,.48]),0);
var intermittent = new EntryTracker();
intermittent.Update([Person(.5,.46)],line,0);
Check("track survives missing motion on crossing",intermittent.Update([Person(.5,.54) with { HasMotion=false }],line,.22),1);
var still = new EntryTracker();
still.Update([Person(.5,.46) with { HasMotion=false }],line,0);
Check("static detection cannot start an entry",still.Update([Person(.5,.54) with { HasMotion=false }],line,.22),0);
// Approximate the short strip between the entrance and the display counter.
var shopDoor = new EntryLine(.32,.39,.64,.355);
double doorCenterY = (.39 + .355) / 2;
Check("threshold crossing at the shop",Walk([doorCenterY-.04,doorCenterY+.037],shopDoor,x:.48),1);
Check("detection jumps from the entrance to the counter",
    Walk([doorCenterY-.04,doorCenterY+.06,doorCenterY+.072],shopDoor,x:.48),0);
Check("movement already inside by the counter",
    Walk([doorCenterY+.12,doorCenterY+.08,doorCenterY+.036,doorCenterY+.038],shopDoor,x:.48),0);
Check("movement to the side of the doorway",
    Walk([doorCenterY-.04,doorCenterY+.037],shopDoor,x:.75),0);
Console.WriteLine($"{checks} tracking checks passed.");
