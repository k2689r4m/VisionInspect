
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VisionInspect.WPF.Models;

public sealed class BenchmarkReport
{
    [JsonPropertyName("benchmarkId")]
    public string BenchmarkId { get; set; } = "";

    [JsonPropertyName("dataset")]
    public string Dataset { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("results")]
    public List<BenchmarkModelResult> Results { get; set; } = [];
}

public sealed class BenchmarkModelResult
{
    [JsonPropertyName("modelKey")]
    public string ModelKey { get; set; } = "";

    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("imageAUROC")]
    public double? ImageAUROC { get; set; }

    [JsonPropertyName("imageF1")]
    public double? ImageF1 { get; set; }

    [JsonPropertyName("pixelAUROC")]
    public double? PixelAUROC { get; set; }

    [JsonPropertyName("pixelF1")]
    public double? PixelF1 { get; set; }

    [JsonPropertyName("evaluationTimeMs")]
    public double? EvaluationTimeMs { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public string ImageAUROCText => FormatMetric(ImageAUROC);
    public string ImageF1Text => FormatMetric(ImageF1);
    public string PixelAUROCText => FormatMetric(PixelAUROC);
    public string PixelF1Text => FormatMetric(PixelF1);

    public string EvaluationTimeText =>
        EvaluationTimeMs.HasValue
            ? $"{EvaluationTimeMs.Value / 1000.0:F2} s"
            : "N/A";

    public string StatusText =>
        Status.Equals("success", StringComparison.OrdinalIgnoreCase)
            ? "SUCCESS"
            : "FAILED";

    private static string FormatMetric(double? value) =>
        value.HasValue ? $"{value.Value:P2}" : "N/A";
}
