using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class ColorInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Color;

    public string Name => "Color Inspection";

    public bool RequiresReference => false;

    public InspectionResult Inspect(
        Mat inspectionImage,
        Mat? referenceImage,
        InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");

        ArgumentNullException.ThrowIfNull(settings);

        ValidateSettings(settings);

        var stopwatch = Stopwatch.StartNew();

        using var original = NormalizeToBgr(inspectionImage);
        using var hsv = new Mat();
        using var mask = new Mat();
        using var cleanedMask = new Mat();
        using var resultImage = original.Clone();

        // 1. BGR → HSV
        Cv2.CvtColor(
            original,
            hsv,
            ColorConversionCodes.BGR2HSV);

        // 2. 지정한 색상 범위 추출
        // HueMin > HueMax이면 빨간색처럼 179→0을
        // 가로지르는 색상 범위로 처리한다.
        if (settings.HueMin <= settings.HueMax)
        {
            Cv2.InRange(
                hsv,
                new Scalar(
                    settings.HueMin,
                    settings.SaturationMin,
                    settings.ValueMin),
                new Scalar(
                    settings.HueMax,
                    settings.SaturationMax,
                    settings.ValueMax),
                mask);
        }
        else
        {
            using var lowerMask = new Mat();
            using var upperMask = new Mat();

            Cv2.InRange(
                hsv,
                new Scalar(
                    0,
                    settings.SaturationMin,
                    settings.ValueMin),
                new Scalar(
                    settings.HueMax,
                    settings.SaturationMax,
                    settings.ValueMax),
                lowerMask);

            Cv2.InRange(
                hsv,
                new Scalar(
                    settings.HueMin,
                    settings.SaturationMin,
                    settings.ValueMin),
                new Scalar(
                    179,
                    settings.SaturationMax,
                    settings.ValueMax),
                upperMask);

            Cv2.BitwiseOr(
                lowerMask,
                upperMask,
                mask);
        }

        // 3. 작은 노이즈 제거
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(3, 3));

        Cv2.MorphologyEx(
            mask,
            cleanedMask,
            MorphTypes.Open,
            kernel);

        // 4. 색상 영역 검출
        Cv2.FindContours(
            cleanedMask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var detectedRegions = new List<DefectRegion>();

        foreach (Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);

            if (area < settings.MinColorArea)
                continue;

            Rect rect = Cv2.BoundingRect(contour);

            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            detectedRegions.Add(new DefectRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Area = area,
                Score = area
            });
        }

        // 큰 영역부터 순서대로 표시해 결과를 읽기 쉽게 한다.
        detectedRegions.Sort((a, b) => b.Area.CompareTo(a.Area));

        // 5. 실제 색상 픽셀 비율 계산
        double colorPixelRatio =
            Cv2.CountNonZero(cleanedMask) * 100.0
            / (cleanedMask.Rows * cleanedMask.Cols);

        bool isDefective;

        if (settings.ColorIsForbidden)
        {
            // 금지 색상이 일정 면적 이상 존재하면 NG
            isDefective = detectedRegions.Count > 0;
        }
        else
        {
            // 필수 색상이 필요한 비율보다 적으면 NG
            isDefective =
                colorPixelRatio < settings.MinRequiredColorRatio;
        }

        // 6. 결과 이미지 표시
        for (int i = 0; i < detectedRegions.Count; i++)
        {
            DefectRegion region = detectedRegions[i];
            Scalar color = settings.ColorIsForbidden
                ? new Scalar(0, 0, 255)
                : new Scalar(0, 255, 0);

            Cv2.Rectangle(
                resultImage,
                new Rect(
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height),
                color,
                2);

            string label = $"#{i + 1} A:{region.Area:F0}";
            int labelY = Math.Max(16, region.Y - 6);
            Cv2.PutText(
                resultImage,
                label,
                new Point(Math.Max(0, region.X), labelY),
                HersheyFonts.HersheySimplex,
                0.45,
                color,
                1);
        }

        string statusText = isDefective ? "NG" : "OK";

        Cv2.PutText(
            resultImage,
            $"{statusText} Color:{colorPixelRatio:F2}%",
            new Point(10, 30),
            HersheyFonts.HersheySimplex,
            0.7,
            isDefective
                ? new Scalar(0, 0, 255)
                : new Scalar(0, 255, 0),
            2);

        stopwatch.Stop();

        // Required Color 모드에서는 색상이 부족한 경우
        // 특정 위치를 결함으로 단정할 수 없다.
        // 따라서 Defects에는 위치가 확인된 색상 영역만 저장한다.
        IReadOnlyList<DefectRegion> defects =
            settings.ColorIsForbidden
                ? detectedRegions
                : Array.Empty<DefectRegion>();

        string message;

        if (settings.ColorIsForbidden)
        {
            message = isDefective
                ? $"금지 색상 영역 {detectedRegions.Count}개 검출 (최소 면적 {settings.MinColorArea:F0}px²), 색상 비율 {colorPixelRatio:F2}%"
                : $"금지 색상 영역 없음 (최소 면적 {settings.MinColorArea:F0}px²), 색상 비율 {colorPixelRatio:F2}%";
        }
        else
        {
            message =
                $"필수 색상 비율: {colorPixelRatio:F2}% / " +
                $"기준: {settings.MinRequiredColorRatio:F2}% / " +
                $"검출 영역: {detectedRegions.Count}개";
        }

        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = isDefective,
            ResultStatus = isDefective ? InspectionStatus.NG : InspectionStatus.OK,

            ProcessingTimeMs =
                stopwatch.Elapsed.TotalMilliseconds,

            // 전체 이미지에서 지정 색상이 차지하는 비율 (%)
            Score = colorPixelRatio,

            Message = message,

            OriginalImage =
                OpenCvImageEncoder.Encode(original),

            ResultImage =
                OpenCvImageEncoder.Encode(resultImage),

            ProcessedImage =
                OpenCvImageEncoder.Encode(cleanedMask),

            Defects = defects
        };
    }

    private static void ValidateSettings(
        InspectionSettings settings)
    {
        if (settings.HueMin < 0 || settings.HueMin > 179 ||
            settings.HueMax < 0 || settings.HueMax > 179)
        {
            throw new ArgumentException(
                "Hue 범위는 0~179여야 합니다.");
        }

        if (settings.SaturationMin < 0 ||
            settings.SaturationMin > 255 ||
            settings.SaturationMax < 0 ||
            settings.SaturationMax > 255 ||
            settings.SaturationMin > settings.SaturationMax)
        {
            throw new ArgumentException(
                "Saturation 범위를 확인하세요.");
        }

        if (settings.ValueMin < 0 ||
            settings.ValueMin > 255 ||
            settings.ValueMax < 0 ||
            settings.ValueMax > 255 ||
            settings.ValueMin > settings.ValueMax)
        {
            throw new ArgumentException(
                "Value 범위를 확인하세요.");
        }

        if (settings.MinColorArea < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.MinColorArea));
        }

        if (settings.MinRequiredColorRatio < 0 ||
            settings.MinRequiredColorRatio > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.MinRequiredColorRatio));
        }
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