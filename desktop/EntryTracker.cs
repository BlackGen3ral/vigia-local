namespace Vigia;

// Coordinates are normalized to the image; IDs exist only while a person is tracked.
internal sealed record EntryLine(double X1, double Y1, double X2, double Y2, bool Reverse = false)
{
    public double Length => double.Hypot(X2 - X1, Y2 - Y1);
    public double InsideDepth => Math.Clamp(Length * .14, .035, .085);
    private double OutsideDepth => Math.Clamp(Length * .50, .07, .18);

    public bool Valid => new[] { X1, Y1, X2, Y2 }.All(v => double.IsFinite(v) && v >= 0 && v <= 1)
        && Length >= .05;
    public double Distance(double x, double y) => ((X2-X1)*(y-Y1)-(Y2-Y1)*(x-X1))
        / Length * (Reverse ? -1 : 1);

    public bool IsNearDoor(double x, double y)
    {
        double distance = Distance(x, y);
        return distance >= -OutsideDepth && distance <= InsideDepth && IsBetweenEnds(x, y);
    }

    private bool IsBetweenEnds(double x, double y)
    {
        double projection = (x-X1)*(X2-X1) + (y-Y1)*(Y2-Y1);
        double lengthSquared = (X2-X1)*(X2-X1) + (Y2-Y1)*(Y2-Y1);
        return projection >= 0 && projection <= lengthSquared;
    }

    public bool Crosses(double ax, double ay, double bx, double by)
    {
        double a = Distance(ax, ay), b = Distance(bx, by);
        if (a * b >= 0) return false;
        double t = a / (a - b), x = ax + (bx-ax)*t, y = ay + (by-ay)*t;
        return IsBetweenEnds(x, y);
    }
}

internal readonly record struct PersonBox(double X, double Y, double Width, double Height, bool HasMotion = true)
{
    public double FootX => X + Width / 2;
    public double FootY => Y + Height * .94;
}

internal sealed class EntryTracker
{
    private sealed class Track(int id, PersonBox box, double now)
    {
        public int Id = id, Hits, Side, CandidateSide, SideHits, OutsideSamples;
        public PersonBox Box = box;
        public double Seen = now, Vx, Vy, StableX, StableY, OutsideSince, LastOutside;
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
        if (line?.Valid == true)
            detections = detections.Where(box => line.IsNearDoor(box.FootX, box.FootY)).ToArray();
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
            if (dt > .8) { t.Side = t.CandidateSide = t.SideHits = t.OutsideSamples = 0; }
            t.Vx = .5*t.Vx + .5*(box.FootX-t.Box.FootX)/dt;
            t.Vy = .5*t.Vy + .5*(box.FootY-t.Box.FootY)/dt;
            t.Box = box; t.Seen = now;
            matched.Add(t);
        }
        for (int i = 0; i < detections.Count; i++)
            if (!assignedDetections.Contains(i) && detections[i].HasMotion)
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
            // At a narrow doorway, the counter can hide the second inside sample.
            // Accept one inside sample only after a recent, repeated outside approach.
            bool confirmedApproach = side == 1 && t.Side == -1 && t.OutsideSamples >= 2
                && distance >= .020 && now - t.LastOutside <= .55;
            if (t.SideHits < 2 && Math.Abs(distance) < .03 && !confirmedApproach) continue;
            if (side == -1)
            {
                if (t.Side != -1) t.OutsideSince = now;
                if (now - t.OutsideSince >= 1) t.Counted = false;
                t.OutsideSamples = Math.Min(2, t.OutsideSamples + 1);
                t.LastOutside = now;
            }
            if (t.Side == -1 && side == 1 && !t.Counted
                && line.Crosses(t.StableX, t.StableY, x, y))
            { entries++; t.Counted = true; }
            if (side == 1) t.OutsideSamples = 0;
            t.Side = side; t.StableX = x; t.StableY = y;
        }
        Visible = matched.Where(t => t.Hits >= 2).Select(t => (t.Id, t.Box)).ToArray();
        return entries;
    }
}
