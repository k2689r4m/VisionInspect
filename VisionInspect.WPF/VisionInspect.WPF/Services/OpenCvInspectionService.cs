using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services;

public sealed class OpenCvRegion
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Area { get; init; }
}

public sealed class OpenCvInspectionResult
{
    public required byte[] Original { get; init; }
    public required byte[] Grayscale { get; init; }
    public required byte[] Binary { get; init; }
    public required byte[] Edges { get; init; }
    public required byte[] Contours { get; init; }

    public required IReadOnlyList<OpenCvRegion> Regions { get; init; }

    public double ProcessingTimeMs { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}



public sealed class OpenCvInspectionService
{
    private static byte[] EncodeImage(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] buffer);
        return buffer;
    }

    public OpenCvInspectionResult Inspect(
        string imagePath,
        double minContourArea = 50)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("이미지 경로가 비어 있습니다.");

        if (minContourArea < 0)
            throw new ArgumentOutOfRangeException(nameof(minContourArea));

        var stopwatch = Stopwatch.StartNew();

        using var original = Cv2.ImRead(
            imagePath,
            ImreadModes.Color);

        if (original.Empty())
            throw new InvalidOperationException(
                "이미지를 불러오지 못했습니다.");

        using var grayscale = new Mat();
        using var blurred = new Mat();
        using var binary = new Mat();
        using var edges = new Mat();
        using var contourImage = original.Clone();

        // 1. Grayscale
        Cv2.CvtColor(
            original,
            grayscale,
            ColorConversionCodes.BGR2GRAY);

        // 2. Noise reduction
        Cv2.GaussianBlur(
            grayscale,
            blurred,
            new Size(5, 5),
            0);

        // 3. Otsu threshold
        Cv2.Threshold(
            blurred,
            binary,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // 4. Canny edge detection
        Cv2.Canny(
            blurred,
            edges,
            50,
            150);

        // 5. Contour detection
        Cv2.FindContours(
            edges,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var regions = new List<OpenCvRegion>();

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);

            if (area < minContourArea)
                continue;

            Rect rect = Cv2.BoundingRect(contour);

            regions.Add(new OpenCvRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Area = area
            });

            Cv2.Rectangle(
                contourImage,
                rect,
                new Scalar(0, 255, 0),
                2);
        }

        stopwatch.Stop();

        return new OpenCvInspectionResult
        {
            Original = EncodeImage(original),
            Grayscale = EncodeImage(grayscale),
            Binary = EncodeImage(binary),
            Edges = EncodeImage(edges),
            Contours = EncodeImage(contourImage),

            Regions = regions,

            ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,

            Width = original.Width,
            Height = original.Height
        };
    }
}