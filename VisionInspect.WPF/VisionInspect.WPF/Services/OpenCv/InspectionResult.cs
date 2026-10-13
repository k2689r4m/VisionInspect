using OpenCvSharp;

namespace VisionInspect.WPF.Services.OpenCv;

public enum InspectionStatus
{
    OK,
    NG,
    ERROR
}

public sealed class DefectRegion
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public double Area { get; init; }
    public double Score { get; init; }
}

public sealed class InspectionResult
{
    public InspectionMode Mode { get; init; }

    public bool IsDefective { get; init; }

    public InspectionStatus? ResultStatus { get; init; }

    public string Status =>
        (ResultStatus ??
            (IsDefective ? InspectionStatus.NG : InspectionStatus.OK))
        .ToString();

    public double ProcessingTimeMs { get; init; }

    public double Score { get; init; }

    public string Message { get; init; } = string.Empty;

    public byte[] OriginalImage { get; init; } = [];

    public byte[] ResultImage { get; init; } = [];

    public byte[]? ProcessedImage { get; init; }

    public IReadOnlyList<DefectRegion> Defects { get; init; }
        = [];

    public int DefectCount => Defects.Count;
}

public static class OpenCvImageEncoder
{
    public static byte[] Encode(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] buffer);
        return buffer;
    }
}