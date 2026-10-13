using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class DimensionInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Dimension;

    public string Name => "Dimension Measurement";

    public bool RequiresReference => false;

    public InspectionResult Inspect(
        Mat inspectionImage,
        Mat? referenceImage,
        InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");

        ArgumentNullException.ThrowIfNull(settings);

        if (settings.MinObjectArea < 0 ||
            settings.MinWidth < 0 || settings.MinHeight < 0 ||
            settings.MmPerPixel <= 0 ||
            settings.MaxWidth < settings.MinWidth ||
            settings.MaxHeight < settings.MinHeight ||
            settings.MaxAngle < settings.MinAngle)
        {
            throw new ArgumentException(
                "치수 검사 설정값을 확인하세요.");
        }

        var stopwatch = Stopwatch.StartNew();

        using var original = NormalizeToBgr(inspectionImage);
        using var gray = new Mat();
        using var blurred = new Mat();
        using var binary = new Mat();
        using var cleaned = new Mat();
        using var resultImage = original.Clone();

        // 1. Grayscale
        Cv2.CvtColor(
            original,
            gray,
            ColorConversionCodes.BGR2GRAY);

        // 2. Gaussian Blur
        Cv2.GaussianBlur(
            gray,
            blurred,
            new Size(5, 5),
            0);

        // 3. Otsu Threshold
        ThresholdTypes thresholdType =
            settings.InvertBinary
                ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                : ThresholdTypes.Binary | ThresholdTypes.Otsu;

        Cv2.Threshold(
            blurred,
            binary,
            0,
            255,
            thresholdType);

        // 4. Morphology
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(3, 3));

        Cv2.MorphologyEx(
            binary,
            cleaned,
            MorphTypes.Open,
            kernel);

        // 5. Contour 추출
        Cv2.FindContours(
            cleaned,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var defects = new List<DefectRegion>();
        int inspectedObjects = 0;
        var failureDetails = new List<string>();

        foreach (Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);

            if (area < settings.MinObjectArea)
                continue;

            // 6. 회전된 최소 외접 사각형
            RotatedRect rotatedRect = Cv2.MinAreaRect(contour);

            Point2f[] vertices = rotatedRect.Points();

            double sideA = rotatedRect.Size.Width;
            double sideB = rotatedRect.Size.Height;

            // 긴 변 = Width, 짧은 변 = Height
            double widthPx = Math.Max(sideA, sideB);
            double heightPx = Math.Min(sideA, sideB);

            // 긴 변의 실제 방향을 이용하여 각도 계산
            Point2f start;
            Point2f end;

            double edge01 = Distance(vertices[0], vertices[1]);
            double edge12 = Distance(vertices[1], vertices[2]);

            if (edge01 >= edge12)
            {
                start = vertices[0];
                end = vertices[1];
            }
            else
            {
                start = vertices[1];
                end = vertices[2];
            }

            double angle = Math.Atan2(
                end.Y - start.Y,
                end.X - start.X) * 180.0 / Math.PI;

            // 직선 방향은 180도 주기이므로 [-90, 90)로 정규화
            while (angle < -90)
                angle += 180;

            while (angle >= 90)
                angle -= 180;

            // 7. 단위 변환
            double width = widthPx * settings.MmPerPixel;
            double height = heightPx * settings.MmPerPixel;

            inspectedObjects++;

            // 8. 규격 검사
            bool invalidWidth =
                width < settings.MinWidth ||
                width > settings.MaxWidth;

            bool invalidHeight =
                height < settings.MinHeight ||
                height > settings.MaxHeight;

            bool invalidAngle =
                angle < settings.MinAngle ||
                angle > settings.MaxAngle;

            bool isDefective =
                invalidWidth ||
                invalidHeight ||
                invalidAngle;

            if (isDefective)
            {
                var reasons = new List<string>();
                if (invalidWidth) reasons.Add($"W={width:F2}");
                if (invalidHeight) reasons.Add($"H={height:F2}");
                if (invalidAngle) reasons.Add($"A={angle:F1}°");
                failureDetails.Add($"#{inspectedObjects}: {string.Join(", ", reasons)}");
            }

            Scalar color = isDefective
                ? new Scalar(0, 0, 255)
                : new Scalar(0, 255, 0);

            // 9. 회전 사각형 표시
            Point[] points = Array.ConvertAll(
                vertices,
                p => new Point(
                    (int)Math.Round(p.X),
                    (int)Math.Round(p.Y)));

            for (int i = 0; i < 4; i++)
            {
                Cv2.Line(
                    resultImage,
                    points[i],
                    points[(i + 1) % 4],
                    color,
                    2);
            }

            Rect bounds = Cv2.BoundingRect(contour);

            string label =
                $"{(isDefective ? "NG" : "OK")} " +
                $"W:{width:F1} H:{height:F1} A:{angle:F1}";

            Cv2.PutText(
                resultImage,
                label,
                new Point(
                    Math.Max(0, bounds.X),
                    Math.Max(18, bounds.Y - 8)),
                HersheyFonts.HersheySimplex,
                0.45,
                color,
                1);

            if (!isDefective)
                continue;

            defects.Add(new DefectRegion
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                Area = area,

                // 이 모드의 영역 Score는 측정 각도
                Score = angle
            });
        }

        stopwatch.Stop();

        bool hasObjects = inspectedObjects > 0;

        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = !hasObjects || defects.Count > 0,
            ResultStatus = !hasObjects ? InspectionStatus.ERROR :
                defects.Count > 0 ? InspectionStatus.NG : InspectionStatus.OK,

            ProcessingTimeMs =
                stopwatch.Elapsed.TotalMilliseconds,

            // 전체 객체 중 규격 이탈 비율 (%)
            Score = hasObjects
                ? defects.Count * 100.0 / inspectedObjects
                : 0,

            Message = !hasObjects
                ? "측정 가능한 객체가 없습니다. 판정 불가."
                : $"측정 객체: {inspectedObjects}개, 규격 이탈: {defects.Count}개" +
                  (failureDetails.Count > 0 ? $" | {string.Join("; ", failureDetails.GetRange(0, Math.Min(5, failureDetails.Count)))}" : ""),

            OriginalImage =
                OpenCvImageEncoder.Encode(original),

            ResultImage =
                OpenCvImageEncoder.Encode(resultImage),

            ProcessedImage =
                OpenCvImageEncoder.Encode(cleaned),

            Defects = defects
        };
    }

    private static double Distance(Point2f a, Point2f b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Mat NormalizeToBgr(Mat source)
    {
        var result = new Mat();

        switch (source.Channels())
        {
            case 1:
                Cv2.CvtColor(
                    source,
                    result,
                    ColorConversionCodes.GRAY2BGR);
                break;

            case 3:
                source.CopyTo(result);
                break;

            case 4:
                Cv2.CvtColor(
                    source,
                    result,
                    ColorConversionCodes.BGRA2BGR);
                break;

            default:
                result.Dispose();

                throw new NotSupportedException(
                    "지원하지 않는 이미지 채널입니다.");
        }

        return result;
    }
}