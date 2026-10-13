using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class TemplateInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Template;
    public string Name => "Template Matching";
    public bool RequiresReference => true;

    private sealed record Candidate(Rect Rect, double Similarity, double Angle);

    public InspectionResult Inspect(Mat inspectionImage, Mat? referenceImage, InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");
        if (referenceImage is null || referenceImage.Empty())
            throw new ArgumentException("잘라낸 템플릿 이미지가 필요합니다.");
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.TemplateThreshold < 0 || settings.TemplateThreshold > 1 ||
            settings.PositionTolerance < 0 || settings.TemplateMaxRotation < 0 ||
            settings.TemplateMaxRotation > 45 || settings.TemplateRotationStep <= 0 ||
            settings.TemplateRotationStep > 45 || settings.TemplateMaxDetections < 1 ||
            settings.TemplateMaxDetections > 100 || settings.TemplateExpectedCount < 1 ||
            settings.TemplateNmsIoU < 0 || settings.TemplateNmsIoU >= 1)
            throw new ArgumentException("Template 검사 설정 범위를 확인하세요.");

        var timer = Stopwatch.StartNew();
        using var inspection = ToBgr(inspectionImage);
        using var template = ToBgr(referenceImage);
        using var imageGray = new Mat();
        using var templateGray = new Mat();
        Cv2.CvtColor(inspection, imageGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);

        if (template.Width > imageGray.Width || template.Height > imageGray.Height)
            throw new ArgumentException("템플릿이 검사 이미지보다 큽니다. 부품 영역을 잘라 사용하세요.");
        if (template.Width < 5 || template.Height < 5)
            throw new ArgumentException("템플릿이 너무 작습니다.");

        Cv2.MeanStdDev(templateGray, out _, out Scalar stddev);
        if (stddev.Val0 < 1.0)
            throw new ArgumentException("템플릿의 명암 변화가 너무 작습니다.");

        var candidates = new List<Candidate>();
        using var bestMap = new Mat();
        double bestSimilarity = -1;
        Point bestPoint = default;
        Size bestSize = default;
        double bestAngle = 0;

        double maxRotation = settings.TemplateEnableRotation ? settings.TemplateMaxRotation : 0;
        double step = settings.TemplateRotationStep;
        var angles = new List<double> { 0 };
        if (maxRotation > 0)
        {
            for (double angle = step; angle <= maxRotation + 0.0001; angle += step)
            {
                angles.Add(angle);
                angles.Add(-angle);
            }
        }

        foreach (double angle in angles)
        {
            using var rotated = RotateTemplate(templateGray, angle);
            if (rotated.Width > imageGray.Width || rotated.Height > imageGray.Height)
                continue;
            Cv2.MeanStdDev(rotated, out _, out Scalar rotatedStd);
            if (rotatedStd.Val0 < 1.0)
                continue;

            using var response = new Mat();
            Cv2.MatchTemplate(imageGray, rotated, response, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(response, out _, out double peak, out _, out Point peakPoint);
            if (double.IsFinite(peak) && peak > bestSimilarity)
            {
                bestSimilarity = peak;
                bestPoint = peakPoint;
                bestSize = rotated.Size();
                bestAngle = angle;
                response.CopyTo(bestMap);
            }

            // 각 회전 각도별 상위 후보를 찾아 NMS로 중복 제거한다.
            using var working = response.Clone();
            int perAngleLimit = settings.TemplateEnableMultiMatch
                ? Math.Min(settings.TemplateMaxDetections * 3, 150) : 1;
            for (int i = 0; i < perAngleLimit; i++)
            {
                Cv2.MinMaxLoc(working, out _, out double score, out _, out Point point);
                if (!double.IsFinite(score) || score < settings.TemplateThreshold)
                    break;
                candidates.Add(new Candidate(
                    new Rect(point.X, point.Y, rotated.Width, rotated.Height), score, angle));
                int radiusX = Math.Max(1, rotated.Width / 2);
                int radiusY = Math.Max(1, rotated.Height / 2);
                int x0 = Math.Max(0, point.X - radiusX);
                int y0 = Math.Max(0, point.Y - radiusY);
                int x1 = Math.Min(working.Width, point.X + radiusX + 1);
                int y1 = Math.Min(working.Height, point.Y + radiusY + 1);
                Cv2.Rectangle(working, new Rect(x0, y0, x1 - x0, y1 - y0),
                    new Scalar(-2), -1);
            }
        }

        if (bestSimilarity < -0.999 || bestSize.Width == 0)
            throw new InvalidOperationException("유효한 템플릿 매칭 결과가 없습니다.");

        var selected = new List<Candidate>();
        foreach (Candidate candidate in candidates.OrderByDescending(c => c.Similarity))
        {
            if (selected.Any(x => IoU(x.Rect, candidate.Rect) > settings.TemplateNmsIoU))
                continue;
            selected.Add(candidate);
            if (selected.Count >= (settings.TemplateEnableMultiMatch ? settings.TemplateMaxDetections : 1))
                break;
        }

        bool similarityFailed = selected.Count == 0;
        bool countFailed = settings.TemplateEnableMultiMatch &&
                           selected.Count != settings.TemplateExpectedCount;
        bool positionFailed = false;
        double positionError = 0;
        if (settings.EnablePositionInspection && selected.Count > 0)
        {
            // 단일 부품 검사에서만 위치를 판정한다.
            if (!settings.TemplateEnableMultiMatch)
            {
                double dx = selected[0].Rect.X - settings.ExpectedX;
                double dy = selected[0].Rect.Y - settings.ExpectedY;
                positionError = Math.Sqrt(dx * dx + dy * dy);
                positionFailed = positionError > settings.PositionTolerance;
            }
        }

        bool ng = similarityFailed || countFailed || positionFailed;
        using var annotated = inspection.Clone();
        var defects = new List<DefectRegion>();
        foreach (Candidate candidate in selected)
        {
            bool itemNg = positionFailed && !settings.TemplateEnableMultiMatch;
            Scalar color = itemNg ? new Scalar(0, 0, 255) : new Scalar(0, 255, 0);
            Cv2.Rectangle(annotated, candidate.Rect, color, 2);
            Cv2.PutText(annotated, $"{candidate.Similarity:F2} {candidate.Angle:+0.#;-0.#;0}deg",
                new Point(candidate.Rect.X, Math.Max(20, candidate.Rect.Y - 6)),
                HersheyFonts.HersheySimplex, 0.5, color, 1);
        }
        if (settings.EnablePositionInspection && !settings.TemplateEnableMultiMatch)
        {
            Cv2.Rectangle(annotated,
                new Rect(settings.ExpectedX, settings.ExpectedY, template.Width, template.Height),
                new Scalar(255, 0, 0), 1);
        }
        // 개수 불일치는 배열 전체의 판정이다. 검출된 정상 패턴을 불량으로 표시하지 않는다.
        if (countFailed)
        {
            Cv2.PutText(annotated,
                $"COUNT NG: {selected.Count}/{settings.TemplateExpectedCount}",
                new Point(12, 30), HersheyFonts.HersheySimplex, 0.8,
                new Scalar(0, 0, 255), 2);
        }

        // 실제 위치 검사 실패 또는 매칭 실패일 때만 해당 후보를 강조한다.
        if (positionFailed || similarityFailed)
        {
            Rect region = selected.Count > 0 ? selected[0].Rect :
                new Rect(bestPoint.X, bestPoint.Y, bestSize.Width, bestSize.Height);
            Cv2.Rectangle(annotated, region, new Scalar(0, 0, 255), 2);
            defects.Add(new DefectRegion
            {
                X = region.X, Y = region.Y, Width = region.Width, Height = region.Height,
                Area = region.Width * region.Height,
                Score = Math.Clamp(1 - bestSimilarity, 0, 1)
            });
        }

        string message = similarityFailed
            ? $"NG: 매칭 실패 (최대 유사도 {bestSimilarity:F3}, 기준 {settings.TemplateThreshold:F3})"
            : countFailed
                ? $"NG: 검출 {selected.Count}개 / 예상 {settings.TemplateExpectedCount}개"
                : positionFailed
                    ? $"NG: 위치 오차 {positionError:F1}px / 허용 {settings.PositionTolerance:F1}px"
                    : $"OK: 검출 {selected.Count}개, 최고 유사도 {bestSimilarity:F3}, 회전 {bestAngle:F1}°";

        byte[]? processed = null;
        if (!bestMap.Empty())
        {
            using var map8 = new Mat();
            Cv2.Normalize(bestMap, map8, 0, 255, NormTypes.MinMax);
            using var converted = new Mat();
            map8.ConvertTo(converted, MatType.CV_8UC1);
            processed = OpenCvImageEncoder.Encode(converted);
        }
        timer.Stop();
        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = ng,
            ResultStatus = ng ? InspectionStatus.NG : InspectionStatus.OK,
            ProcessingTimeMs = timer.Elapsed.TotalMilliseconds,
            Score = bestSimilarity,
            Message = message,
            OriginalImage = OpenCvImageEncoder.Encode(inspection),
            ResultImage = OpenCvImageEncoder.Encode(annotated),
            ProcessedImage = processed,
            Defects = defects
        };
    }

    private static Mat RotateTemplate(Mat gray, double angle)
    {
        if (Math.Abs(angle) < 0.0001) return gray.Clone();
        var result = new Mat();
        var center = new Point2f(gray.Width / 2f, gray.Height / 2f);
        using var matrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        Cv2.WarpAffine(gray, result, matrix, gray.Size(), InterpolationFlags.Linear,
            BorderTypes.Reflect101);
        return result;
    }

    private static double IoU(Rect a, Rect b)
    {
        int x0 = Math.Max(a.Left, b.Left);
        int y0 = Math.Max(a.Top, b.Top);
        int x1 = Math.Min(a.Right, b.Right);
        int y1 = Math.Min(a.Bottom, b.Bottom);
        double intersection = Math.Max(0, x1 - x0) * (double)Math.Max(0, y1 - y0);
        double union = a.Width * (double)a.Height + b.Width * (double)b.Height - intersection;
        return union > 0 ? intersection / union : 0;
    }

    private static Mat ToBgr(Mat source)
    {
        var result = new Mat();
        switch (source.Channels())
        {
            case 1: Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR); break;
            case 3: source.CopyTo(result); break;
            case 4: Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR); break;
            default:
                result.Dispose();
                throw new NotSupportedException("지원하지 않는 이미지 채널 형식입니다.");
        }
        return result;
    }
}
