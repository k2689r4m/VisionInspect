using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionInspect.WPF.Services;
using VisionInspect.WPF.Services.OpenCv;

namespace VisionInspect.WPF.ViewModels;

public partial class OpenCvInspectionViewModel : ObservableObject
{
    private readonly OpenCvInspectionService _service = new();
    private readonly InspectionEngine _engine = new();
    private string? _imagePath;
    private string? _referencePath;

    public Array InspectionModes { get; } = Enum.GetValues(typeof(InspectionMode));

    [ObservableProperty] private BitmapImage? originalImage;
    [ObservableProperty] private BitmapImage? grayscaleImage;
    [ObservableProperty] private BitmapImage? binaryImage;
    [ObservableProperty] private BitmapImage? edgesImage;
    [ObservableProperty] private BitmapImage? contoursImage;
    [ObservableProperty] private BitmapImage? inspectionResultImage;
    [ObservableProperty] private BitmapImage? inspectionProcessedImage;
    [ObservableProperty] private string imageName = "No image selected";
    [ObservableProperty] private string referenceName = "No reference selected";
    [ObservableProperty] private string processingTime = "-";
    [ObservableProperty] private string imageSize = "-";
    [ObservableProperty] private string regionCount = "0";
    [ObservableProperty] private string inspectionStatus = "READY";
    [ObservableProperty] private string inspectionScore = "-";
    [ObservableProperty] private string inspectionTime = "-";
    [ObservableProperty] private string inspectionMessage = "검사 모드를 선택하고 RUN INSPECTION을 누르세요.";
    [ObservableProperty] private string statusMessage = "이미지를 선택하세요.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private InspectionMode selectedMode = InspectionMode.Difference;

    // Difference
    [ObservableProperty] private int threshold = 30;
    [ObservableProperty] private double minDefectArea = 50;
    [ObservableProperty] private bool enableAlignment = true;
    // Shape / Dimension
    [ObservableProperty] private double minObjectArea = 100;
    [ObservableProperty] private double minContourArea = 50;
    [ObservableProperty] private double maxContourArea = 100000;
    [ObservableProperty] private double minWidth = 0;
    [ObservableProperty] private double maxWidth = 100000;
    [ObservableProperty] private double minHeight = 0;
    [ObservableProperty] private double maxHeight = 100000;
    [ObservableProperty] private double minAspectRatio = 0;
    [ObservableProperty] private double maxAspectRatio = 100000;
    [ObservableProperty] private bool invertBinary;
    [ObservableProperty] private double minAngle = -90;
    [ObservableProperty] private double maxAngle = 90;
    [ObservableProperty] private double mmPerPixel = 1;
    // Template
    [ObservableProperty] private double templateThreshold = 0.8;
    [ObservableProperty] private bool enablePositionInspection;
    [ObservableProperty] private int expectedX;
    [ObservableProperty] private int expectedY;
    [ObservableProperty] private double positionTolerance = 10;
    [ObservableProperty] private bool templateEnableRotation;
    [ObservableProperty] private double templateMaxRotation = 10;
    [ObservableProperty] private double templateRotationStep = 2;
    [ObservableProperty] private bool templateEnableMultiMatch = true;
    [ObservableProperty] private int templateExpectedCount = 36;
    [ObservableProperty] private int templateMaxDetections = 50;
    [ObservableProperty] private double templateNmsIoU = 0.3;
    // Surface
    [ObservableProperty] private bool detectBrightDefects = true;
    [ObservableProperty] private bool detectDarkDefects = true;
    [ObservableProperty] private int surfaceThreshold = 25;
    [ObservableProperty] private int surfaceKernelSize = 15;
    [ObservableProperty] private double minSurfaceDefectArea = 20;
    // Color
    [ObservableProperty] private bool colorIsForbidden = true;
    [ObservableProperty] private int hueMin;
    [ObservableProperty] private int hueMax = 179;
    [ObservableProperty] private int saturationMin;
    [ObservableProperty] private int saturationMax = 255;
    [ObservableProperty] private int valueMin;
    [ObservableProperty] private int valueMax = 255;
    [ObservableProperty] private double minColorArea = 30;
    [ObservableProperty] private double minRequiredColorRatio = 5;
    // Feature
    [ObservableProperty] private int maxFeatures = 500;
    [ObservableProperty] private double matchRatio = 0.75;
    [ObservableProperty] private int minGoodMatches = 12;
    [ObservableProperty] private double minInlierRatio = 0.5;
    [ObservableProperty] private double ransacThreshold = 3;
    [ObservableProperty] private int maxDrawMatches = 50;

