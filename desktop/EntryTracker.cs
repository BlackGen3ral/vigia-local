namespace Vigia;

// Coordinates are normalized to the image; IDs exist only while a person is tracked.
internal sealed record EntryLine(double X1, double Y1, double X2, double Y2, bool Reverse = false)
{
    public bool Valid => new[] { X1, Y1, X2, Y2 }.All(v => double.IsFinite(v) && v >= 0 && v <= 1)
        && double.Hypot(X2 - X1, Y2 - Y1) >= .05;
    public double Distance(double x, double y) => ((X2-X1)*(y-Y1)-(Y2-Y1)*(x-X1))
        / double.Hypot(X2-X1, Y2-Y1) * (Reverse ? -1 : 1);
    public bool Crosses(double ax, double ay, double bx, double by)
    {
        double a = Distance(ax, ay), b = Distance(bx, by);
        if (a * b >= 0) return false;
        double t = a / (a - b), x = ax + (bx-ax)*t, y = ay + (by-ay)*t;
        double projection = ((x-X1)*(X2-X1)+(y-Y1)*(Y2-Y1)) /
            ((X2-X1)*(X2-X1)+(Y2-Y1)*(Y2-Y1));
        return projection >= 0 && projection <= 1;
    }
}

internal readonly record struct PersonBox(double X, double Y, double Width, double Height)
{
    public double FootX => X + Width / 2;
    public double FootY => Y + Height * .94;
}

internal sealed class EntryTracker
{
    private sealed class Track(int id, PersonBox box, double now)
    {
        public int Id = id, Hits, Side, CandidateSide, SideHits;
        public PersonBox Box = box;
        public double Seen = now, Vx, Vy, StableX, StableY, OutsideSince;
        public bool Counted;
    }
    private readonly List<Track> tracks = [];
    private EntryLine? activeLine;
    private int nextId;
    public IReadOnlyList<(int Id, PersonBox Box)> Visible { get; private set; } = [];

    public int Update(IReadOnlyList<PersonBox> detections, EntryLine? line, double now)
    {
        if (line != activeLine) { tracks.Clear(); activeLine = line; }
        tracks.RemoveAll(t => now - t.Seen > 1.5);
        var candidates = new List<(double Cost, Track Track, int Detection)>();
        foreach (var track in tracks)
        for (int i = 0; i < detections.Count; i++)
        {
            var box = detections[i];
            double dt = Math.Min(now-track.Seen, .7);
            double distance = double.Hypot(box.FootX-track.Box.FootX-track.Vx*dt,
                box.FootY-track.Box.FootY-track.Vy*dt);
            double scale = Math.Max(.06, Math.Min(.22, Math.Max(box.Height, track.Box.Height)*.65));
            double ratio = box.Height / Math.Max(.001, track.Box.Height);
            if (distance <= scale && ratio is > .5 and < 2)
                candidates.Add((distance / scale, track, i));
        }
        var assignedTracks = new HashSet<int>();
        var assignedDetections = new HashSet<int>();
        var matched = new List<Track>();
        foreach (var pair in candidates.OrderBy(c => c.Cost))
        {
            if (assignedTracks.Contains(pair.Track.Id) || assignedDetections.Contains(pair.Detection)) continue;
            assignedTracks.Add(pair.Track.Id); assignedDetections.Add(pair.Detection);
            var t = pair.Track; var box = detections[pair.Detection];
            double dt = Math.Max(.01, now-t.Seen);
            // Never infer a crossing through an extended occlusion.
            if (dt > .8) { t.Side = t.CandidateSide = t.SideHits = 0; }
            t.Vx = .5*t.Vx + .5*(box.FootX-t.Box.FootX)/dt;
            t.Vy = .5*t.Vy + .5*(box.FootY-t.Box.FootY)/dt;
            t.Box = box; t.Seen = now;
            matched.Add(t);
        }
        for (int i = 0; i < detections.Count; i++)
            if (!assignedDetections.Contains(i))
            {
                var t = new Track(++nextId, detections[i], now);
                tracks.Add(t); matched.Add(t);
            }
        int entries = 0;
        foreach (var t in matched)
        {
            t.Hits++;
            if (line?.Valid != true) continue;
            double x = t.Box.FootX, y = t.Box.FootY, distance = line.Distance(x, y);
            int side = distance < -.015 ? -1 : distance > .015 ? 1 : 0;
            if (side == 0) { t.CandidateSide = t.SideHits = 0; continue; }
            if (t.CandidateSide != side) { t.CandidateSide = side; t.SideHits = 0; }
            t.SideHits++;
            if (t.SideHits < 2) continue;
            if (side == -1 && t.Side != -1) t.OutsideSince = now;
            if (side == -1 && now-t.OutsideSince >= 1) t.Counted = false;
            if (t.Side == -1 && side == 1 && !t.Counted && t.Hits >= 4
                && line.Crosses(t.StableX, t.StableY, x, y))
            { entries++; t.Counted = true; }
            t.Side = side; t.StableX = x; t.StableY = y;
        }
        Visible = matched.Where(t => t.Hits >= 2).Select(t => (t.Id, t.Box)).ToArray();
        return entries;
    }
}
