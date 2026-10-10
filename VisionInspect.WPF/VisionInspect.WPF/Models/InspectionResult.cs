
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VisionInspect.WPF.Models;

public sealed class InspectionResult
{
    [JsonPropertyName("inspectionId")]
    public string InspectionId { get; set; } = "";

    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("anomalyScore")]
    public double AnomalyScore { get; set; }

    [JsonPropertyName("imageThreshold")]
    public double? ImageThreshold { get; set; }

    [JsonPropertyName("pixelThreshold")]
    public double PixelThreshold { get; set; }

    [JsonPropertyName("processingTimeMs")]
    public double ProcessingTimeMs { get; set; }

    [JsonPropertyName("defectCount")]
    public int DefectCount { get; set; }

    [JsonPropertyName("defects")]
    public List<DefectInfo> Defects { get; set; } = [];

    [JsonPropertyName("outputs")]
    public InspectionOutputs Outputs { get; set; } = new();

    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = "";
}

public sealed class InspectionOutputs
{
    [JsonPropertyName("heatmap")]
    public string Heatmap { get; set; } = "";

    [JsonPropertyName("overlay")]
    public string Overlay { get; set; } = "";

    [JsonPropertyName("result")]
    public string Result { get; set; } = "";
}
