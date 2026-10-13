using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public sealed class InspectionEngine
{
    private readonly Dictionary<InspectionMode, IInspectionAlgorithm> _algorithms;

    public InspectionEngine()
    {
        IInspectionAlgorithm[] algorithms =
        [
            new DifferenceInspection(),
            new ShapeInspection(),
            new TemplateInspection(),
            new DimensionInspection(),
            new SurfaceInspection(),
            new ColorInspection(),
            new FeatureInspection()
        ];
        _algorithms = algorithms.ToDictionary(a => a.Mode);
    }

    public InspectionResult InspectFile(InspectionMode mode, string inspectionPath,
        string? referencePath, InspectionSettings settings)
    {
        if (!_algorithms.TryGetValue(mode, out var algorithm))
            throw new NotSupportedException($"지원하지 않는 검사 모드: {mode}");
        if (string.IsNullOrWhiteSpace(inspectionPath))
            throw new ArgumentException("검사 이미지 경로가 없습니다.");
        using var inspection = Cv2.ImRead(inspectionPath, ImreadModes.Color);
        if (inspection.Empty()) throw new InvalidOperationException("검사 이미지를 읽을 수 없습니다.");
        using var reference = new Mat();
        if (algorithm.RequiresReference)
        {
            if (string.IsNullOrWhiteSpace(referencePath))
                throw new ArgumentException("기준 이미지가 필요합니다.");
            using var loaded = Cv2.ImRead(referencePath, ImreadModes.Color);
            if (loaded.Empty()) throw new InvalidOperationException("기준 이미지를 읽을 수 없습니다.");
            loaded.CopyTo(reference);
        }
        return algorithm.Inspect(inspection, algorithm.RequiresReference ? reference : null, settings);
    }
}
