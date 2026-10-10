
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VisionInspect.WPF.Models;
using VisionInspect.WPF.Services;

namespace VisionInspect.WPF.ViewModels;

public sealed class BenchmarkMetricBar
{
    public string ModelName { get; set; } = "";

    public double Value { get; set; }

    public string ValueText => $"{Value:P2}";

    // 0~1 범위를 ProgressBar에 표시
    public double Percent =>
        Math.Clamp(Value * 100.0, 0.0, 100.0);
}

public partial class BenchmarkViewModel : ObservableObject
{
    private readonly BenchmarkApiService _api = new();

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Benchmark 결과를 불러오는 중...";

    [ObservableProperty]
    private string datasetName = "-";

    [ObservableProperty]
    private string categoryName = "-";

    [ObservableProperty]
    private string deviceName = "-";

    [ObservableProperty]
    private string createdAtText = "-";

    [ObservableProperty]
    private string bestImageModel = "-";

    [ObservableProperty]
    private string bestPixelModel = "-";

    [ObservableProperty]
    private string fastestModel = "-";

    [ObservableProperty]
    private List<BenchmarkModelResult> chartResults = [];

    public ObservableCollection<BenchmarkModelResult> Results { get; } = [];

    public ObservableCollection<BenchmarkMetricBar> ImageAUROCChart { get; } = [];

    public ObservableCollection<BenchmarkMetricBar> ImageF1Chart { get; } = [];

    public ObservableCollection<BenchmarkMetricBar> PixelAUROCChart { get; } = [];

    public ObservableCollection<BenchmarkMetricBar> PixelF1Chart { get; } = [];


    private bool CanRefresh() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Benchmark API 연결 중...";

        try
        {
            var report = await _api.GetLatestAsync();

            DatasetName = report.Dataset;
            CategoryName = report.Category;
            DeviceName = report.Device;

            CreatedAtText = report.CreatedAt == default
                ? "-"
                : report.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

            Results.Clear();

            foreach (var item in report.Results)
                Results.Add(item);

            ChartResults = report.Results.ToList();

            BuildChart(
                ImageAUROCChart,
                report.Results,
                x => x.ImageAUROC
            );

            BuildChart(
                ImageF1Chart,
                report.Results,
                x => x.ImageF1
            );

            BuildChart(
                PixelAUROCChart,
                report.Results,
                x => x.PixelAUROC
            );

            BuildChart(
                PixelF1Chart,
                report.Results,
                x => x.PixelF1
            );

            var successful = report.Results
                .Where(x => x.Status.Equals(
                    "success",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            BestImageModel = successful
                .Where(x => x.ImageAUROC.HasValue)
                .OrderByDescending(x => x.ImageAUROC)
                .Select(x => x.ModelName)
                .FirstOrDefault() ?? "-";

            BestPixelModel = successful
                .Where(x => x.PixelAUROC.HasValue)
                .OrderByDescending(x => x.PixelAUROC)
                .Select(x => x.ModelName)
                .FirstOrDefault() ?? "-";

            FastestModel = successful
                .Where(x => x.EvaluationTimeMs.HasValue)
                .OrderBy(x => x.EvaluationTimeMs)
                .Select(x => x.ModelName)
                .FirstOrDefault() ?? "-";

            StatusMessage =
                $"Benchmark 로드 완료 · {successful.Count}/{report.Results.Count}개 성공";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Benchmark 로드 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void BuildChart(
        ObservableCollection<BenchmarkMetricBar> target,
        System.Collections.Generic.IEnumerable<BenchmarkModelResult> results,
        Func<BenchmarkModelResult, double?> selector)
    {
        target.Clear();

        foreach (var item in results)
        {
            var value = selector(item);

            if (!value.HasValue || !double.IsFinite(value.Value))
                continue;

            target.Add(new BenchmarkMetricBar
            {
                ModelName = item.ModelName,
                Value = value.Value
            });
        }
    }
}