    public bool RequiresReference => SelectedMode is InspectionMode.Difference or InspectionMode.Template or InspectionMode.Feature;
    public bool IsDifference => SelectedMode == InspectionMode.Difference;
    public bool IsShape => SelectedMode == InspectionMode.Shape;
    public bool IsTemplate => SelectedMode == InspectionMode.Template;
    public bool IsDimension => SelectedMode == InspectionMode.Dimension;
    public bool IsSurface => SelectedMode == InspectionMode.Surface;
    public bool IsColor => SelectedMode == InspectionMode.Color;
    public bool IsFeature => SelectedMode == InspectionMode.Feature;

    public ObservableCollection<OpenCvRegion> Regions { get; } = [];
    public ObservableCollection<DefectRegion> Defects { get; } = [];

    partial void OnSelectedModeChanged(InspectionMode value)
    {
        OnPropertyChanged(nameof(RequiresReference));
        OnPropertyChanged(nameof(IsDifference));
        OnPropertyChanged(nameof(IsShape));
        OnPropertyChanged(nameof(IsTemplate));
        OnPropertyChanged(nameof(IsDimension));
        OnPropertyChanged(nameof(IsSurface));
        OnPropertyChanged(nameof(IsColor));
        OnPropertyChanged(nameof(IsFeature));
        ResetInspection();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OpenImageCommand.NotifyCanExecuteChanged();
        OpenReferenceCommand.NotifyCanExecuteChanged();
        RunInspectionCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenImage() => !IsBusy;
    private bool CanRunInspection() => !IsBusy && !string.IsNullOrWhiteSpace(_imagePath);

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
    private async Task OpenImageAsync()
    {
        var dialog = new OpenFileDialog { Title = "OpenCV 검사 이미지 선택", Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        StatusMessage = "OpenCV 이미지 처리 중...";
        try
        {
            var result = await Task.Run(() => _service.Inspect(dialog.FileName));
            _imagePath = dialog.FileName;
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
            foreach (var region in result.Regions) Regions.Add(region);
            ResetInspection();
            StatusMessage = "OpenCV 이미지 처리 완료. 검사 모드를 선택하세요.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"이미지 처리 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RunInspectionCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
    private void OpenReference()
    {
        var dialog = new OpenFileDialog { Title = SelectedMode == InspectionMode.Template ? "템플릿 이미지 선택" : "정상 기준 이미지 선택", Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog() != true) return;
        _referencePath = dialog.FileName;
        ReferenceName = Path.GetFileName(dialog.FileName);
        ResetInspection();
        StatusMessage = $"기준 이미지 선택: {ReferenceName}";
    }

    [RelayCommand(CanExecute = nameof(CanRunInspection))]
    private async Task RunInspectionAsync()
    {
        if (RequiresReference && string.IsNullOrWhiteSpace(_referencePath))
        {
            InspectionStatus = "ERROR";
            InspectionMessage = SelectedMode == InspectionMode.Template ? "템플릿 이미지를 선택하세요." : "정상 기준 이미지를 선택하세요.";
            StatusMessage = InspectionMessage;
            return;
        }

        var mode = SelectedMode;
        var path = _imagePath!;
        var reference = _referencePath;
        var settings = BuildSettings();
        IsBusy = true;
        InspectionStatus = "RUNNING";
        StatusMessage = $"{mode} 검사 중...";
        try
        {
            var result = await Task.Run(() => _engine.InspectFile(mode, path, reference, settings));
            InspectionResultImage = result.ResultImage.Length > 0 ? ToBitmap(result.ResultImage) : null;
            InspectionProcessedImage = result.ProcessedImage is { Length: > 0 } ? ToBitmap(result.ProcessedImage) : null;
            InspectionStatus = result.Status;
            InspectionScore = $"{result.Score:F3} (모드별 지표)";
            InspectionTime = $"{result.ProcessingTimeMs:F2} ms";
            InspectionMessage = result.Message;
            Defects.Clear();
            foreach (var defect in result.Defects) Defects.Add(defect);
            StatusMessage = $"{mode}: {result.Status} - {result.Message}";
        }
        catch (Exception ex)
        {
            InspectionStatus = "ERROR";
            InspectionScore = "-";
            InspectionTime = "-";
            InspectionMessage = ex.Message;
            InspectionResultImage = null;
            InspectionProcessedImage = null;
            Defects.Clear();
            StatusMessage = $"검사 실패: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private InspectionSettings BuildSettings() => new()
    {
        Threshold = Threshold,
        MinDefectArea = MinDefectArea,
        EnableAlignment = EnableAlignment,
        MinObjectArea = MinObjectArea,
        MinContourArea = MinContourArea,
        MaxContourArea = MaxContourArea,
        MinWidth = MinWidth,
        MaxWidth = MaxWidth,
        MinHeight = MinHeight,
        MaxHeight = MaxHeight,
        MinAspectRatio = MinAspectRatio,
        MaxAspectRatio = MaxAspectRatio,
        InvertBinary = InvertBinary,
        MinAngle = MinAngle,
        MaxAngle = MaxAngle,
        MmPerPixel = MmPerPixel,
        TemplateThreshold = TemplateThreshold,
        EnablePositionInspection = EnablePositionInspection,
        ExpectedX = ExpectedX,
        ExpectedY = ExpectedY,
        PositionTolerance = PositionTolerance,
        TemplateEnableRotation = TemplateEnableRotation,
        TemplateMaxRotation = TemplateMaxRotation,
        TemplateRotationStep = TemplateRotationStep,
        TemplateEnableMultiMatch = TemplateEnableMultiMatch,
        TemplateExpectedCount = TemplateExpectedCount,
        TemplateMaxDetections = TemplateMaxDetections,
        TemplateNmsIoU = TemplateNmsIoU,
        DetectBrightDefects = DetectBrightDefects,
        DetectDarkDefects = DetectDarkDefects,
        SurfaceThreshold = SurfaceThreshold,
        SurfaceKernelSize = SurfaceKernelSize,
        MinSurfaceDefectArea = MinSurfaceDefectArea,
        ColorIsForbidden = ColorIsForbidden,
        HueMin = HueMin,
        HueMax = HueMax,
        SaturationMin = SaturationMin,
        SaturationMax = SaturationMax,
        ValueMin = ValueMin,
        ValueMax = ValueMax,
        MinColorArea = MinColorArea,
        MinRequiredColorRatio = MinRequiredColorRatio,
        MaxFeatures = MaxFeatures,
        MatchRatio = MatchRatio,
        MinGoodMatches = MinGoodMatches,
        MinInlierRatio = MinInlierRatio,
        RansacThreshold = RansacThreshold,
        MaxDrawMatches = MaxDrawMatches
    };

    private void ResetInspection()
    {
        InspectionStatus = "READY";
        InspectionScore = "-";
        InspectionTime = "-";
        InspectionMessage = "검사 모드를 선택하고 RUN INSPECTION을 누르세요.";
        InspectionResultImage = null;
        InspectionProcessedImage = null;
        Defects.Clear();
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
