using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspect.WPF.Services;

namespace VisionInspect.WPF.ViewModels;

public partial class OpenCvInspectionViewModel : ObservableObject
{
    private readonly OpenCvInspectionService _service = new();

    [ObservableProperty]
    private BitmapImage? originalImage;

    [ObservableProperty]
    private BitmapImage? grayscaleImage;

    [ObservableProperty]
    private BitmapImage? binaryImage;

    [ObservableProperty]
    private BitmapImage? edgesImage;

    [ObservableProperty]
    private BitmapImage? contoursImage;

    [ObservableProperty]
    private string imageName = "No image selected";

    [ObservableProperty]
    private string processingTime = "-";

    [ObservableProperty]
    private string imageSize = "-";

    [ObservableProperty]
    private string regionCount = "0";

    [ObservableProperty]
    private string statusMessage = "이미지를 선택하세요.";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<OpenCvRegion> Regions { get; } = [];

    private bool CanOpenImage() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OpenImageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
    private async Task OpenImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "OpenCV 검사 이미지 선택",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        StatusMessage = "OpenCV 이미지 처리 중...";

        try
        {
            var result = await Task.Run(
                () => _service.Inspect(dialog.FileName));

            OriginalImage = ToBitmap(result.Original);
            GrayscaleImage = ToBitmap(result.Grayscale);
            BinaryImage = ToBitmap(result.Binary);
            EdgesImage = ToBitmap(result.Edges);
            ContoursImage = ToBitmap(result.Contours);

            ImageName = Path.GetFileName(dialog.FileName);
            ImageSize = $"{result.Width} × {result.Height}";
            ProcessingTime = $"{result.ProcessingTimeMs:F2} ms";
            RegionCount = result.Regions.Count.ToString();

            Regions.Clear();

            foreach (var region in result.Regions)
                Regions.Add(region);

            StatusMessage = "OpenCV 이미지 처리 완료";
        }
        catch (Exception ex)
        {
            StatusMessage = $"검사 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }
}