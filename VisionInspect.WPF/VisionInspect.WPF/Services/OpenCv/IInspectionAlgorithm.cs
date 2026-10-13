using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public interface IInspectionAlgorithm
{
    InspectionMode Mode { get; }

    string Name { get; }

    bool RequiresReference { get; }

    InspectionResult Inspect(
        Mat inspectionImage,
        Mat? referenceImage,
        InspectionSettings settings);
}