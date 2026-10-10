
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisionInspect.WPF.Models;
using VisionInspect.WPF.Services;

namespace VisionInspect.WPF.ViewModels;

public partial class ComparisonViewModel : ObservableObject
{
    private readonly VisionApiService _api = new();

    [ObservableProperty]
    private string selectedImagePath = "";

    [ObservableProperty]
    private string statusMessage = "비교할 이미지를 선택하세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string selectedView = "Defects";

    [ObservableProperty]
    private AlgorithmComparisonItem? patchCore;

    [ObservableProperty]
    private AlgorithmComparisonItem? paDim;

    [ObservableProperty]
    private AlgorithmComparisonItem? efficientAD;

    [ObservableProperty]
    private ImageSource? selectedImage;

    [ObservableProperty]
    private bool hasResults;

    [ObservableProperty]
    private double speedDifferenceMs;

    public bool ShowDefects => SelectedView == "Defects";

    public bool CanRun =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SelectedImagePath);

    partial void OnIsBusyChanged(bool value)
    {
        OpenImageCommand.NotifyCanExecuteChanged();
        RunComparisonCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedImagePathChanged(string value)
    {
        RunComparisonCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedViewChanged(string value)
    {
        OnPropertyChanged(nameof(ShowDefects));
    }

    private bool CanOpen() => !IsBusy;

    private bool CanCompare() => CanRun;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "비교 검사 이미지 선택",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog() != true)
            return;

        SelectedImagePath = dialog.FileName;
        SelectedImage = LoadLocalImage(dialog.FileName);

        PatchCore = null;
        PaDim = null;
        EfficientAD = null;

        HasResults = false;
        SpeedDifferenceMs = 0;

        StatusMessage =
            $"이미지 선택: {Path.GetFileName(dialog.FileName)}";
    }

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task RunComparisonAsync()
    {
        IsBusy = true;
        HasResults = false;

        PatchCore = null;
        PaDim = null;
        EfficientAD = null;

        try
        {
            await InspectOneAsync(
                "patchcore",
                "PatchCore",
                item => PatchCore = item
            );

            await InspectOneAsync(
                "padim",
                "PaDiM",
                item => PaDim = item
            );

            await InspectOneAsync(
                "efficientad",
                "EfficientAD",
                item => EfficientAD = item
            );

            var times = new List<double>();

            if (PatchCore != null)
                times.Add(PatchCore.ProcessingTimeMs);

            if (PaDim != null)
                times.Add(PaDim.ProcessingTimeMs);

            if (EfficientAD != null)
                times.Add(EfficientAD.ProcessingTimeMs);

            if (times.Count >= 2)
            {
                SpeedDifferenceMs =
                    Math.Max(
                        times[0],
                        Math.Max(
                            times.Count > 1 ? times[1] : 0,
                            times.Count > 2 ? times[2] : 0
                        )
                    ) - Math.Min(
                        times[0],
                        Math.Min(
                            times.Count > 1 ? times[1] : times[0],
                            times.Count > 2 ? times[2] : times[0]
                        )
                    );
            }
            else
            {
                SpeedDifferenceMs = 0;
            }

            HasResults = times.Count > 0;

            StatusMessage =
                $"비교 완료: {times.Count}/3개 모델 성공";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InspectOneAsync(
        string modelKey,
        string displayName,
        Action<AlgorithmComparisonItem> update)
    {
        StatusMessage = $"{displayName} 검사 중...";

        try
        {
            var item = await InspectModelAsync(
                modelKey,
                displayName
            );

            update(UpdateView(item, SelectedView));
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"{displayName} 검사 실패: {ex.Message}";
        }
    }

    private async Task<AlgorithmComparisonItem> InspectModelAsync(
        string modelKey,
        string displayName)
    {
        var result = await _api.InspectAsync(
            SelectedImagePath,
            modelKey
        );

        var heatmap = await _api.DownloadImageAsync(
            result.Outputs.Heatmap
        );

        var overlay = await _api.DownloadImageAsync(
            result.Outputs.Overlay
        );

        var resultImage = await _api.DownloadImageAsync(
            result.Outputs.Result
        );

        return new AlgorithmComparisonItem
        {
            ModelName = displayName,
            Result = result.Result,
            AnomalyScore = result.AnomalyScore,
            ImageThreshold = result.ImageThreshold,
            ProcessingTimeMs = result.ProcessingTimeMs,
            DefectCount = result.DefectCount,
            Defects = new List<DefectInfo>(
                result.Defects
            ),
            OriginalImage = SelectedImage,
            HeatmapImage = LoadImage(heatmap),
            OverlayImage = LoadImage(overlay),
            ResultImage = LoadImage(resultImage),
            DisplayedImage = SelectedImage
        };
    }

    [RelayCommand]
    private void ChangeView(string view)
    {
        SelectedView = view;

        if (PatchCore != null)
            PatchCore = UpdateView(PatchCore, view);

        if (PaDim != null)
            PaDim = UpdateView(PaDim, view);

        if (EfficientAD != null)
            EfficientAD = UpdateView(EfficientAD, view);
    }

    private static AlgorithmComparisonItem UpdateView(
        AlgorithmComparisonItem item,
        string view)
    {
        return new AlgorithmComparisonItem
        {
            ModelName = item.ModelName,
            Result = item.Result,
            AnomalyScore = item.AnomalyScore,
            ImageThreshold = item.ImageThreshold,
            ProcessingTimeMs = item.ProcessingTimeMs,
            DefectCount = item.DefectCount,
            Defects = item.Defects,

            OriginalImage = item.OriginalImage,
            HeatmapImage = item.HeatmapImage,
            OverlayImage = item.OverlayImage,
            ResultImage = item.ResultImage,

            DisplayedImage = view switch
            {
                "Original" => item.OriginalImage,
                "Heatmap" => item.HeatmapImage,
                "Overlay" => item.OverlayImage,
                "Defects" => item.OriginalImage,
                _ => item.OriginalImage
            }
        };
    }

    private static BitmapSource LoadLocalImage(string path)
    {
        var image = new BitmapImage();

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        return image;
    }

    private static BitmapSource LoadImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var image = new BitmapImage();

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }
}
