using System.Diagnostics;
using System.Net;
using OpenCvSharp;

namespace Vigia;

internal sealed record DoorZone(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

internal sealed record CameraFrame(byte[] Jpeg, int People, bool InDoor, bool Calibrating, int EventCount, DateTimeOffset? LastEvent);

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

    public static void Run(string endpoint, Func<DoorZone?> readZone, Action<CameraFrame> publish, CancellationToken token)
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
        TimeSpan? lastOccupied = null;
        int consecutiveOccupied = 0;
        int eventCount = 0;
        bool armed = true;
        DateTimeOffset? lastEvent = null;

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
                consecutiveOccupied = 0;
                armed = true;
            }

            bool analyzed = now >= nextDetection;
            if (analyzed)
            {
                nextDetection = now + TimeSpan.FromMilliseconds(220);
                motion.Update(preview);
                people = motion.Calibrating ? [] : detector.Find(preview).Where(motion.Overlaps).ToArray();
            }

            bool inDoor = false;
            foreach (var person in people)
            {
                var foot = new Point(person.X + person.Width / 2, person.Y + (int)(person.Height * 0.94));
                bool inside = activeZone?.Contains(foot.X / (double)width, foot.Y / (double)height) == true;
                inDoor |= inside;
                var color = inside ? new Scalar(85, 240, 120) : new Scalar(232, 201, 123);
                Cv2.Rectangle(preview, person, color, 2);
                Cv2.Circle(preview, foot, 4, color, -1);
            }
            if (analyzed && activeZone is not null)
            {
                if (inDoor)
                {
                    lastOccupied = now;
                    consecutiveOccupied = Math.Min(2, consecutiveOccupied + 1);
                    if (armed && consecutiveOccupied == 2)
                    {
                        armed = false;
                        eventCount++;
                        lastEvent = DateTimeOffset.Now;
                    }
                }
                else
                {
                    consecutiveOccupied = 0;
                    if (lastOccupied is TimeSpan seen && now - seen >= TimeSpan.FromSeconds(1.2))
                        armed = true;
                }
            }

            if (Cv2.ImEncode(".jpg", preview, out byte[] jpeg))
                publish(new CameraFrame(jpeg, people.Length, inDoor, motion.Calibrating, eventCount, lastEvent));
        }
    }
}
