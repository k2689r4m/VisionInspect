using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class ShapeInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Shape;
    public string Name => "Shape Inspection";
    public bool RequiresReference => false;

    public InspectionResult Inspect(Mat inspectionImage, Mat? referenceImage, InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        var timer = Stopwatch.StartNew();
        using var original = NormalizeToBgr(inspectionImage);
        using var gray = new Mat();
        using var blurred = new Mat();
        using var binary = new Mat();
        using var cleaned = new Mat();
        using var resultImage = original.Clone();

        Cv2.CvtColor(original, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, blurred,
            new Size(settings.ShapeBlurSize, settings.ShapeBlurSize), 0);

        var thresholdMode = settings.InvertBinary
            ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
        double thresholdValue = settings.ShapeUseOtsu ? 0 : settings.Threshold;
        if (settings.ShapeUseOtsu) thresholdMode |= ThresholdTypes.Otsu;
        Cv2.Threshold(blurred, binary, thresholdValue, 255, thresholdMode);

        using (var kernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(settings.ShapeMorphSize, settings.ShapeMorphSize)))
        {
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            // 작은 구멍을 채워 형상/원형도 측정을 안정화한다.
            Cv2.MorphologyEx(cleaned, cleaned, MorphTypes.Close, kernel);
        }

        Cv2.FindContours(cleaned, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var defects = new List<DefectRegion>();
        int inspectedObjects = 0;
        int okObjects = 0;
        int skippedBorderObjects = 0;

        foreach (Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < settings.MinObjectArea) continue;

            Rect rect = Cv2.BoundingRect(contour);
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            if (settings.ShapeExcludeBorderObjects &&
                (rect.X <= settings.ShapeBorderMargin ||
                 rect.Y <= settings.ShapeBorderMargin ||
                 rect.Right >= original.Width - settings.ShapeBorderMargin ||
                 rect.Bottom >= original.Height - settings.ShapeBorderMargin))
            {
                skippedBorderObjects++;
                continue;
            }

            inspectedObjects++;
            double aspect = (double)rect.Width / rect.Height;
            double perimeter = Cv2.ArcLength(contour, true);
            double circularity = perimeter > 0
                ? 4.0 * Math.PI * area / (perimeter * perimeter)
                : 0.0;
            // 근사/래스터 오차로 1을 약간 넘는 경우를 표시용으로 제한한다.
            circularity = Math.Clamp(circularity, 0, 1);

            bool invalid =
                area < settings.MinContourArea || area > settings.MaxContourArea ||
                rect.Width < settings.MinWidth || rect.Width > settings.MaxWidth ||
                rect.Height < settings.MinHeight || rect.Height > settings.MaxHeight ||
                aspect < settings.MinAspectRatio || aspect > settings.MaxAspectRatio ||
                (settings.ShapeEnableCircularity &&
                 (circularity < settings.ShapeMinCircularity ||
                  circularity > settings.ShapeMaxCircularity));

            var color = invalid ? new Scalar(0, 0, 255) : new Scalar(0, 220, 0);
            Cv2.Rectangle(resultImage, rect, color, 2);
            Cv2.PutText(resultImage,
                $"{(invalid ? "NG" : "OK")} #{inspectedObjects} C:{circularity:F2}",
                new Point(rect.X, Math.Max(16, rect.Y - 5)),
                HersheyFonts.HersheySimplex, 0.45, color, 1);

            if (!invalid)
            {
                okObjects++;
                continue;
            }

            defects.Add(new DefectRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Area = area,
                Score = circularity
            });
        }

        // 예상 개수가 지정되었다면, 개수 불일치는 형상 규격 불량(NG)이다.
        // 단, 대상이 하나도 없는 경우는 기본적으로 검사 불능(ERROR)으로 처리한다.
        bool countMismatch = settings.ShapeExpectedObjectCount > 0 &&
                             inspectedObjects != settings.ShapeExpectedObjectCount;
        bool noObjects = inspectedObjects == 0;
        bool isDefective = !noObjects && (defects.Count > 0 || countMismatch);
        InspectionStatus status = noObjects ? InspectionStatus.ERROR :
            isDefective ? InspectionStatus.NG : InspectionStatus.OK;

        if (countMismatch && !noObjects)
        {
            Cv2.PutText(resultImage,
                $"COUNT NG: {inspectedObjects}/{settings.ShapeExpectedObjectCount}",
                new Point(12, 28), HersheyFonts.HersheySimplex,
                0.65, new Scalar(0, 0, 255), 2);
        }

        timer.Stop();
        string message = noObjects
            ? $"검사 가능한 객체가 없습니다. 이진화 반전/최소 면적을 확인하세요. (경계 제외 {skippedBorderObjects}개)"
            : $"검사 {inspectedObjects}개 / 정상 {okObjects}개 / 형상 NG {defects.Count}개" +
              (countMismatch ? $" / 개수 NG (기대 {settings.ShapeExpectedObjectCount}개)" : "") +
              (skippedBorderObjects > 0 ? $" / 경계 제외 {skippedBorderObjects}개" : "");

        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = isDefective,
            ResultStatus = status,
            ProcessingTimeMs = timer.Elapsed.TotalMilliseconds,
            // 점수는 검사 객체 중 형상 NG 비율(%). 개수 NG는 Message/Status로 표시.
            Score = inspectedObjects == 0 ? 0 : defects.Count * 100.0 / inspectedObjects,
            Message = message,
            OriginalImage = OpenCvImageEncoder.Encode(original),
            ResultImage = OpenCvImageEncoder.Encode(resultImage),
            ProcessedImage = OpenCvImageEncoder.Encode(cleaned),
            Defects = defects
        };
    }

    private static void Validate(InspectionSettings s)
    {
        if (s.MinObjectArea < 0 || s.MinContourArea < 0 ||
            s.MaxContourArea < s.MinContourArea ||
            s.MinWidth < 0 || s.MaxWidth < s.MinWidth ||
            s.MinHeight < 0 || s.MaxHeight < s.MinHeight ||
            s.MinAspectRatio < 0 || s.MaxAspectRatio < s.MinAspectRatio ||
            s.Threshold < 0 || s.Threshold > 255 ||
            s.ShapeExpectedObjectCount < 0 || s.ShapeBorderMargin < 0 ||
            s.ShapeMinCircularity < 0 || s.ShapeMaxCircularity > 1 ||
            s.ShapeMaxCircularity < s.ShapeMinCircularity ||
            !IsOddPositive(s.ShapeBlurSize) || !IsOddPositive(s.ShapeMorphSize))
            throw new ArgumentException("Shape 검사 설정값의 범위를 확인하세요.");
    }

    private static bool IsOddPositive(int value) => value >= 1 && value % 2 == 1;

    private static Mat NormalizeToBgr(Mat source)
    {
        var output = new Mat();
        switch (source.Channels())
        {
            case 1:
                Cv2.CvtColor(source, output, ColorConversionCodes.GRAY2BGR);
                break;
            case 3:
                source.CopyTo(output);
                break;
            case 4:
                Cv2.CvtColor(source, output, ColorConversionCodes.BGRA2BGR);
                break;
            default:
                output.Dispose();
                throw new NotSupportedException("지원하지 않는 이미지 채널입니다.");
        }
        return output;
    }
}
