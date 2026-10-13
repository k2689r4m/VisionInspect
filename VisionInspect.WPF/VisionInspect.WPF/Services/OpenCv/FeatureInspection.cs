using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class FeatureInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Feature;

    public string Name => "Feature Matching";

    public bool RequiresReference => true;

    public InspectionResult Inspect(
        Mat inspectionImage,
        Mat? referenceImage,
        InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");

        if (referenceImage is null || referenceImage.Empty())
            throw new ArgumentException("정상 기준 이미지가 필요합니다.");

        ArgumentNullException.ThrowIfNull(settings);

        ValidateSettings(settings);

        var stopwatch = Stopwatch.StartNew();

        using var inspection = NormalizeToBgr(inspectionImage);
        using var reference = NormalizeToBgr(referenceImage);

        using var inspectionGray = new Mat();
        using var referenceGray = new Mat();

        Cv2.CvtColor(
            inspection,
            inspectionGray,
            ColorConversionCodes.BGR2GRAY);

        Cv2.CvtColor(
            reference,
            referenceGray,
            ColorConversionCodes.BGR2GRAY);

        // 1. ORB 특징점 추출
        using var orb = ORB.Create(settings.MaxFeatures);

        using var referenceDescriptors = new Mat();
        using var inspectionDescriptors = new Mat();

        orb.DetectAndCompute(
            referenceGray,
            null,
            out KeyPoint[] referenceKeypoints,
            referenceDescriptors);

        orb.DetectAndCompute(
            inspectionGray,
            null,
            out KeyPoint[] inspectionKeypoints,
            inspectionDescriptors);

        if (referenceDescriptors.Empty() ||
            inspectionDescriptors.Empty())
        {
            return CreateNgResult(
                inspection, stopwatch, "특징점을 충분히 추출하지 못했습니다.");
        }

        // 2. BFMatcher (Hamming Distance)
        using var matcher = new BFMatcher(
            NormTypes.Hamming,
            crossCheck: false);

        DMatch[][] knnMatches = matcher.KnnMatch(
            referenceDescriptors,
            inspectionDescriptors,
            k: 2);

        // 3. Lowe Ratio Test
        var goodMatches = new List<DMatch>();

        foreach (DMatch[] pair in knnMatches)
        {
            if (pair.Length < 2)
                continue;

            if (pair[0].Distance <
                settings.MatchRatio * pair[1].Distance)
            {
                goodMatches.Add(pair[0]);
            }
        }

        if (goodMatches.Count < settings.MinGoodMatches)
        {
            return CreateNgResult(
                inspection, stopwatch,
                $"매칭점 부족: {goodMatches.Count}개 / 최소 {settings.MinGoodMatches}개");
        }

        // 4. 대응점 좌표 추출
        Point2f[] sourcePoints = goodMatches
            .Select(m => referenceKeypoints[m.QueryIdx].Pt)
            .ToArray();

        Point2f[] targetPoints = goodMatches
            .Select(m => inspectionKeypoints[m.TrainIdx].Pt)
            .ToArray();

        // 5. RANSAC 기반 Homography
        using var inlierMask = new Mat();

        using var homography = Cv2.FindHomography(
            InputArray.Create(sourcePoints),
            InputArray.Create(targetPoints),
            HomographyMethods.Ransac,
            settings.RansacThreshold,
            inlierMask);

        if (homography.Empty())
        {
            return CreateNgResult(
                inspection, stopwatch, "일치하는 물체를 찾지 못했습니다 (Homography 실패).");
        }

        // 6. Inlier 계산
        int inlierCount = Cv2.CountNonZero(inlierMask);

        double inlierRatio =
            goodMatches.Count == 0
                ? 0
                : (double)inlierCount / goodMatches.Count;

        bool isDefective =
            inlierCount < 4 || inlierRatio < settings.MinInlierRatio;

        // 7. 기준 이미지의 외곽선을 검사 이미지에 투영
        Point2f[] referenceCorners =
        [
            new Point2f(0, 0),
            new Point2f(reference.Width - 1, 0),
            new Point2f(reference.Width - 1, reference.Height - 1),
            new Point2f(0, reference.Height - 1)
        ];

        Point2f[] transformedCorners =
            Cv2.PerspectiveTransform(
                referenceCorners,
                homography);

        // 비정상적인 투영 결과 방어
        if (transformedCorners.Any(
            p => !float.IsFinite(p.X) ||
                 !float.IsFinite(p.Y) ||
                 Math.Abs(p.X) > 1_000_000 ||
                 Math.Abs(p.Y) > 1_000_000))
        {
            return CreateNgResult(
                inspection, stopwatch, "투영 좌표가 비정상적입니다. 일치하는 물체를 찾지 못했습니다.");
        }

        // 잘못된 매칭으로 생긴 뒤집힘/찌그러짐/극단적 크기 변환을 거부한다.
        if (!IsValidProjection(transformedCorners, reference.Size(), inspection.Size()))
        {
            return CreateNgResult(
                inspection, stopwatch, "투영된 물체의 크기 또는 형태가 비정상적입니다.");
        }

        using var resultImage = inspection.Clone();

        Scalar color = isDefective
            ? new Scalar(0, 0, 255)
            : new Scalar(0, 255, 0);

        Point[] corners = transformedCorners
            .Select(p => new Point(
                (int)Math.Round(p.X),
                (int)Math.Round(p.Y)))
            .ToArray();

        for (int i = 0; i < 4; i++)
        {
            Cv2.Line(
                resultImage,
                corners[i],
                corners[(i + 1) % 4],
                color,
                2);
        }

        // 8. 특징점 시각화
        // RANSAC Inlier만 화면에 표시
        int drawn = 0;

        for (int i = 0; i < goodMatches.Count; i++)
        {
            if (drawn >= settings.MaxDrawMatches)
                break;

            if (inlierMask.At<byte>(i, 0) == 0)
                continue;

            DMatch match = goodMatches[i];

            Point2f pt =
                inspectionKeypoints[match.TrainIdx].Pt;

            Cv2.Circle(
                resultImage,
                new Point(
                    (int)Math.Round(pt.X),
                    (int)Math.Round(pt.Y)),
                3,
                new Scalar(255, 255, 0),
                -1);

            drawn++;
        }

        string statusText = isDefective ? "NG" : "OK";

        Cv2.PutText(
            resultImage,
            $"{statusText} Inlier:{inlierRatio:P1}",
            new Point(10, 30),
            HersheyFonts.HersheySimplex,
            0.7,
            color,
            2);

        // 9. 매칭 결과 시각화
        using var matchingImage = new Mat();

        Cv2.DrawMatches(
            reference,
            referenceKeypoints,
            inspection,
            inspectionKeypoints,
            goodMatches
                .Where((m, i) => inlierMask.At<byte>(i, 0) != 0)
                .Take(settings.MaxDrawMatches)
                .ToArray(),
            matchingImage);

        stopwatch.Stop();

        return new InspectionResult
        {
            Mode = Mode,

            ResultStatus = isDefective
                ? InspectionStatus.NG
                : InspectionStatus.OK,

            IsDefective = isDefective,

            ProcessingTimeMs =
                stopwatch.Elapsed.TotalMilliseconds,

            // Feature 모드의 Score는 Inlier 비율 (%)
            Score = inlierRatio * 100.0,

            Message =
                $"Reference Features: {referenceKeypoints.Length}, " +
                $"Inspection Features: {inspectionKeypoints.Length}, " +
                $"Good Matches: {goodMatches.Count}, " +
                $"Inliers: {inlierCount}, " +
                $"Inlier Ratio: {inlierRatio:P1}, " +
                $"Threshold: {settings.MinInlierRatio:P1}, " +
                $"Result: {(isDefective ? "NG" : "OK")}",

            OriginalImage =
                OpenCvImageEncoder.Encode(inspection),

            ResultImage =
                OpenCvImageEncoder.Encode(resultImage),

            ProcessedImage =
                OpenCvImageEncoder.Encode(matchingImage),

            // 특징점 매칭은 개별 결함 영역을 산출하지 않음
            Defects = Array.Empty<DefectRegion>()
        };
    }

    private static bool IsValidProjection(Point2f[] corners, Size referenceSize, Size inspectionSize)
    {
        if (corners.Length != 4) return false;

        double signedArea = 0;
        for (int i = 0; i < 4; i++)
        {
            Point2f a = corners[i];
            Point2f b = corners[(i + 1) % 4];
            signedArea += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        double referenceArea = (double)referenceSize.Width * referenceSize.Height;
        double inspectionArea = (double)inspectionSize.Width * inspectionSize.Height;
        double area = signedArea / 2.0;
        if (area <= 0 || area < referenceArea * 0.01 ||
            area > Math.Max(referenceArea, inspectionArea) * 16.0)
            return false;

        // 투영 사각형이 검사 이미지와 전혀 겹치지 않는 경우를 제외한다.
        float minX = corners.Min(p => p.X);
        float maxX = corners.Max(p => p.X);
        float minY = corners.Min(p => p.Y);
        float maxY = corners.Max(p => p.Y);
        if (maxX < 0 || maxY < 0 ||
            minX >= inspectionSize.Width || minY >= inspectionSize.Height)
            return false;

        // 네 꼭짓점의 볼록성 확인 (뒤틀린 투영 방지)
        double sign = 0;
        for (int i = 0; i < 4; i++)
        {
            Point2f a = corners[i];
            Point2f b = corners[(i + 1) % 4];
            Point2f c = corners[(i + 2) % 4];
            double cross = ((double)b.X - a.X) * (c.Y - b.Y) -
                           ((double)b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) < 1e-6) return false;
            if (sign != 0 && cross * sign < 0) return false;
            sign = Math.Sign(cross);
        }
        return true;
    }

    private static InspectionResult CreateNgResult(Mat inspection, Stopwatch stopwatch, string message)
    {
        stopwatch.Stop();
        using var result = inspection.Clone();
        Cv2.PutText(result, "NG - No reliable match", new Point(10, 30),
            HersheyFonts.HersheySimplex, 0.65, new Scalar(0, 0, 255), 2);
        return new InspectionResult
        {
            Mode = InspectionMode.Feature,
            ResultStatus = InspectionStatus.NG,
            IsDefective = true,
            ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Score = 0,
            Message = message,
            OriginalImage = OpenCvImageEncoder.Encode(inspection),
            ResultImage = OpenCvImageEncoder.Encode(result),
            ProcessedImage = OpenCvImageEncoder.Encode(result),
            Defects = Array.Empty<DefectRegion>()
        };
    }

    private static InspectionResult CreateErrorResult(
        Mat inspection,
        Stopwatch stopwatch,
        string message)
    {
        stopwatch.Stop();

        byte[] image = OpenCvImageEncoder.Encode(inspection);

        return new InspectionResult
        {
            Mode = InspectionMode.Feature,

            ResultStatus = InspectionStatus.ERROR,

            IsDefective = false,

            ProcessingTimeMs =
                stopwatch.Elapsed.TotalMilliseconds,

            Score = 0,

            Message = message,

            OriginalImage = image,

            ResultImage = image,

            Defects = Array.Empty<DefectRegion>()
        };
    }

    private static void ValidateSettings(
        InspectionSettings settings)
    {
        if (settings.MaxFeatures < 4)
        {
            throw new ArgumentException(
                "MaxFeatures는 4 이상이어야 합니다.");
        }

        if (settings.MatchRatio <= 0 ||
            settings.MatchRatio >= 1)
        {
            throw new ArgumentException(
                "MatchRatio는 0과 1 사이여야 합니다.");
        }

        if (settings.MinGoodMatches < 4)
        {
            throw new ArgumentException(
                "MinGoodMatches는 4 이상이어야 합니다.");
        }

        if (settings.MinInlierRatio < 0 ||
            settings.MinInlierRatio > 1)
        {
            throw new ArgumentException(
                "MinInlierRatio는 0~1 범위여야 합니다.");
        }

        if (settings.RansacThreshold <= 0)
        {
            throw new ArgumentException(
                "RansacThreshold는 0보다 커야 합니다.");
        }

        if (settings.MaxDrawMatches < 1)
        {
            throw new ArgumentException(
                "MaxDrawMatches는 1 이상이어야 합니다.");
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