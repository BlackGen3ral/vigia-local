using OpenCvSharp;

namespace Vigia;

internal sealed class CameraMotion : IDisposable
{
    private readonly BackgroundSubtractorMOG2 background = BackgroundSubtractorMOG2.Create(100, 24, true);
    private readonly Mat gray = new();
    private readonly Mat mask = new();
    private int samples;
    private bool usefulMask;

    public bool Calibrating => samples < 8;

    public void Update(Mat frame)
    {
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
        background.Apply(gray, mask, Calibrating ? 0.30 : 0.04);
        if (Calibrating) samples++;
        // MOG2 marks shadows as 127 and moving foreground as 255.
        Cv2.Threshold(mask, mask, 200, 255, ThresholdTypes.Binary);
        usefulMask = !Calibrating && Cv2.CountNonZero(mask) < mask.Total() * 0.45;
    }

    public bool Overlaps(Rect person)
    {
        if (!usefulMask) return false;
        int sideInset = person.Width / 5;
        int topInset = person.Height / 10;
        var core = new Rect(person.X + sideInset, person.Y + topInset,
            person.Width - 2 * sideInset, person.Height - 2 * topInset);
        using var area = new Mat(mask, core);
        return Cv2.CountNonZero(area) >= Math.Max(12, area.Total() * 0.09);
    }

    public void Dispose()
    {
        background.Dispose();
        gray.Dispose();
        mask.Dispose();
    }
}
