
using System.Text.Json.Serialization;

namespace VisionInspect.WPF.Models;

public sealed class DefectInfo
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("area")]
    public double Area { get; set; }
}
