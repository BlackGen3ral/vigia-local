using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace Vigia;

// YOLOX-S from OpenCV Zoo. The COCO person class is index zero.
internal sealed class PersonDetector : IDisposable
{
    private const int InputSize = 640;
    private const int OutputsPerCell = 85;
    private const float Confidence = 0.42f;
    private static readonly int[] Strides = [8, 16, 32];
    private readonly Net net;
    private readonly Mat input;

    public PersonDetector()
    {
        using var resource = typeof(PersonDetector).Assembly.GetManifestResourceStream("Vigia.Camera.YoloX.onnx")
            ?? throw new InvalidOperationException("Modelo de detecção ausente.");
        var model = new byte[checked((int)resource.Length)];
        resource.ReadExactly(model);
        net = Net.ReadNetFromONNX(model) ?? throw new InvalidOperationException("Modelo de detecção inválido.");
        input = new Mat(InputSize, InputSize, MatType.CV_8UC3);
    }

    public Rect[] Find(Mat frame)
    {
        double ratio = Math.Min(InputSize / (double)frame.Width, InputSize / (double)frame.Height);
        var size = new Size(Math.Max(1, (int)(frame.Width * ratio)), Math.Max(1, (int)(frame.Height * ratio)));
        input.SetTo(Scalar.All(114));
        using var imageArea = new Mat(input, new Rect(0, 0, size.Width, size.Height));
        Cv2.Resize(frame, imageArea, size);
        using var blob = CvDnn.BlobFromImage(input, 1, new Size(InputSize, InputSize), Scalar.All(0), swapRB: true);
        net.SetInput(blob);
        using var prediction = net.Forward();
        int expected = Strides.Sum(stride => (InputSize / stride) * (InputSize / stride)) * OutputsPerCell;
        if (prediction.Total() != expected || prediction.Type() != MatType.CV_32FC1)
            throw new InvalidOperationException("Formato de saída do detector inesperado.");
        using var continuous = prediction.Clone();
        var values = new float[expected];
        Marshal.Copy(continuous.Data, values, 0, values.Length);

        var boxes = new List<Rect>();
        var scores = new List<float>();
        int offset = 0;
        foreach (int stride in Strides)
        {
            int cells = InputSize / stride;
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++, offset += OutputsPerCell)
            {
                float personScore = values[offset + 5];
                float score = values[offset + 4] * personScore;
                if (!float.IsFinite(score) || score <= Confidence) continue;
                if (!PersonIsBestClass(values.AsSpan(offset + 6, OutputsPerCell - 6), personScore)) continue;
                double centerX = (values[offset] + x) * stride / ratio;
                double centerY = (values[offset + 1] + y) * stride / ratio;
                double halfWidth = Math.Exp(values[offset + 2]) * stride / (2 * ratio);
                double halfHeight = Math.Exp(values[offset + 3]) * stride / (2 * ratio);
                if (!double.IsFinite(centerX + centerY + halfWidth + halfHeight)) continue;
                int left = (int)Math.Clamp(centerX - halfWidth, 0, frame.Width - 1);
                int top = (int)Math.Clamp(centerY - halfHeight, 0, frame.Height - 1);
                int right = (int)Math.Clamp(centerX + halfWidth, 0, frame.Width - 1);
                int bottom = (int)Math.Clamp(centerY + halfHeight, 0, frame.Height - 1);
                if (right <= left || bottom <= top) continue;
                boxes.Add(new Rect(left, top, right - left, bottom - top));
                scores.Add(score);
            }
        }

        if (boxes.Count == 0) return [];
        CvDnn.NMSBoxes(boxes, scores, Confidence, 0.45f, out int[] keep);
        return keep.Select(index => boxes[index]).ToArray();
    }

    private static bool PersonIsBestClass(ReadOnlySpan<float> otherClasses, float personScore)
    {
        foreach (float other in otherClasses)
            if (other >= personScore) return false;
        return true;
    }

    public void Dispose()
    {
        net.Dispose();
        input.Dispose();
    }
}
