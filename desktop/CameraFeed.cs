using System.Diagnostics;
using System.Net;
using OpenCvSharp;

namespace Vigia;

internal sealed record DoorZone(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

internal sealed record CameraFrame(byte[] Jpeg, int People, bool InDoor, bool Calibrating);
internal sealed record CameraArrival(string Id, DateTimeOffset Time, byte[]? Thumbnail);

internal static class CameraFeed
{
    // OpenCV CAP_PROP_* IDs. The managed enum does not expose these two options.
    private const int OpenTimeout = 53;
    private const int ReadTimeout = 54;

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

    public static void Run(string endpoint, Func<EntryLine?> readLine, Func<bool> includeThumbnail,
        Action<CameraFrame> publish, Action<CameraArrival> arrival, CancellationToken token)
    {
        using var capture = new VideoCapture(endpoint, VideoCaptureAPIs.FFMPEG, [OpenTimeout, 5000, ReadTimeout, 4000]);
        if (!capture.IsOpened()) return;
        using var detector = new PersonDetector();
        using var motion = new CameraMotion();
        using var raw = new Mat();
        using var preview = new Mat();
        var tracker = new EntryTracker();
        var clock = Stopwatch.StartNew();
        var nextFrame = TimeSpan.Zero;
        var nextDetection = TimeSpan.Zero;
        while (!token.IsCancellationRequested && capture.Read(raw) && !raw.Empty())
        {
            var now = clock.Elapsed;
            if (now < nextFrame) continue;
            nextFrame = now + TimeSpan.FromMilliseconds(125);
            int width = Math.Min(640, raw.Width);
            int height = Math.Max(1, (int)Math.Round(raw.Height * (double)width / raw.Width));
            Cv2.Resize(raw, preview, new Size(width, height), interpolation: InterpolationFlags.Area);
            var line = readLine();
            if (now >= nextDetection)
            {
                nextDetection = now + TimeSpan.FromMilliseconds(220);
                motion.Update(preview);
                var people = motion.Calibrating ? [] : detector.Find(preview);
                // Motion starts a track. Matching can continue through a frame without motion.
                var boxes = people.Select(p => new PersonBox(p.X/(double)width, p.Y/(double)height,
                    p.Width/(double)width, p.Height/(double)height, motion.Overlaps(p))).ToArray();
                int entries = tracker.Update(boxes, line, now.TotalSeconds);
                for (int i = 0; i < entries; i++)
                    arrival(new CameraArrival(Guid.NewGuid().ToString(), DateTimeOffset.Now,
                        includeThumbnail() ? MakeThumbnail(raw) : null));
            }
            foreach (var (id, box) in tracker.Visible)
            {
                var color = new Scalar(85, 240, 120);
                Cv2.Rectangle(preview, new Rect((int)(box.X*width), (int)(box.Y*height),
                    Math.Max(1,(int)(box.Width*width)), Math.Max(1,(int)(box.Height*height))), color, 2);
                Cv2.PutText(preview, "#" + id, new Point((int)(box.X*width), Math.Max(14,(int)(box.Y*height)-5)),
                    HersheyFonts.HersheySimplex, .5, color, 1);
                Cv2.Circle(preview, new Point((int)(box.FootX*width), (int)(box.FootY*height)), 4, color, -1);
            }
            if (Cv2.ImEncode(".jpg", preview, out byte[] jpeg))
                publish(new CameraFrame(jpeg, tracker.Visible.Count, false, motion.Calibrating));
        }
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
