using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class SurfaceInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Surface;

    public string Name => "Surface Defect Detection";

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
        using var gray = new Mat();
        using var blurred = new Mat();

        Cv2.CvtColor(
            original,
            gray,
            ColorConversionCodes.BGR2GRAY);

        Cv2.GaussianBlur(
            gray,
            blurred,
            new Size(3, 3),
            0);

        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(
                settings.SurfaceKernelSize,
                settings.SurfaceKernelSize));

        using var topHat = new Mat();
        using var blackHat = new Mat();

        // 밝은 결함 후보 추출
        Cv2.MorphologyEx(
            blurred,
            topHat,
            MorphTypes.TopHat,
            kernel);

        // 어두운 결함 후보 추출
        Cv2.MorphologyEx(
            blurred,
            blackHat,
            MorphTypes.BlackHat,
            kernel);

        using var brightMask = new Mat();
        using var darkMask = new Mat();

        Cv2.Threshold(
            topHat,
            brightMask,
            settings.SurfaceThreshold,
            255,
            ThresholdTypes.Binary);

        Cv2.Threshold(
            blackHat,
            darkMask,
            settings.SurfaceThreshold,
            255,
            ThresholdTypes.Binary);

        // 검출 모드에 따라 마스크 병합
        using var combinedMask = new Mat(
            gray.Size(),
            MatType.CV_8UC1,
            Scalar.Black);

        if (settings.DetectBrightDefects &&
            settings.DetectDarkDefects)
        {
            Cv2.BitwiseOr(
                brightMask,
                darkMask,
                combinedMask);
        }
        else if (settings.DetectBrightDefects)
        {
            brightMask.CopyTo(combinedMask);
        }
        else
        {
            darkMask.CopyTo(combinedMask);
        }

        // 작은 잡음 제거
        using var noiseKernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(3, 3));

        using var cleanedMask = new Mat();

        Cv2.MorphologyEx(
            combinedMask,
            cleanedMask,
            MorphTypes.Open,
            noiseKernel);

        // 윤곽선 검출
        Cv2.FindContours(
            cleanedMask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        using var resultImage = original.Clone();

        var defects = new List<DefectRegion>();
        using var acceptedMask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.Black);
        int brightCount = 0;
        int darkCount = 0;

        foreach (Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);

            if (area < settings.MinSurfaceDefectArea)
                continue;

            Rect rect = Cv2.BoundingRect(contour);

            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            // 결함 영역 내부의 명암 차이만 비교한다.
            // BoundingRect 전체를 평균내면 주변 배경에 의해 점수가 희석된다.
            using var contourMask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.DrawContours(contourMask, new[] { contour }, -1, Scalar.White, -1);
            double brightScore = settings.DetectBrightDefects
                ? Cv2.Mean(topHat, contourMask).Val0 : 0;
            double darkScore = settings.DetectDarkDefects
                ? Cv2.Mean(blackHat, contourMask).Val0 : 0;

            bool isBright = settings.DetectBrightDefects &&
                (!settings.DetectDarkDefects || brightScore >= darkScore);

            if (isBright) brightCount++;
            else darkCount++;

            Cv2.DrawContours(acceptedMask, new[] { contour }, -1, Scalar.White, -1);

            Scalar color = isBright
                ? new Scalar(0, 255, 255) // 밝은 결함: 노란색
                : new Scalar(0, 0, 255);  // 어두운 결함: 빨간색

            Cv2.Rectangle(
                resultImage,
                rect,
                color,
                2);

            string label = $"#{defects.Count + 1} {(isBright ? "BRIGHT" : "DARK")} " +
                           $"A:{area:F0} S:{Math.Max(brightScore, darkScore):F0}";

            Cv2.PutText(
                resultImage,
                label,
                new Point(
                    Math.Max(0, rect.X),
                    Math.Max(18, rect.Y - 6)),
                HersheyFonts.HersheySimplex,
                0.4,
                color,
                1);

            defects.Add(new DefectRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Area = area,

                // 평균 명암 차이 (0~255)
                Score = Math.Max(brightScore, darkScore)
            });
        }

        stopwatch.Stop();

        double defectPixelRatio =
            Cv2.CountNonZero(acceptedMask) * 100.0
            / (acceptedMask.Rows * acceptedMask.Cols);

        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = defects.Count > 0,
            ResultStatus = defects.Count > 0 ? InspectionStatus.NG : InspectionStatus.OK,

            ProcessingTimeMs =
                stopwatch.Elapsed.TotalMilliseconds,

            // 마스크에서 결함 후보로 표시된 픽셀 비율
            Score = defectPixelRatio,

            Message = defects.Count > 0
                ? $"표면 결함 {defects.Count}개 검출 (밝음 {brightCount}, 어두움 {darkCount})"
                : "설정한 기준 이상의 표면 결함 후보가 없습니다.",

            OriginalImage =
                OpenCvImageEncoder.Encode(original),

            ResultImage =
                OpenCvImageEncoder.Encode(resultImage),

            ProcessedImage =
                OpenCvImageEncoder.Encode(acceptedMask),

            Defects = defects
        };
    }

    private static void ValidateSettings(InspectionSettings settings)
    {
        if (!settings.DetectBrightDefects &&
            !settings.DetectDarkDefects)
        {
            throw new ArgumentException(
                "밝은 결함 또는 어두운 결함 검출을 활성화해야 합니다.");
        }

        if (settings.SurfaceThreshold < 0 ||
            settings.SurfaceThreshold > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.SurfaceThreshold),
                "표면 검사 임계값은 0~255 사이여야 합니다.");
        }

        if (settings.SurfaceKernelSize < 3 ||
            settings.SurfaceKernelSize % 2 == 0)
        {
            throw new ArgumentException(
                "SurfaceKernelSize는 3 이상의 홀수여야 합니다.");
        }

        if (settings.MinSurfaceDefectArea < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.MinSurfaceDefectArea));
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
                    "지원하지 않는 이미지 채널 형식입니다.");
        }

        return result;
    }
}