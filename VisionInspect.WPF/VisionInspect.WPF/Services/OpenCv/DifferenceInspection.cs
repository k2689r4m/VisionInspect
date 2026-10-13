using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

/// <summary>
/// 기준 영상과 검사 영상의 픽셀 차이를 비교하는 검사기.
/// Score: 유효 검사 영역 중 최종 결함 픽셀 비율(%)
/// DefectRegion.Score: 결함 영역 내 평균 밝기 차이(0~255)
/// </summary>
public sealed class DifferenceInspection : IInspectionAlgorithm
{
    public InspectionMode Mode => InspectionMode.Difference;
    public string Name => "Image Difference";
    public bool RequiresReference => true;

    public InspectionResult Inspect(Mat inspectionImage, Mat? referenceImage, InspectionSettings settings)
    {
        if (inspectionImage is null || inspectionImage.Empty())
            throw new ArgumentException("검사 이미지가 없습니다.");
        if (referenceImage is null || referenceImage.Empty())
            throw new ArgumentException("정상 기준 이미지가 필요합니다.");
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        Validate(settings);
        var stopwatch = Stopwatch.StartNew();

        using var inspection = NormalizeToBgr(inspectionImage);
        using var reference = NormalizeToBgr(referenceImage);

        // 해상도가 다른 영상의 강제 리사이즈는 치수와 결함을 왜곡할 수 있다.
        // 동일 카메라/해상도에서 촬영한 영상을 사용하도록 요구한다.
        if (inspection.Size() != reference.Size())
            return Error("기준 이미지와 검사 이미지의 해상도가 다릅니다. 동일 해상도의 이미지를 사용하세요.",
                inspection, stopwatch);

        using var alignedReference = new Mat();
        using var validMask = new Mat(inspection.Size(), MatType.CV_8UC1, Scalar.All(255));

        if (settings.EnableAlignment)
        {
            if (!TryAlignReference(reference, inspection, alignedReference, validMask,
                    settings, out string alignmentMessage))
                return Error($"이미지 정렬 실패: {alignmentMessage}", inspection, stopwatch);
        }
        else
        {
            reference.CopyTo(alignedReference);
        }

        // 정렬 과정에서 보간된 가장자리 픽셀을 검사에서 제외한다.
        if (settings.AlignmentBorderMargin > 0)
        {
            int radius = settings.AlignmentBorderMargin;
            using var borderKernel = Cv2.GetStructuringElement(MorphShapes.Rect,
                new Size(radius * 2 + 1, radius * 2 + 1));
            using var eroded = new Mat();
            Cv2.Erode(validMask, eroded, borderKernel);
            eroded.CopyTo(validMask);
        }

        int validPixels = Cv2.CountNonZero(validMask);
        if (validPixels < inspection.Rows * inspection.Cols * 0.5)
            return Error("정렬 후 유효 검사 영역이 전체 이미지의 50% 미만입니다.", inspection, stopwatch);

        using var inspectionGray = new Mat();
        using var referenceGray = new Mat();
        Cv2.CvtColor(inspection, inspectionGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(alignedReference, referenceGray, ColorConversionCodes.BGR2GRAY);

        using var correctedReference = new Mat();
        if (settings.EnableBrightnessCompensation)
        {
            // 유효 영역의 전역 평균 밝기 차이를 보정한다.
            // 국소적인 조명 불균일은 별도 보정이 필요하다.
            double delta = Cv2.Mean(inspectionGray, validMask).Val0
                         - Cv2.Mean(referenceGray, validMask).Val0;
            referenceGray.ConvertTo(correctedReference, MatType.CV_8UC1, 1.0, delta);
        }
        else
        {
            referenceGray.CopyTo(correctedReference);
        }

        using var inspectionBlur = new Mat();
        using var referenceBlur = new Mat();
        int blur = settings.DifferenceBlurSize;
        Cv2.GaussianBlur(inspectionGray, inspectionBlur, new Size(blur, blur), 0);
        Cv2.GaussianBlur(correctedReference, referenceBlur, new Size(blur, blur), 0);

        using var difference = new Mat();
        Cv2.Absdiff(inspectionBlur, referenceBlur, difference);

        using var binary = new Mat();
        Cv2.Threshold(difference, binary, settings.Threshold, 255, ThresholdTypes.Binary);
        Cv2.BitwiseAnd(binary, validMask, binary);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect,
            new Size(settings.DifferenceMorphSize, settings.DifferenceMorphSize));
        using var opened = new Mat();
        using var connected = new Mat();
        Cv2.MorphologyEx(binary, opened, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(opened, connected, MorphTypes.Close, kernel);
        Cv2.BitwiseAnd(connected, validMask, connected);

        // OpenCV의 FindContours는 입력 영상을 수정할 수 있으므로 복사본 사용.
        using var contourInput = connected.Clone();
        Cv2.FindContours(contourInput, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        using var resultImage = inspection.Clone();
        using var acceptedMask = new Mat(connected.Size(), MatType.CV_8UC1, Scalar.All(0));
        var defects = new List<DefectRegion>();

        foreach (var contour in contours)
        {
            using var componentMask = new Mat(connected.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.FillPoly(componentMask, new[] { contour }, Scalar.All(255));
            Cv2.BitwiseAnd(componentMask, connected, componentMask);

            // contour 기하학적 면적보다 실제 이진화 픽셀 개수를 기준으로 필터링.
            int area = Cv2.CountNonZero(componentMask);
            if (area < settings.MinDefectArea)
                continue;

            Rect rect = Cv2.BoundingRect(contour);
            Cv2.BitwiseOr(acceptedMask, componentMask, acceptedMask);
            Cv2.Rectangle(resultImage, rect, new Scalar(0, 0, 255), 2);

            defects.Add(new DefectRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Area = area,
                Score = Cv2.Mean(difference, componentMask).Val0
            });
        }

        double changedPixelRatio = Cv2.CountNonZero(acceptedMask) * 100.0 / validPixels;
        bool isDefective = defects.Count > 0;
        stopwatch.Stop();

        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = isDefective,
            ResultStatus = isDefective ? InspectionStatus.NG : InspectionStatus.OK,
            ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Score = changedPixelRatio,
            Message = isDefective
                ? $"결함 후보 {defects.Count}개 검출 (변화 면적 {changedPixelRatio:F3}%)."
                : "설정된 기준 이상의 차이 영역이 검출되지 않았습니다.",
            OriginalImage = OpenCvImageEncoder.Encode(inspection),
            ResultImage = OpenCvImageEncoder.Encode(resultImage),
            ProcessedImage = OpenCvImageEncoder.Encode(acceptedMask),
            Defects = defects
        };
    }

    private InspectionResult Error(string message, Mat inspection, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new InspectionResult
        {
            Mode = Mode,
            IsDefective = false,
            ResultStatus = InspectionStatus.ERROR,
            ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Score = 0,
            Message = message,
            OriginalImage = OpenCvImageEncoder.Encode(inspection),
            ResultImage = OpenCvImageEncoder.Encode(inspection),
            Defects = Array.Empty<DefectRegion>()
        };
    }

    private static void Validate(InspectionSettings s)
    {
        if (s.Threshold < 0 || s.Threshold > 255)
            throw new ArgumentOutOfRangeException(nameof(s.Threshold), "0~255 범위여야 합니다.");
        if (s.MinDefectArea < 0)
            throw new ArgumentOutOfRangeException(nameof(s.MinDefectArea));
        if (s.DifferenceBlurSize < 1 || s.DifferenceBlurSize % 2 == 0)
            throw new ArgumentException("DifferenceBlurSize는 1 이상의 홀수여야 합니다.");
        if (s.DifferenceMorphSize < 1 || s.DifferenceMorphSize % 2 == 0)
            throw new ArgumentException("DifferenceMorphSize는 1 이상의 홀수여야 합니다.");
        if (s.AlignmentMinMatches < 4)
            throw new ArgumentException("AlignmentMinMatches는 4 이상이어야 합니다.");
        if (s.AlignmentMinInlierRatio < 0 || s.AlignmentMinInlierRatio > 1)
            throw new ArgumentException("AlignmentMinInlierRatio는 0~1 범위여야 합니다.");
        if (s.AlignmentBorderMargin < 0)
            throw new ArgumentException("AlignmentBorderMargin은 0 이상이어야 합니다.");
        if (s.AlignmentMaxScaleChange < 0 || s.AlignmentMaxScaleChange >= 1)
            throw new ArgumentException("AlignmentMaxScaleChange는 0 이상 1 미만이어야 합니다.");
        if (s.AlignmentMaxPerspectiveShift < 0)
            throw new ArgumentException("AlignmentMaxPerspectiveShift는 0 이상이어야 합니다.");
    }

    private static Mat NormalizeToBgr(Mat source)
    {
        var result = new Mat();
        switch (source.Channels())
        {
            case 1:
                Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);
                break;
            case 3:
                source.CopyTo(result);
                break;
            case 4:
                Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR);
                break;
            default:
                result.Dispose();
                throw new NotSupportedException("지원하지 않는 이미지 채널 형식입니다.");
        }
        return result;
    }

    private static bool TryAlignReference(Mat reference, Mat inspection, Mat output,
        Mat validMask, InspectionSettings settings, out string message)
    {
        message = "특징점을 찾을 수 없습니다.";
        using var refGray = new Mat();
        using var inspectGray = new Mat();
        Cv2.CvtColor(reference, refGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(inspection, inspectGray, ColorConversionCodes.BGR2GRAY);

        using var orb = ORB.Create(1500);
        using var refDescriptors = new Mat();
        using var inspectDescriptors = new Mat();
        orb.DetectAndCompute(refGray, null, out KeyPoint[] refKeys, refDescriptors);
        orb.DetectAndCompute(inspectGray, null, out KeyPoint[] inspectKeys, inspectDescriptors);
        if (refDescriptors.Empty() || inspectDescriptors.Empty())
            return false;

        using var matcher = new BFMatcher(NormTypes.Hamming);
        DMatch[][] pairs = matcher.KnnMatch(refDescriptors, inspectDescriptors, 2);
        var good = new List<DMatch>();
        foreach (var pair in pairs)
        {
            if (pair.Length >= 2 && pair[0].Distance < 0.75 * pair[1].Distance)
                good.Add(pair[0]);
        }
        if (good.Count < settings.AlignmentMinMatches)
        {
            message = $"유효 특징점 매칭 부족 ({good.Count}/{settings.AlignmentMinMatches}).";
            return false;
        }

        var src = new Point2f[good.Count];
        var dst = new Point2f[good.Count];
        for (int i = 0; i < good.Count; i++)
        {
            src[i] = refKeys[good[i].QueryIdx].Pt;
            dst[i] = inspectKeys[good[i].TrainIdx].Pt;
        }

        using var inlierMask = new Mat();
        using var homography = Cv2.FindHomography(
            InputArray.Create(src), InputArray.Create(dst),
            HomographyMethods.Ransac, 3.0, inlierMask);
        if (homography.Empty())
        {
            message = "Homography 계산에 실패했습니다.";
            return false;
        }

        double inlierRatio = Cv2.CountNonZero(inlierMask) / (double)good.Count;
        if (inlierRatio < settings.AlignmentMinInlierRatio)
        {
            message = $"정렬 신뢰도 부족 (inlier {inlierRatio:P1}).";
            return false;
        }

        // 네 모서리를 변환하여 지나친 확대/축소·원근 왜곡을 검사한다.
        double h00 = homography.At<double>(0, 0);
        double h01 = homography.At<double>(0, 1);
        double h02 = homography.At<double>(0, 2);
        double h10 = homography.At<double>(1, 0);
        double h11 = homography.At<double>(1, 1);
        double h12 = homography.At<double>(1, 2);
        double h20 = homography.At<double>(2, 0);
        double h21 = homography.At<double>(2, 1);
        double h22 = homography.At<double>(2, 2);

        if (Math.Abs(h22) < 1e-10)
        {
            message = "비정상적인 변환 행렬입니다.";
            return false;
        }

        double w = reference.Width;
        double h = reference.Height;
        var corners = new[] { (x: 0.0, y: 0.0), (x: w, y: 0.0),
                              (x: w, y: h), (x: 0.0, y: h) };
        var mapped = new (double x, double y)[4];
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = corners[i];
            double d = h20 * x + h21 * y + h22;
            if (Math.Abs(d) < 1e-10)
            {
                message = "변환된 이미지의 좌표가 유효하지 않습니다.";
                return false;
            }
            mapped[i] = ((h00 * x + h01 * y + h02) / d,
                         (h10 * x + h11 * y + h12) / d);
            if (!double.IsFinite(mapped[i].x) || !double.IsFinite(mapped[i].y))
            {
                message = "변환된 이미지의 좌표가 유효하지 않습니다.";
                return false;
            }
        }

        static double Distance((double x, double y) a, (double x, double y) b)
            => Math.Sqrt(Math.Pow(a.x - b.x, 2) + Math.Pow(a.y - b.y, 2));

        double top = Distance(mapped[0], mapped[1]) / w;
        double bottom = Distance(mapped[3], mapped[2]) / w;
        double left = Distance(mapped[0], mapped[3]) / h;
        double right = Distance(mapped[1], mapped[2]) / h;
        double minScale = 1.0 - settings.AlignmentMaxScaleChange;
        double maxScale = 1.0 + settings.AlignmentMaxScaleChange;
        foreach (double scale in new[] { top, bottom, left, right })
        {
            if (scale < minScale || scale > maxScale)
            {
                message = "허용 범위를 벗어난 이미지 크기 변형이 감지되었습니다.";
                return false;
            }
        }
        if (Math.Abs(top - bottom) > settings.AlignmentMaxPerspectiveShift ||
            Math.Abs(left - right) > settings.AlignmentMaxPerspectiveShift)
        {
            message = "원근 왜곡이 허용 범위를 벗어났습니다.";
            return false;
        }

        Cv2.WarpPerspective(reference, output, homography, inspection.Size(),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

        using var sourceMask = new Mat(reference.Size(), MatType.CV_8UC1, Scalar.All(255));
        Cv2.WarpPerspective(sourceMask, validMask, homography, inspection.Size(),
            InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
        message = $"정렬 성공 (inlier {inlierRatio:P1}).";
        return true;
    }
}
