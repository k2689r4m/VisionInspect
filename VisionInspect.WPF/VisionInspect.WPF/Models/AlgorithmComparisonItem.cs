
using System.Collections.Generic;
using System.Windows.Media;

namespace VisionInspect.WPF.Models;

public sealed class AlgorithmComparisonItem
{
    public string ModelName { get; set; } = "";

    public string Result { get; set; } = "READY";

    public double AnomalyScore { get; set; }

    public double? ImageThreshold { get; set; }

    public double ProcessingTimeMs { get; set; }

    public int DefectCount { get; set; }

    public List<DefectInfo> Defects { get; set; } = [];

    public ImageSource? OriginalImage { get; set; }

    public ImageSource? HeatmapImage { get; set; }

    public ImageSource? OverlayImage { get; set; }

    public ImageSource? ResultImage { get; set; }

    public ImageSource? DisplayedImage { get; set; }

    public Brush ResultBrush => Result switch
    {
        "OK" => Brushes.LimeGreen,
        "NG" => Brushes.OrangeRed,
        _ => Brushes.LightGray
    };

    public string ThresholdText =>
        ImageThreshold.HasValue
            ? $"Threshold: {ImageThreshold.Value:F6}"
            : "Threshold: N/A";
}
