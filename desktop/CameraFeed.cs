using System.Diagnostics;
using System.Net;
using OpenCvSharp;

namespace Vigia;

internal sealed record DoorZone(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

internal sealed record CameraFrame(byte[] Jpeg, int People, bool InDoor, bool Calibrating, int EventCount,
    DateTimeOffset? LastEvent, byte[]? Thumbnail);

internal static class CameraFeed
{
    // OpenCV CAP_PROP_* IDs. The managed enum does not expose these two options.
    private const int OpenTimeout = 53;
    private const int ReadTimeout = 54;
    private static readonly TimeSpan ClearBeforeRearm = TimeSpan.FromSeconds(8);

    public static string? LocalEndpoint(string host, string port, string channel, string login, string password)
    {
        if (!IPAddress.TryParse(host.Trim(), out var address) ||
            !IsPrivateIpv4(address.GetAddressBytes()) ||
            !int.TryParse(port, out int portNumber) || portNumber is < 1 or > 65535 ||
            !int.TryParse(channel, out int channelNumber) || channelNumber is < 1 or > 64 ||
            string.IsNullOrWhiteSpace(login) || password.Length == 0)
            return null;

        return $"rtsp://{Uri.EscapeDataString(login.Trim())}:{Uri.EscapeDataString(password)}@{address}:{portNumber}/cam/realmonitor?channel={channelNumber}&subtype=0";
    }

    private static bool IsPrivateIpv4(byte[] ip) => ip.Length == 4 &&
        (ip[0] == 10 || ip[0] == 172 && ip[1] is >= 16 and <= 31 || ip[0] == 192 && ip[1] == 168);

    public static void Run(string endpoint, Func<DoorZone?> readZone, Func<bool> includeThumbnail,
        Action<CameraFrame> publish, CancellationToken token)
    {
        using var capture = new VideoCapture(endpoint, VideoCaptureAPIs.FFMPEG, [OpenTimeout, 5000, ReadTimeout, 4000]);
        if (!capture.IsOpened()) return;

        using var detector = new PersonDetector();
        using var motion = new CameraMotion();
        using var raw = new Mat();
        using var preview = new Mat();
        var clock = Stopwatch.StartNew();
        var nextFrame = TimeSpan.Zero;
        var nextDetection = TimeSpan.Zero;
        DoorZone? activeZone = null;
        Rect[] people = [];
        Rect[] visiblePeople = [];
        Rect? trackedOccupant = null;
        TimeSpan? lastOccupied = null;
        int consecutiveOccupied = 0;
        int eventCount = 0;
        bool armed = true;
        DateTimeOffset? lastEvent = null;
        byte[]? thumbnail = null;
        bool areaOccupied = false;

        while (!token.IsCancellationRequested && capture.Read(raw) && !raw.Empty())
        {
            var now = clock.Elapsed;
            if (now < nextFrame) continue;
            nextFrame = now + TimeSpan.FromMilliseconds(125);
            int width = Math.Min(640, raw.Width);
            int height = Math.Max(1, (int)Math.Round(raw.Height * (double)width / raw.Width));
            Cv2.Resize(raw, preview, new Size(width, height), interpolation: InterpolationFlags.Area);
            var selected = readZone();
            if (!ReferenceEquals(activeZone, selected))
            {
                activeZone = selected;
                lastOccupied = null;
                trackedOccupant = null;
                areaOccupied = false;
                consecutiveOccupied = 0;
                armed = true;
            }

            bool analyzed = now >= nextDetection;
            if (analyzed)
            {
                nextDetection = now + TimeSpan.FromMilliseconds(220);
                motion.Update(preview);
                visiblePeople = motion.Calibrating ? [] : detector.Find(preview);
                people = visiblePeople.Where(motion.Overlaps).ToArray();
            }

            foreach (var person in people)
            {
                var foot = new Point(person.X + person.Width / 2, person.Y + (int)(person.Height * 0.94));
                bool inside = activeZone?.Contains(foot.X / (double)width, foot.Y / (double)height) == true;
                var color = inside ? new Scalar(85, 240, 120) : new Scalar(232, 201, 123);
                Cv2.Rectangle(preview, person, color, 2);
                Cv2.Circle(preview, foot, 4, color, -1);
            }
            if (analyzed && activeZone is not null)
            {
                Rect? occupant = people.Where(person => FootInside(person, activeZone, width, height))
                    .Select(person => (Rect?)person).FirstOrDefault();
                if (!armed && trackedOccupant is Rect previous)
                {
                    occupant ??= visiblePeople.Where(person => FootInside(person, activeZone, width, height)
                            && SamePosition(previous, person))
                        .Select(person => (Rect?)person).FirstOrDefault();
                }
                areaOccupied = occupant.HasValue;
                if (areaOccupied)
                {
                    trackedOccupant = occupant;
                    lastOccupied = now;
                    consecutiveOccupied = Math.Min(2, consecutiveOccupied + 1);
                    if (armed && consecutiveOccupied == 2)
                    {
                        armed = false;
                        eventCount++;
                        lastEvent = DateTimeOffset.Now;
                        thumbnail = includeThumbnail() ? MakeThumbnail(raw) : null;
                    }
                }
                else
                {
                    consecutiveOccupied = 0;
                    if (lastOccupied is TimeSpan seen && now - seen >= ClearBeforeRearm)
                    {
                        armed = true;
                        trackedOccupant = null;
                    }
                }
            }

            if (Cv2.ImEncode(".jpg", preview, out byte[] jpeg))
                publish(new CameraFrame(jpeg, people.Length, areaOccupied, motion.Calibrating, eventCount, lastEvent, thumbnail));
        }
    }

    private static bool FootInside(Rect person, DoorZone zone, int width, int height) =>
        zone.Contains((person.X + person.Width / 2.0) / width, (person.Y + person.Height * .94) / height);

    private static bool SamePosition(Rect previous, Rect current)
    {
        var overlap = previous & current;
        double intersection = (double)overlap.Width * overlap.Height;
        double union = (double)previous.Width * previous.Height + (double)current.Width * current.Height - intersection;
        return union > 0 && intersection / union >= .30;
    }

    private static byte[]? MakeThumbnail(Mat raw)
    {
        double scale = Math.Min(1, 320.0 / Math.Max(raw.Width, raw.Height));
        using var small = new Mat();
        Cv2.Resize(raw, small, new Size(Math.Max(1, (int)(raw.Width * scale)),
            Math.Max(1, (int)(raw.Height * scale))), interpolation: InterpolationFlags.Area);
        foreach (int quality in new[] { 65, 45, 25 })
            if (Cv2.ImEncode(".jpg", small, out byte[] bytes, [(int)ImwriteFlags.JpegQuality, quality]) && bytes.Length <= 24 * 1024)
                return bytes;
        return null;
    }
}
